using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// CoinJoin v1: recognises transactions where many owners pay in together and get equal outputs back, so that whose
/// output is whose cannot be read. It stops every merge in the transaction (common-input-ownership does not hold
/// there), and a recognised CoinJoin ends the trail. It looks only at the transaction itself; no labels are used.
/// </summary>
/// <remarks>
/// The rules err towards seeing a CoinJoin: a CoinJoin that is missed would merge strangers into one cluster, while
/// a transaction wrongly taken for one only leaves some addresses unmerged.
/// </remarks>
internal sealed class CoinJoinHeuristic : IUtxoHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.CoinJoin, 1);

    /// <summary>
    /// The smallest amount equal outputs must have to count; equal dust (for example from address poisoning) is no
    /// sign of a CoinJoin.
    /// </summary>
    public const long MinimumDenomination = 5_000;

    /// <summary>Whirlpool's number of inputs and outputs in a mix.</summary>
    public const int WhirlpoolParticipants = 5;

    /// <summary>
    /// Whirlpool's pool amounts in satoshi (0.001, 0.01, 0.05 and 0.5 BTC). The list is version 1's; a new pool
    /// means a new version.
    /// </summary>
    public static readonly IReadOnlySet<long> WhirlpoolPools = new HashSet<long>
    {
        100_000, 1_000_000, 5_000_000, 50_000_000,
    };

    /// <summary>The fewest inputs and outputs a Wasabi round has in version 1's reading.</summary>
    public const int WasabiMinimumParticipants = 20;

    /// <summary>The fewest equal outputs a JoinMarket round has in version 1's reading.</summary>
    public const int JoinMarketMinimumEqualOutputs = 3;

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.IsCoinbase)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var outputs = transaction.PayingOutputs();
        var group = LargestEqualGroup(outputs);
        var inputAddresses = transaction.InputAddresses().Count;
        var funding = transaction.FundingTransactions();
        if (group is not { } found || found.Count < 2 || inputAddresses < 2 || funding < 2)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var (denomination, equal) = found;

        var (pattern, certainty) = Classify(transaction, outputs, denomination, equal, inputAddresses);
        var reason = Reason(pattern, certainty, denomination, equal, transaction.Inputs.Count);
        var finding = new CoinJoinFinding(
            reason, pattern, certainty, denomination, equal, transaction.Inputs.Count);
        var veto = certainty == CoinJoinCertainty.Recognised
            ? new OwnershipVeto(VetoKind.Stop, "Tx:en är en CoinJoin; dess inputs har många ägare.")
            : new OwnershipVeto(VetoKind.Suggest, "Tx:en kan vara en CoinJoin; därför slås inget ihop automatiskt.");
        return new HeuristicResult(Identity, transaction.Txid, [], [finding], veto);
    }

    /// <summary>
    /// Finds the largest group of outputs with the same amount and different addresses, ignoring dust; the larger
    /// amount wins a tie.
    /// </summary>
    /// <param name="outputs">The outputs with an address.</param>
    /// <returns>
    /// The amount and how many different addresses get it, or <see langword="null"/> when there are none.
    /// </returns>
    private static (Satoshi Amount, int Count)? LargestEqualGroup(IReadOnlyList<BitcoinOutput> outputs) =>
        outputs.Where(output => output.Amount.Value >= MinimumDenomination)
            .GroupBy(output => output.Amount)
            .Select(group => (Amount: group.Key, Count: group.Select(output => output.Address).Distinct().Count()))
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.Amount.Value)
            .Select(group => ((Satoshi, int)?)group)
            .FirstOrDefault();

    /// <summary>Decides which pattern the transaction has and how sure that is.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="outputs">Its outputs with an address.</param>
    /// <param name="denomination">The equal outputs' amount.</param>
    /// <param name="equal">How many equal outputs.</param>
    /// <param name="inputAddresses">How many different addresses the inputs have.</param>
    /// <returns>The pattern and the certainty.</returns>
    private static (CoinJoinPattern Pattern, CoinJoinCertainty Certainty) Classify(
        BitcoinTransaction transaction,
        IReadOnlyList<BitcoinOutput> outputs,
        Satoshi denomination,
        int equal,
        int inputAddresses)
    {
        if (IsWhirlpool(transaction, denomination, equal))
        {
            return (CoinJoinPattern.Whirlpool, CoinJoinCertainty.Recognised);
        }

        if (IsWasabi(transaction, outputs))
        {
            return (CoinJoinPattern.Wasabi, CoinJoinCertainty.Recognised);
        }

        // JoinMarket picks unround amounts; equal round amounts from several inputs are just as likely a batch of
        // withdrawals or structuring, so they are only possible CoinJoins that never end the trail.
        var joinMarket = equal >= JoinMarketMinimumEqualOutputs
            && inputAddresses >= equal
            && outputs.Count <= 2 * equal
            && !TransactionShape.IsRound(denomination);
        return joinMarket
            ? (CoinJoinPattern.JoinMarket, CoinJoinCertainty.Recognised)
            : (CoinJoinPattern.Generic, CoinJoinCertainty.Possible);
    }

    /// <summary>
    /// Whirlpool: exactly five inputs and five outputs, all native SegWit, all with the same amount, which is one of
    /// the pools.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="denomination">The equal outputs' amount.</param>
    /// <param name="equal">How many equal outputs.</param>
    /// <returns><see langword="true"/> when it matches.</returns>
    private static bool IsWhirlpool(BitcoinTransaction transaction, Satoshi denomination, int equal) =>
        transaction.Inputs.Count == WhirlpoolParticipants
        && transaction.Outputs.Count == WhirlpoolParticipants
        && equal == WhirlpoolParticipants
        && WhirlpoolPools.Contains(denomination.Value)
        && transaction.Outputs.All(output => output.ScriptType == BitcoinScriptType.PayToWitnessPublicKeyHash);

    /// <summary>
    /// Wasabi: many inputs and outputs, all outputs SegWit (P2WPKH or P2TR), and at least half of the outputs in
    /// groups of equal amounts (Wasabi 1's one denomination or Wasabi 2's standard amounts).
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="outputs">Its outputs with an address.</param>
    /// <returns><see langword="true"/> when it matches.</returns>
    private static bool IsWasabi(BitcoinTransaction transaction, IReadOnlyList<BitcoinOutput> outputs)
    {
        var segwit = outputs.Count == transaction.Outputs.Count && outputs.All(output =>
            output.ScriptType is BitcoinScriptType.PayToWitnessPublicKeyHash or BitcoinScriptType.PayToTaproot);
        var inGroups = outputs.GroupBy(output => output.Amount).Where(group => group.Count() >= 2)
            .Sum(group => group.Count());
        return segwit
            && transaction.Inputs.Count >= WasabiMinimumParticipants
            && outputs.Count >= WasabiMinimumParticipants
            && inGroups * 2 >= outputs.Count;
    }

    /// <summary>Writes the reason in plain Swedish.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="certainty">The certainty.</param>
    /// <param name="denomination">The equal outputs' amount.</param>
    /// <param name="equal">How many equal outputs.</param>
    /// <param name="inputs">How many inputs.</param>
    /// <returns>The reason.</returns>
    private static string Reason(
        CoinJoinPattern pattern, CoinJoinCertainty certainty, Satoshi denomination, int equal, int inputs)
    {
        var shape = string.Format(
            CultureInfo.InvariantCulture,
            "{0} inputs och {1} lika stora outputs på {2} BTC",
            inputs,
            equal,
            denomination);
        var verdict = (pattern, certainty) switch
        {
            (_, CoinJoinCertainty.Possible) =>
                "kan vara en CoinJoin mellan flera ägare, men också en batchutbetalning eller en slump",
            (CoinJoinPattern.Whirlpool, _) => "samma form som en Whirlpool-mix (fem deltagare, en av poolernas belopp)",
            (CoinJoinPattern.Wasabi, _) => "samma form som en Wasabi-runda (många deltagare, grupper av lika belopp)",
            (CoinJoinPattern.JoinMarket, _) =>
                "samma form som JoinMarket (lika stora outputs och högst en växel per deltagare)",
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
        return shape + ": " + verdict + ". Vem som äger vilken output går inte att läsa ut.";
    }
}
