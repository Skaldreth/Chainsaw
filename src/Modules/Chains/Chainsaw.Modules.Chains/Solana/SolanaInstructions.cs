using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>What a parsed instruction moves.</summary>
internal enum SolanaMoveKind
{
    /// <summary>
    /// Lamports from one account to another, with the amount the instruction names (the System program's
    /// <c>transfer</c>, <c>transferWithSeed</c>, <c>createAccount</c>, <c>createAccountWithSeed</c> and
    /// <c>withdrawNonceAccount</c>).
    /// </summary>
    Lamports,

    /// <summary>A token from one token account to another (<c>transfer</c> and <c>transferChecked</c>).</summary>
    Token,

    /// <summary>A token created into a token account by its mint (<c>mintTo</c> and <c>mintToChecked</c>).</summary>
    Mint,

    /// <summary>A token destroyed from a token account (<c>burn</c> and <c>burnChecked</c>).</summary>
    Burn,

    /// <summary>
    /// A token account closed, its lamports all going to a destination (<c>closeAccount</c>). The instruction does not
    /// name the amount: it is what the account holds when it is closed.
    /// </summary>
    Close,
}

/// <summary>
/// One movement a parsed instruction makes, in the order the transaction ran them: the accounts as the instruction
/// names them (token accounts, not their owners), and the amount when it names one.
/// </summary>
/// <param name="Kind">What it moves.</param>
/// <param name="From">The account the value leaves (the mint for <see cref="SolanaMoveKind.Mint"/>).</param>
/// <param name="To">The account that receives it (the mint for <see cref="SolanaMoveKind.Burn"/>).</param>
/// <param name="Amount">
/// The amount in lamports or in the token's smallest unit, or <see langword="null"/> for a close.
/// </param>
/// <param name="Mint">The token's mint when the instruction names it, or <see langword="null"/>.</param>
/// <param name="Decimals">The token's decimals when the instruction names them, or <see langword="null"/>.</param>
internal sealed record SolanaMove(
    SolanaMoveKind Kind, EthereumAddress From, EthereumAddress To, BigInteger? Amount, string? Mint, int? Decimals);

/// <summary>A token account's mint and owner as an instruction that makes it names them.</summary>
/// <param name="Account">The token account.</param>
/// <param name="Mint">The mint.</param>
/// <param name="Owner">The owner.</param>
internal sealed record SolanaTokenAccountHint(EthereumAddress Account, string Mint, EthereumAddress Owner);

/// <summary>
/// Reads the instructions of a transaction answered with <c>encoding: jsonParsed</c>: the top-level ones and their
/// inner instructions, in the order they ran, keeping those of the System program, the Token programs (classic and
/// Token-2022) and the Associated Token Account program that move value or name a token account's owner. An
/// instruction of any other program is not parsed by the node, and whatever it moves is seen only in the balances.
/// </summary>
internal static class SolanaInstructions
{
    /// <summary>The System program's name in a parsed instruction.</summary>
    private const string SystemProgram = "system";

    /// <summary>The Associated Token Account program's name in a parsed instruction.</summary>
    private const string AssociatedTokenProgram = "spl-associated-token-account";

    /// <summary>The Token programs' names in a parsed instruction: the classic one and Token-2022.</summary>
    private static readonly HashSet<string> TokenPrograms =
        new(StringComparer.Ordinal) { "spl-token", "spl-token-2022" };

    /// <summary>Reads the movements and the token accounts' owners, in the order the transaction ran.</summary>
    /// <param name="message">The transaction's message.</param>
    /// <param name="meta">The transaction's meta.</param>
    /// <returns>The movements and the owners the instructions name.</returns>
    /// <exception cref="FormatException">A parsed instruction lacks what its type must name.</exception>
    public static (List<SolanaMove> Moves, List<SolanaTokenAccountHint> Hints) Read(
        JsonElement message, JsonElement meta)
    {
        var moves = new List<SolanaMove>();
        var hints = new List<SolanaTokenAccountHint>();
        var inner = InnerByIndex(meta);
        var index = 0;
        foreach (var instruction in Instructions(message))
        {
            Take(instruction, moves, hints);
            foreach (var called in inner.GetValueOrDefault(index) ?? [])
            {
                Take(called, moves, hints);
            }

            index++;
        }

        return (moves, hints);
    }

