using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises dust and address poisoning: a transaction that sends tiny amounts to many addresses (dusting, to link
/// them when they are spent together), and a tiny payment to or from an address that looks like another address in
/// the graph in its first and last characters (address poisoning, to make the victim copy the wrong address from the
/// history). The trail is not to follow either. Checks with an area of the sort Obfuscation; judgements, never facts
/// (principle 2).
/// </summary>
internal sealed class DustCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "dust";

    /// <summary>The largest amount, in satoshi, that is dust here.</summary>
    public const long DustLimit = 1_000;

    /// <summary>How many addresses a transaction must send dust to, to be dusting.</summary>
    public const int MinimumDusted = 10;

    /// <summary>
    /// The largest amount, in satoshi, a payment from or to a look-alike address may have to be poisoning.
    /// </summary>
    public const long PoisonLimit = 10_000;

    /// <summary>
    /// How many characters at the start (after the address's prefix) and at the end two addresses share to look
    /// alike.
    /// </summary>
    public const int LookAlikeCharacters = 4;

    /// <summary>
    /// The amounts inscriptions (ordinals) are usually sent with: 330 satoshi (the smallest for SegWit) and 546 (the
    /// smallest for P2PKH). Dust of only these amounts is said to be possibly inscriptions.
    /// </summary>
    public static readonly IReadOnlySet<long> InscriptionPostage = new HashSet<long> { 330, 546 };

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Damm och förgiftning";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var findings = input.Transactions
            .Select(transaction => (Transaction: transaction, Dusted: Dusted(transaction)))
            .Where(entry => entry.Dusted.Count >= MinimumDusted)
            .Select(entry => DescribeDusting(entry.Transaction, entry.Dusted))
            .ToList();
        findings.AddRange(Poisonings(input));
        return [.. findings.OrderBy(finding => finding.Key, StringComparer.Ordinal)];
    }

    /// <summary>Gives a transaction's outputs of dust, at most <see cref="DustLimit"/>.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The outputs.</returns>
    private static IReadOnlyList<BitcoinOutput> Dusted(BitcoinTransaction transaction) =>
        [.. transaction.PayingOutputs().Where(output => output.Amount.Value is > 0 and <= DustLimit)];

    /// <summary>Describes dusting.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="dusted">Its outputs of dust.</param>
    /// <returns>The check.</returns>
    private CheckFinding DescribeDusting(BitcoinTransaction transaction, IReadOnlyList<BitcoinOutput> dusted)
    {
        var count = CheckText.Count(dusted.Select(output => output.Address).Distinct().Count());
        var postage = dusted.All(output => InscriptionPostage.Contains(output.Amount.Value));
        var maybeInscriptions = postage
            ? " Alla belopp är 330 eller 546 satoshi, de vanliga beloppen för inskriptioner (ordinals), så det kan "
                + "lika gärna vara inskriptioner som skickas ut som damm."
            : string.Empty;
        var why = $"Analysen {this.Named()} ser att tx {CheckText.Short(transaction.Txid)} skickar högst "
            + $"{CheckText.Count((int)DustLimit)} satoshi var till {count} adresser. Så små belopp är damm: den som "
            + "skickar det hoppas att mottagarna spenderar det tillsammans med andra mynt, så att adresserna kan "
            + $"slås ihop.{maybeInscriptions} Spåret ska inte följa dammet, och ett damm-mynt som spenderas "
            + "tillsammans med andra är ett svagare tecken på samma ägare. Det är en bedömning, inte ett faktum.";
        return new CheckFinding(
            "dust:" + transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            postage ? $"Damm eller inskriptioner till {count} adresser" : $"Damm till {count} adresser",
            $"Högst {CheckText.Count((int)DustLimit)} satoshi var",
            why,
            [.. transaction.InputAddresses()],
            [transaction.Txid],
            [CheckBasis.Of(transaction)],
            []);
    }

    /// <summary>
    /// Finds the look-alike addresses: an address in a tiny payment (at most <see cref="PoisonLimit"/>, as payer or
    /// payee) that looks like another address in the graph without being it.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>The checks, one per look-alike address.</returns>
    private IEnumerable<CheckFinding> Poisonings(CheckInput input)
    {
        var all = input.Transactions
            .SelectMany(transaction => transaction.Inputs.Select(spent => spent.Address)
                .Concat(transaction.Outputs.Select(output => output.Address)))
            .OfType<BitcoinAddress>()
            .Distinct()
            .ToList();
        var byLook = all.ToLookup(Look);
        var seen = new HashSet<BitcoinAddress>();
        foreach (var transaction in input.Transactions)
        {
            foreach (var (suspect, amount) in TinyParties(transaction))
            {
                var genuine = byLook[Look(suspect)].Where(other => other != suspect).ToList();
                if (genuine.Count > 0 && seen.Add(suspect))
                {
                    yield return DescribePoisoning(transaction, suspect, amount, genuine);
                }
            }
        }
    }

    /// <summary>
    /// Gives the addresses in a transaction's tiny payment: the payers when it pays at most
    /// <see cref="PoisonLimit"/> in all, and the payees of outputs of at most that.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>Each address with the amount it sent or got.</returns>
    private static IEnumerable<(BitcoinAddress Address, Satoshi Amount)> TinyParties(BitcoinTransaction transaction)
    {
        var paying = transaction.PayingOutputs();
        var total = paying.Aggregate(Satoshi.Zero, (sum, output) => sum + output.Amount);
        if (total.Value is > 0 and <= PoisonLimit)
        {
            foreach (var payer in transaction.InputAddresses())
            {
                yield return (payer, total);
            }
        }

        foreach (var output in paying.Where(output => output.Amount.Value is > 0 and <= PoisonLimit))
        {
            yield return (output.Address!, output.Amount);
        }
    }

    /// <summary>
    /// Gives how an address looks to a person who glances at it: its type and its first and last
    /// <see cref="LookAlikeCharacters"/> characters after the prefix (<c>bc1q</c>, <c>bc1p</c>, <c>1</c> or <c>3</c>).
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The look.</returns>
    internal static (BitcoinScriptType Type, string Start, string End) Look(BitcoinAddress address)
    {
        // A Bech32 address is read without regard to case, and its prefix is the part before the last '1' and the
        // witness version after it; a Base58 address's case matters, and its prefix is its first character.
        var value = address.Value;
        var bech32 = value.StartsWith("bc1", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("tb1", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("bcrt1", StringComparison.OrdinalIgnoreCase);
        if (bech32)
        {
            value = value.ToLowerInvariant();
        }

        var prefix = bech32 ? value.LastIndexOf('1') + 2 : 1;
        var start = value.Length >= prefix + LookAlikeCharacters
            ? value.Substring(prefix, LookAlikeCharacters)
            : value[prefix..];
        return (address.Type, start, value[^LookAlikeCharacters..]);
    }

    /// <summary>Describes a look-alike address.</summary>
    /// <param name="transaction">The tiny payment.</param>
    /// <param name="suspect">The look-alike address.</param>
    /// <param name="amount">What it sent or got.</param>
    /// <param name="genuine">The addresses in the graph it looks like.</param>
    /// <returns>The check.</returns>
    private CheckFinding DescribePoisoning(
        BitcoinTransaction transaction, BitcoinAddress suspect, Satoshi amount, IReadOnlyList<BitcoinAddress> genuine)
    {
        var why = $"Analysen {this.Named()} ser att {suspect.Value} liknar {genuine[0].Value} i början och slutet, "
            + $"men är en annan adress, och att den bara har ett litet belopp ({CheckText.Count((int)amount.Value)} "
            + $"satoshi) i tx {CheckText.Short(transaction.Txid)}. Så ser adressförgiftning ut: den liknande adressen "
            + "läggs i offrets historik i hopp om att offret kopierar den nästa gång. Spåret ska inte följa den, och "
            + "jämför alltid hela adressen. Det är en bedömning, inte ett faktum: två adresser kan likna varandra av "
            + "en slump.";
        return new CheckFinding(
            "poison:" + suspect.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            "Trolig adressförgiftning",
            $"{CheckText.Short(suspect)} liknar {CheckText.Short(genuine[0])}",
            why,
            [suspect, .. genuine],
            [transaction.Txid],
            [CheckBasis.Of(transaction)],
            []);
    }
}
