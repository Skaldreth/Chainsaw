using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// PayJoin: a PayJoin cannot be seen on the chain, only guessed. In a PayJoin the payee adds an input of its own, so
/// the inputs have two owners and common-input-ownership would merge strangers. Both versions note a transaction with
/// two inputs or more and two outputs where an input was not needed for either payment (without the smallest input
/// the others still pay the larger output and the fee), as a finding of the sort
/// <see cref="HeuristicNames.PayJoinSort"/>. That alone is common in ordinary wallets' payments, so it never changes
/// the clustering.
/// </summary>
/// <remarks>
/// Version 2 holds the transaction's merges back as suggestions (<see cref="VetoKind.Suggest"/>) only on the strong
/// signs of a PayJoin (<see cref="IsStrong"/>): two or three inputs, one of them of an address type that no other input
/// has and that exactly one output has, that output larger than the input (the payee's own coin plus the payment), and
/// the input not needed. Version 1 never vetoes; a case that draws version 2 back runs it, so that the analysis can be
/// switched off per case without losing its findings.
/// </remarks>
internal sealed class PayJoinHeuristic : IUtxoHeuristic
{
    /// <summary>The heuristic's name and newest version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.PayJoin, 2);

    /// <summary>
    /// The most inputs a PayJoin has by version 2's strong signs: the payer's one or two and the payee's.
    /// </summary>
    public const int MostStrongInputs = 3;

    /// <summary>Whether this version holds merges back on the strong signs (version 2).</summary>
    private readonly bool _vetoes;

    /// <summary>Creates the heuristic in a version.</summary>
    /// <param name="version">1 or 2 (the newest, the default).</param>
    /// <exception cref="ArgumentOutOfRangeException">Another version.</exception>
    public PayJoinHeuristic(int version = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(version, Identity.Version);
        Id = new HeuristicId(HeuristicNames.PayJoin, version);
        _vetoes = version >= 2;
    }

    /// <inheritdoc/>
    public HeuristicId Id { get; }

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!HasUnneededInput(transaction, payee: null))
        {
            return HeuristicResult.Nothing(Id, transaction.Txid);
        }

        var strong = _vetoes && IsStrong(transaction);
        var reason = strong
            ? "En input behövdes inte och har en adresstyp som ingen annan input har men som mottagarens output har, "
                + "och den outputen är större än inputen: så ser en PayJoin ut, där mottagaren lade till ett eget "
                + "mynt. "
                + "Inputs kan ha två ägare, så inget i tx:en slås ihop automatiskt; det blir förslag."
            : "En input behövdes inte: utan den minsta räcker de andra till den större utbetalningen och avgiften. "
                + "Det kan vara en PayJoin, men är vanligt också i vanliga plånböcker, så klustringen påverkas inte.";
        var finding = new GenericFinding(
            reason, HeuristicNames.PayJoinSort, JsonSerializer.Serialize(new { strong }));
        var veto = strong
            ? new OwnershipVeto(VetoKind.Suggest, "Tx:en ser ut som en PayJoin; därför slås inget ihop automatiskt.")
            : null;
        return new HeuristicResult(Id, transaction.Txid, [], [finding], veto);
    }

    /// <summary>
    /// Says whether a transaction with two inputs or more from different addresses and two outputs (and nothing else)
    /// had an input that it did not need: without it, the others still pay the larger output and the fee.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="payee">The input to leave out, or <see langword="null"/> for the smallest.</param>
    /// <returns><see langword="true"/> when it had.</returns>
    private static bool HasUnneededInput(BitcoinTransaction transaction, BitcoinInput? payee)
    {
        var paying = transaction.PayingOutputs();
        if (transaction.IsCoinbase || transaction.Inputs.Count < 2 || transaction.InputAddresses().Count < 2
            || paying.Count != 2 || transaction.Outputs.Count != 2
            || transaction.Inputs.Any(spent => spent.Amount is null))
        {
            return false;
        }

        var total = transaction.Inputs.Sum(spent => spent.Amount!.Value.Value);
        var input = payee?.Amount!.Value.Value ?? transaction.Inputs.Min(spent => spent.Amount!.Value.Value);
        var largest = paying.Max(output => output.Amount.Value);
        return total - input >= largest + transaction.Fee.Value;
    }

    /// <summary>
    /// Says whether a transaction has the strong signs of a PayJoin: at most <see cref="MostStrongInputs"/> inputs, one
    /// of which (the payee's) has an address type that no other input has, that exactly one output has (the payee's,
    /// paid the payment and its own coin back) and that the other output has not; that output is larger than the input;
    /// and the input was not needed.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    private static bool IsStrong(BitcoinTransaction transaction)
    {
        if (transaction.Inputs.Count > MostStrongInputs)
        {
            return false;
        }

        var paying = transaction.PayingOutputs();
        foreach (var payee in transaction.Inputs)
        {
            var others = transaction.Inputs.Where(input => input.Index != payee.Index).ToList();
            var receiving = paying.Where(output => output.ScriptType == payee.ScriptType).ToList();
            if (payee.ScriptType is { } type
                && others.All(input => input.ScriptType != type)
                && receiving is [var received]
                && received.Amount.CompareTo(payee.Amount!.Value) > 0
                && HasUnneededInput(transaction, payee))
            {
                return true;
            }
        }

        return false;
    }
}
