using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// PayJoin v1: a PayJoin cannot be seen on the chain, only guessed. In a PayJoin the payee adds an input of its own, so
/// the inputs have two owners and common-input-ownership would merge strangers. This analysis looks for a transaction
/// with two inputs or more and two outputs where an input was not needed for either payment (without the smallest
/// input the others still pay the larger output and the fee); wallets seldom add an input they do not need. Then it
/// holds every merge in the transaction back as a suggestion (<see cref="VetoKind.Suggest"/>), the safer choice, and
/// says why in a finding of the sort <see cref="HeuristicNames.PayJoinSort"/>.
/// </summary>
/// <remarks>
/// A wallet that consolidates coins while it pays has the same shape, so its inputs are only suggested too; the user
/// can approve the merge.
/// </remarks>
internal sealed class PayJoinHeuristic : IUtxoHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.PayJoin, 1);

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!HasUnneededInput(transaction))
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var mixedTypes = transaction.Inputs.Select(input => input.ScriptType).Distinct().Count() > 1;
        var types = mixedTypes ? " Inputs är dessutom av olika adresstyp, som när två plånböcker bidrar." : string.Empty;
        var reason = "En input behövdes inte: utan den minsta räcker de andra till den större utbetalningen och "
            + $"avgiften.{types} Tx:en kan vara en PayJoin, där mottagaren lade till ett eget mynt, så inputs kan ha "
            + "två ägare; därför slås inget i tx:en ihop automatiskt, det blir förslag. Det kan också vara en "
            + "plånbok som samlar ihop mynt.";
        var finding = new GenericFinding(
            reason, HeuristicNames.PayJoinSort, JsonSerializer.Serialize(new { mixedTypes }));
        var veto = new OwnershipVeto(
            VetoKind.Suggest, "Tx:en kan vara en PayJoin; därför slås inget ihop automatiskt.");
        return new HeuristicResult(Identity, transaction.Txid, [], [finding], veto);
    }

    /// <summary>
    /// Says whether a transaction with two inputs or more from different addresses and two outputs had an input it
    /// did not need: without its smallest input, the others still pay the larger output and the fee.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it had.</returns>
    private static bool HasUnneededInput(BitcoinTransaction transaction)
    {
        var paying = transaction.PayingOutputs();
        if (transaction.IsCoinbase || transaction.Inputs.Count < 2 || transaction.InputAddresses().Count < 2
            || paying.Count != 2 || transaction.Outputs.Count != 2
            || transaction.Inputs.Any(input => input.Amount is null))
        {
            return false;
        }

        var total = transaction.Inputs.Sum(input => input.Amount!.Value.Value);
        var smallest = transaction.Inputs.Min(input => input.Amount!.Value.Value);
        var largest = paying.Max(output => output.Amount.Value);
        return total - smallest >= largest + transaction.Fee.Value;
    }
}
