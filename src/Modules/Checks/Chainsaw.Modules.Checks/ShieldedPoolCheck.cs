using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Suggests likely ways out of a shielded pool (CLAUDE.md, <em>Privacy coins</em>): for money that goes into Zcash's
/// pools (t→z) or into Litecoin's MWEB (a peg-in), the payments out of the same pool in the graph (z→t, or a peg-out
/// in a block's integration transaction) that may be the same money after a fee, weighed by the amount and the time
/// after. Inside the pool nothing ties the two together; the method from the research is that users often take out
/// what they put in, soon after, so every candidate is a judgement, always dashed (principle 2).
/// </summary>
/// <remarks>Only what the graph holds is weighed; the chain around the deposit is not searched.</remarks>
internal sealed class ShieldedPoolCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "shielded-pool";

    /// <summary>The analysis's name and version, which the user's decisions about its suggestions are kept by.</summary>
    public static readonly HeuristicId Identity = new(Name, 1);

    /// <summary>
    /// The largest share of the deposit the way out may be smaller by: the pools take only the network's fee, so a
    /// way out that is the same money is within a per cent.
    /// </summary>
    public const double MostShortfall = 0.01;

    /// <summary>The lowest score a candidate is kept at.</summary>
    public const double LowestScore = 0.4;

    /// <summary>The most candidates kept per deposit, the best first.</summary>
    public const int MostCandidates = 5;

    /// <summary>The weight of the amount in the score.</summary>
    private const double AmountWeight = 0.6;

    /// <summary>The weight of the time in the score.</summary>
    private const double TimeWeight = 0.4;

    /// <summary>What the time counts at the end of the window.</summary>
    private const double LatestTimeScore = 0.2;

    /// <summary>How long after a deposit a way out is weighed.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public string DisplayName => "Ut ur skyddad pool";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Litecoin, Chain.Zcash);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ways = WaysOut(input).ToList();
        return [.. Deposits(input).Select(deposit => Describe(deposit, ways)).OfType<CheckFinding>()];
    }

    /// <summary>
    /// Gives the score of how much smaller a way out is than the deposit, from 1 for the same amount to 0 at
    /// <see cref="MostShortfall"/>; a way out larger than the deposit is not the same money.
    /// </summary>
    /// <param name="shortfall">The share of the deposit that did not come out.</param>
    /// <returns>The score.</returns>
    public static double AmountScore(double shortfall) =>
        shortfall is < 0 or > MostShortfall ? 0 : 1 - (shortfall / MostShortfall);

    /// <summary>Gives the score of the time after the deposit, from 1 at once to a fifth at the end of the window.</summary>
    /// <param name="after">The time after the deposit.</param>
    /// <returns>The score, or 0 outside the window.</returns>
    public static double TimeScore(TimeSpan after) => after <= TimeSpan.Zero || after > Window
        ? 0
        : 1 - ((1 - LatestTimeScore) * (after / Window));

    /// <summary>
    /// Gives the money that goes into a pool in the graph's confirmed transactions: what a Zcash transaction puts into
    /// its pools, and every peg-in to MWEB outside the block's integration transaction.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>The deposits, in block order.</returns>
    private static IEnumerable<PoolMove> Deposits(CheckInput input)
    {
        foreach (var transaction in input.Transactions.Where(t => t.Block is not null && !IsIntegration(t)))
        {
            if (transaction.Shielded is { IntoPool.Value: > 0 } flow)
            {
                yield return new PoolMove(transaction, null, flow.IntoPool, "Zcash skyddade pool");
            }

            foreach (var pegIn in transaction.Outputs.Where(o => o.ScriptType == BitcoinScriptType.MwebPegIn))
            {
                yield return new PoolMove(transaction, pegIn, pegIn.Amount, "MWEB");
            }
        }
    }

    /// <summary>
    /// Gives the ways out of a pool in the graph's confirmed transactions: each transparent output of a Zcash
    /// transaction that takes money out of its pools, and each peg-out of a block's integration transaction on Litecoin.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>The ways out.</returns>
    private static IEnumerable<PoolMove> WaysOut(CheckInput input)
    {
        foreach (var transaction in input.Transactions.Where(t => t.Block is not null))
        {
            var fromPool = transaction.Shielded is { OutOfPool.Value: > 0 } || IsIntegration(transaction);
            if (!fromPool)
            {
                continue;
            }

            foreach (var output in transaction.Outputs.Where(o => o.Address is not null && !IsPoolScript(o)))
            {
                var pool = transaction.Txid.Chain == Chain.Zcash ? "Zcash skyddade pool" : "MWEB";
                yield return new PoolMove(transaction, output, output.Amount, pool);
            }
        }
    }

    /// <summary>
    /// Says whether a transaction is a Litecoin block's integration transaction (HogEx): it spends the extension
    /// block's own address, and its other outputs are the block's peg-outs.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsIntegration(BitcoinTransaction transaction) =>
        transaction.Inputs.Any(input => input.ScriptType == BitcoinScriptType.MwebHogAddress);

    /// <summary>Says whether an output pays into MWEB itself (its own address or a peg-in), not out of it.</summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool IsPoolScript(BitcoinOutput output) =>
        output.ScriptType is BitcoinScriptType.MwebHogAddress or BitcoinScriptType.MwebPegIn;

    /// <summary>Describes one deposit's candidates, or nothing when it has none.</summary>
    /// <param name="deposit">The money into the pool.</param>
    /// <param name="ways">The ways out of the pools in the graph.</param>
    /// <returns>The suggestion, or <see langword="null"/>.</returns>
    private CheckFinding? Describe(PoolMove deposit, IReadOnlyList<PoolMove> ways)
    {
        var best = ways
            .Where(way => way.Transaction.Txid.Chain == deposit.Transaction.Txid.Chain)
            .Select(way => (Way: way, Candidate: Candidate(deposit, way)))
            .Where(item => item.Candidate is not null)
            .OrderByDescending(item => item.Candidate!.Score)
            .ThenBy(item => item.Candidate!.Key, StringComparer.Ordinal)
            .Take(MostCandidates)
            .ToList();
        if (best.Count == 0)
        {
            return null;
        }

        var coin = deposit.Transaction.Txid.Chain.NativeAsset().Symbol;
        var amount = CheckText.Btc(deposit.Amount);
        var why = $"{Id} letar i grafen efter pengar som kommer ut ur {deposit.Pool} och kan vara insättningens "
            + $"{amount} {coin} efter en avgift (högst {CheckText.Percent(MostShortfall)} mindre), inom "
            + $"{CheckText.Span(Window)} efter insättningen i block {deposit.Transaction.Block!.Height}. Poängen väger "
            + "beloppet till tre femtedelar och tiden till två. Metoden är forskningens: den som sätter in tar ofta ut "
            + "samma belopp snart efter. I poolen syns varken belopp eller ägare, så en kandidat är en bedömning, "
            + "aldrig ett faktum. Bara det som finns i grafen prövas.";
        IReadOnlyList<BitcoinAddress> depositors =
            [.. deposit.Transaction.Inputs.Select(input => input.Address).OfType<BitcoinAddress>().Distinct()];
        return new CheckFinding(
            deposit.Key,
            CheckKind.Suggestion,
            AreaSort.Obfuscation,
            "Troliga vägar ut ur den skyddade poolen",
            $"{amount} {coin} in i {deposit.Pool} · samma belopp minus avgift och tid",
            why,
            [.. depositors, .. best.Select(item => item.Candidate!.Address).OfType<BitcoinAddress>().Distinct()],
            [],
            [
                CheckBasis.Of(deposit.Transaction),
                .. best.Select(item => item.Way.Transaction).Distinct().Select(CheckBasis.Of),
            ],
            [.. best.Select(item => item.Candidate!)]);
    }

    /// <summary>Weighs one way out as a candidate for a deposit.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="way">A way out of the same chain's pool.</param>
    /// <returns>The candidate, or <see langword="null"/> when it is not one.</returns>
    private static CheckCandidate? Candidate(PoolMove deposit, PoolMove way)
    {
        var after = way.Transaction.Block!.Time - deposit.Transaction.Block!.Time;
        if (way.Transaction.Block.Height <= deposit.Transaction.Block.Height || deposit.Amount.Value <= 0)
        {
            return null;
        }

        var shortfall = (double)(deposit.Amount.Value - way.Amount.Value) / deposit.Amount.Value;
        var amountScore = AmountScore(shortfall);
        var timeScore = TimeScore(after);
        if (amountScore <= 0 || timeScore <= 0)
        {
            return null;
        }

        var score = Math.Round(
            (AmountWeight * amountScore) + (TimeWeight * timeScore), 2, MidpointRounding.AwayFromZero);
        if (score < LowestScore)
        {
            return null;
        }

        var coin = deposit.Transaction.Txid.Chain.NativeAsset().Symbol;
        var reason = string.Create(
            CultureInfo.InvariantCulture,
            $"{CheckText.Btc(way.Amount)} {coin} ut mot {CheckText.Btc(deposit.Amount)} in, ")
            + $"{CheckText.Percent(shortfall)} mindre; {CheckText.Span(after)} senare";
        return new CheckCandidate(
            way.Key, way.Output!.Address!, way.Transaction.Txid, way.Output.Index, way.Amount, score, reason);
    }

    /// <summary>Money into or out of a pool in one transaction.</summary>
    /// <param name="Transaction">The transaction, confirmed.</param>
    /// <param name="Output">The output that moves it (a peg-in or a way out); none for a Zcash deposit.</param>
    /// <param name="Amount">How much.</param>
    /// <param name="Pool">The pool's name, as the texts write it.</param>
    private sealed record PoolMove(BitcoinTransaction Transaction, BitcoinOutput? Output, Satoshi Amount, string Pool)
    {
        /// <summary>Gets the move as a finding's or a candidate's key: the coin, or the transaction and the pool.</summary>
        public string Key => Output is { } output
            ? MixerDeposits.CoinKey(Transaction.Txid, output.Index)
            : Transaction.Txid.Value + ":pool";
    }
}
