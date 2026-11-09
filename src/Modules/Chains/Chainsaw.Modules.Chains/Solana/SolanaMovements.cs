using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// What a Solana transaction moved, read from what it changed in its accounts' balances: the chain's own record of
/// lamports and token amounts before and after, whatever instructions and inner instructions it ran (principle 2).
/// </summary>
/// <param name="Transfers">
/// The movements between two parties, in a fixed order (see <see cref="SolanaMovements"/>).
/// </param>
/// <param name="Problems">
/// What could not be read as movements, in Swedish: an asset with several senders and several receivers, or balances
/// that do not add up. Empty when everything was read.
/// </param>
/// <param name="Skipped">How many tokens were not counted, for decimals Chainsaw cannot count in.</param>
internal sealed record SolanaMoves(
    IReadOnlyList<EthereumTransfer> Transfers, IReadOnlyList<string> Problems, int Skipped)
{
    /// <summary>Gets whether every change in the balances was read as movements.</summary>
    public bool IsComplete => Problems.Count == 0;
}

/// <summary>
/// Reads a Solana transaction's movements from its balances. Per asset, each party's change is counted:
/// <list type="bullet">
/// <item>SOL by account (<c>preBalances</c>/<c>postBalances</c>), with the fee given back to the payer, since the fee
/// is not a movement to anyone (it is given as the transaction's fee); a wrapped-SOL token account's lamports are its
/// owner's, as its token balance says, so that SOL paid out of an exchange's pool comes from the pool's owner, as the
/// token it was swapped for goes to;</item>
/// <item>each SPL token by the token accounts' owner (<c>preTokenBalances</c>/<c>postTokenBalances</c>), so that a
/// token account is knit to its owner and the movement is between owners; an account missing before or after counts as
/// zero (created or closed in the transaction). Wrapped SOL is not counted as a token, since its amount is the
/// account's own lamports, already counted as SOL.</item>
/// </list>
/// The changes are then paired only where the arithmetic leaves one answer: with one party that lost and any number
/// that gained (or the reverse), each gained what it gained from that one. A token whose amounts do not add up to
/// zero was created or destroyed: when only parties that gained (or only parties that lost) are left, the mint stands
/// on the other side. Anything else (several that lost and several that gained) cannot be paired from the balances; it
/// is left out and said, never guessed. A failed transaction moved nothing.
/// </summary>
internal static class SolanaMovements
{
    /// <summary>Reads a transaction's movements.</summary>
    /// <param name="raw">The transaction.</param>
    /// <param name="block">Its block, as every movement in it carries it.</param>
    /// <returns>The movements, and what could not be read.</returns>
    public static SolanaMoves Read(SolanaTransactionRaw raw, EthereumBlockReference block)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(block);
        if (raw.Failed)
        {
            return new SolanaMoves([], [], 0);
        }

        var problems = new List<string>();
        var pairs = new List<(EthereumAddress From, EthereumAddress To, AssetAmount Amount)>();
        pairs.AddRange(Sol(raw, problems));
        var skipped = 0;
        foreach (var (asset, changes) in Tokens(raw, ref skipped))
        {
            pairs.AddRange(Pair(asset, changes, Mint(asset), problems));
        }