    /// <summary>Gives the message's top-level instructions.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The instructions; none when the list is missing.</returns>
    private static IEnumerable<JsonElement> Instructions(JsonElement message) =>
        message.TryGetProperty("instructions", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
            : [];

    /// <summary>Gives the inner instructions by the index of the top-level instruction that ran them.</summary>
    /// <param name="meta">The meta.</param>
    /// <returns>The inner instructions in the order they ran.</returns>
    private static Dictionary<int, List<JsonElement>> InnerByIndex(JsonElement meta)
    {
        var groups = new Dictionary<int, List<JsonElement>>();
        if (!meta.TryGetProperty("innerInstructions", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return groups;
        }

        foreach (var group in list.EnumerateArray())
        {
            var at = group.TryGetProperty("index", out var number) && number.TryGetInt32(out var value)
                ? value
                : throw new FormatException("An inner instruction group has no index.");
            groups[at] = group.TryGetProperty("instructions", out var called) && called.ValueKind == JsonValueKind.Array
                ? [.. called.EnumerateArray()]
                : [];
        }

        return groups;
    }

    /// <summary>Takes one instruction's movement or owner, when its program is one that is read.</summary>
    /// <param name="instruction">The instruction.</param>
    /// <param name="moves">Where a movement is added.</param>
    /// <param name="hints">Where an owner is added.</param>
    private static void Take(JsonElement instruction, List<SolanaMove> moves, List<SolanaTokenAccountHint> hints)
    {
        if (!instruction.TryGetProperty("parsed", out var parsed) || parsed.ValueKind != JsonValueKind.Object
            || Text(instruction, "program") is not { } program || Text(parsed, "type") is not { } type
            || !parsed.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (program == SystemProgram && SystemMove(type, info) is { } lamports)
        {
            moves.Add(lamports);
        }
        else if (TokenPrograms.Contains(program))
        {
            TokenInstruction(type, info, moves, hints);
        }
        else if (program == AssociatedTokenProgram && type is "create" or "createIdempotent")
        {
            hints.Add(new SolanaTokenAccountHint(
                Address(info, "account"), Address(info, "mint").Value, Address(info, "wallet")));
        }
    }

    /// <summary>Reads a System instruction that moves lamports.</summary>
    /// <param name="type">The instruction's type.</param>
    /// <param name="info">Its parsed fields.</param>
    /// <returns>The movement, or <see langword="null"/> for one that moves nothing.</returns>
    private static SolanaMove? SystemMove(string type, JsonElement info) => type switch
    {
        "transfer" or "transferWithSeed" =>
            Lamports(Address(info, "source"), Address(info, "destination"), info),
        "createAccount" or "createAccountWithSeed" =>
            Lamports(Address(info, "source"), Address(info, "newAccount"), info),
        "withdrawNonceAccount" =>
            Lamports(Address(info, "nonceAccount"), Address(info, "destination"), info),
        _ => null,
    };

    /// <summary>
    /// Reads a Token instruction that moves a token or closes an account, or names an account's owner.
    /// </summary>
    /// <param name="type">The instruction's type.</param>
    /// <param name="info">Its parsed fields.</param>
    /// <param name="moves">Where a movement is added.</param>
    /// <param name="hints">Where an owner is added.</param>
    private static void TokenInstruction(
        string type, JsonElement info, List<SolanaMove> moves, List<SolanaTokenAccountHint> hints)
    {
        switch (type)
        {
            case "transfer":
                moves.Add(new SolanaMove(SolanaMoveKind.Token, Address(info, "source"), Address(info, "destination"),
                    Units(Text(info, "amount")), null, null));
                break;
            case "transferChecked":
                moves.Add(Checked(SolanaMoveKind.Token, Address(info, "source"), Address(info, "destination"), info));
                break;
            case "mintTo":
                moves.Add(new SolanaMove(SolanaMoveKind.Mint, Address(info, "mint"), Address(info, "account"),
                    Units(Text(info, "amount")), Address(info, "mint").Value, null));
                break;
            case "mintToChecked":
                moves.Add(Checked(SolanaMoveKind.Mint, Address(info, "mint"), Address(info, "account"), info));
                break;
            case "burn":
                moves.Add(new SolanaMove(SolanaMoveKind.Burn, Address(info, "account"), Address(info, "mint"),
                    Units(Text(info, "amount")), Address(info, "mint").Value, null));
                break;
            case "burnChecked":
                moves.Add(Checked(SolanaMoveKind.Burn, Address(info, "account"), Address(info, "mint"), info));
                break;
            case "closeAccount":
                moves.Add(new SolanaMove(
                    SolanaMoveKind.Close, Address(info, "account"), Address(info, "destination"), null, null, null));
                break;
            case "initializeAccount" or "initializeAccount2" or "initializeAccount3":
                hints.Add(new SolanaTokenAccountHint(
                    Address(info, "account"), Address(info, "mint").Value, Address(info, "owner")));
                break;
        }
    }

    /// <summary>Makes a movement of lamports with the instruction's amount.</summary>
    /// <param name="from">The account the lamports leave.</param>
    /// <param name="to">The account that receives them.</param>
    /// <param name="info">The parsed fields, with <c>lamports</c>.</param>
    /// <returns>The movement.</returns>
    private static SolanaMove Lamports(EthereumAddress from, EthereumAddress to, JsonElement info) =>
        new(SolanaMoveKind.Lamports, from, to,
            info.TryGetProperty("lamports", out var value) && value.TryGetUInt64(out var lamports)
                ? new BigInteger(lamports)
                : throw new FormatException("lamports is missing."),
            null, null);

    /// <summary>
    /// Makes a token movement of a checked instruction, which names the mint, the amount and the decimals.
    /// </summary>
    /// <param name="kind">What it moves.</param>
    /// <param name="from">The account (or mint) the token leaves.</param>
    /// <param name="to">The account (or mint) that receives it.</param>
    /// <param name="info">The parsed fields, with <c>mint</c> and <c>tokenAmount</c>.</param>
    /// <returns>The movement.</returns>
    private static SolanaMove Checked(SolanaMoveKind kind, EthereumAddress from, EthereumAddress to, JsonElement info)
    {
        if (!info.TryGetProperty("tokenAmount", out var amount) || amount.ValueKind != JsonValueKind.Object
            || !amount.TryGetProperty("decimals", out var decimals) || !decimals.TryGetInt32(out var places))
        {
            throw new FormatException("tokenAmount is missing.");
        }

        return new SolanaMove(kind, from, to, Units(Text(amount, "amount")), Address(info, "mint").Value, places);
    }

    /// <summary>Reads an address a parsed instruction names.</summary>
    /// <param name="info">The parsed fields.</param>
    /// <param name="name">The field.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">It is missing or not a Solana address.</exception>
    private static EthereumAddress Address(JsonElement info, string name) =>
        EthereumAddress.TryParseSolana(Text(info, name), out var address, out _)
            ? address
            : throw new FormatException($"{name} is not a Solana address.");

    /// <summary>Reads an amount the instruction writes as a string of digits.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The amount.</returns>
    private static BigInteger Units(string? text) =>
        BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var units)
            ? units
            : throw new FormatException("An amount is not a whole number.");

    /// <summary>Reads a field that is a string, if it is there.</summary>
    /// <param name="item">The element.</param>
    /// <param name="name">The field.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