        var transfers = pairs.Select((pair, position) => new EthereumTransfer(
                raw.Hash,
                pair.Amount.Asset.Kind == AssetKind.Native ? EthereumTransferKind.Native : EthereumTransferKind.Token,
                pair.From,
                pair.To,
                pair.Amount,
                position,
                block))
            .ToList();
        return new SolanaMoves(transfers, problems, skipped);
    }

    /// <summary>Reads the SOL each account gained or lost, with the fee given back to the payer.</summary>
    /// <param name="raw">The transaction.</param>
    /// <param name="problems">Where a problem is told.</param>
    /// <returns>The SOL movements.</returns>
    private static IEnumerable<(EthereumAddress From, EthereumAddress To, AssetAmount Amount)> Sol(
        SolanaTransactionRaw raw, List<string> problems)
    {
        var wrapped = raw.PreTokens.Concat(raw.PostTokens)
            .Where(balance => balance.Mint == KnownTokens.SolanaWrappedSolMint && balance.Owner is not null)
            .GroupBy(balance => balance.AccountIndex)
            .ToDictionary(group => group.Key, group => group.First().Owner!);
        var changes = new List<(EthereumAddress Party, BigInteger Change)>();
        for (var index = 0; index < raw.Accounts.Count; index++)
        {
            var change = raw.Post[index] - raw.Pre[index] + (index == 0 ? raw.Fee : BigInteger.Zero);
            Add(changes, wrapped.GetValueOrDefault(index) ?? raw.Accounts[index], change);
        }

        if (changes.Aggregate(BigInteger.Zero, (sum, each) => sum + each.Change) != BigInteger.Zero)
        {
            // Lamports cannot come from nowhere in a transaction; balances that do not add up are not read at all.
            problems.Add("Tx:ens SOL före och efter går inte ihop med avgiften; SOL räknas inte för den.");
            return [];
        }

        return Pair(Asset.Sol, changes, null, problems);
    }

    /// <summary>
    /// Reads each token's change per owner, one token at a time in the order of their mints, leaving out wrapped SOL
    /// and tokens with more decimals than Chainsaw can count in.
    /// </summary>
    /// <param name="raw">The transaction.</param>
    /// <param name="skipped">Counts the tokens left out.</param>
    /// <returns>Each token and its changes per owner.</returns>
    private static List<(Asset Asset, List<(EthereumAddress Party, BigInteger Change)> Changes)> Tokens(
        SolanaTransactionRaw raw, ref int skipped)
    {
        var result = new List<(Asset, List<(EthereumAddress, BigInteger)>)>();
        var mints = raw.PreTokens.Concat(raw.PostTokens)
            .Where(balance => balance.Mint != KnownTokens.SolanaWrappedSolMint)
            .GroupBy(balance => balance.Mint, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var mint in mints)
        {
            var decimals = mint.Select(balance => balance.Decimals).Distinct().ToList();
            if (decimals is not [var places] || places > Asset.MaximumDecimals)
            {
                skipped++;
                continue;
            }

            var asset = KnownTokens.Canonical(Asset.Token(Chain.Solana, mint.Key, Symbol(mint.Key), places));
            var changes = new List<(EthereumAddress Party, BigInteger Change)>();
            foreach (var account in mint.Select(balance => balance.AccountIndex).Distinct().Order())
            {
                var before = Find(raw.PreTokens, account, mint.Key);
                var after = Find(raw.PostTokens, account, mint.Key);
                var owner = after?.Owner ?? before?.Owner ?? raw.Accounts[account];
                Add(changes, owner, (after?.Amount ?? BigInteger.Zero) - (before?.Amount ?? BigInteger.Zero));
            }

            result.Add((asset, changes));
        }

        return result;
    }

    /// <summary>
    /// Pairs the changes of one asset into movements where the arithmetic leaves one answer; says so and gives none
    /// where it does not.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <param name="changes">Each party's change, in the order of the transaction's accounts.</param>
    /// <param name="mint">The token's mint, which stands on the other side of what was created or destroyed.</param>
    /// <param name="problems">Where a problem is told.</param>
    /// <returns>The movements.</returns>
    private static List<(EthereumAddress From, EthereumAddress To, AssetAmount Amount)> Pair(
        Asset asset, List<(EthereumAddress Party, BigInteger Change)> changes, EthereumAddress? mint,
        List<string> problems)
    {
        var lost = changes.Where(change => change.Change.Sign < 0).ToList();
        var gained = changes.Where(change => change.Change.Sign > 0).ToList();
        var sum = changes.Aggregate(BigInteger.Zero, (total, each) => total + each.Change);
        if (sum != BigInteger.Zero && mint is not null && (lost.Count == 0 || gained.Count == 0))
        {
            // Created (only parties that gained) or destroyed (only parties that lost): the mint is the other side.
            (lost.Count == 0 ? lost : gained).Add((mint, -sum));
        }
        else if (sum != BigInteger.Zero)
        {
            problems.Add($"{asset.Symbol} i tx:en går inte ihop före och efter; det räknas inte för den.");
            return [];
        }

        if (lost.Count == 1)
        {
            return [.. gained.Select(each => (lost[0].Party, each.Party, new AssetAmount(asset, each.Change)))];
        }

        if (gained.Count == 1)
        {
            return [.. lost.Select(each => (each.Party, gained[0].Party, new AssetAmount(asset, -each.Change)))];
        }

        if (lost.Count > 1)
        {
            problems.Add($"Tx:en flyttar {asset.Symbol} mellan flera avsändare och flera mottagare; vem som gav till "
                + "vem står bara i instruktionerna, som Chainsaw inte läser än, så det räknas inte.");
        }

        return [];
    }

    /// <summary>Adds a party's change, summing it with what the same party already changed.</summary>
    /// <param name="changes">The changes so far, in the order the parties were met.</param>
    /// <param name="party">The party.</param>
    /// <param name="change">Its change.</param>
    private static void Add(List<(EthereumAddress Party, BigInteger Change)> changes, EthereumAddress party,
        BigInteger change)
    {
        var index = changes.FindIndex(each => each.Party == party);
        if (index < 0)
        {
            changes.Add((party, change));
        }
        else
        {
            changes[index] = (party, changes[index].Change + change);
        }
    }

    /// <summary>Finds a token account's balance for a mint.</summary>
    /// <param name="balances">The balances before or after.</param>
    /// <param name="account">The account's place among the transaction's accounts.</param>
    /// <param name="mint">The mint.</param>
    /// <returns>The balance, or <see langword="null"/> when the account had none (it did not exist).</returns>
    private static SolanaTokenBalance? Find(IReadOnlyList<SolanaTokenBalance> balances, int account, string mint) =>
        balances.FirstOrDefault(balance => balance.AccountIndex == account
            && string.Equals(balance.Mint, mint, StringComparison.Ordinal));

    /// <summary>Gives a token's mint as an address, for the side of what was created or destroyed.</summary>
    /// <param name="asset">The token.</param>
    /// <returns>The mint.</returns>
    private static EthereumAddress Mint(Asset asset) => EthereumAddress.Parse(Chain.Solana, asset.Contract!);

    /// <summary>
    /// Gives a token's symbol: the known token's own, otherwise a short form of its mint, since the chain's balances do
    /// not name the token and a name a source gave would say nothing about what it is.
    /// </summary>
    /// <param name="mint">The mint.</param>
    /// <returns>The symbol.</returns>
    private static string Symbol(string mint) => "SPL " + mint[..4] + "…";
}
