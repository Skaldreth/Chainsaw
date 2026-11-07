using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Change address v1: points out the output that is probably the sender's change, from signs that tell the outputs
/// apart, and says how many signs hold. The change is merged with the sender only when the signs are enough
/// (<see cref="AutomaticMergeSigns"/>, two outputs and nothing against); otherwise it is a suggestion.
/// </summary>
internal sealed class ChangeAddressHeuristic : IClusteringHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.ChangeAddress, 1);

    /// <summary>
    /// How many signs must point at the change, with none pointing elsewhere, before it is merged automatically.
    /// </summary>
    public const int AutomaticMergeSigns = 3;

    /// <summary>
    /// The number of outputs a transaction may have for its change to be merged automatically. With more outputs
    /// (a batch payout) the signs are weaker and every recipient is a stranger, so the change is only suggested.
    /// </summary>
    public const int AutomaticMergeOutputs = 2;

    /// <summary>How many kinds of sign there are, to show the score against.</summary>
    private static readonly int SignKinds = Enum.GetValues<ChangeSign>().Length;

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(context);
        var outputs = transaction.PayingOutputs();
        if (transaction.IsCoinbase || outputs.Count < 2)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var scored = outputs.Select(output => (Output: output, Signs: Signs(transaction, output, outputs, context)))
            .OrderByDescending(entry => entry.Signs.Signs.Count)
            .ThenBy(entry => entry.Output.Index)
            .ToList();
        var (change, (signs, seenIn)) = scored[0];
        var against = scored.Skip(1).Max(entry => entry.Signs.Signs.Count);
        if (signs.Count == 0 || signs.Count <= against)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var merge = outputs.Count <= AutomaticMergeOutputs && signs.Count >= AutomaticMergeSigns && against == 0;
        var reason = Reason(change.Index, signs, against, merge, outputs.Count);
        var finding = new ChangeFinding(
            reason, change.Index, signs, against, SignKinds, [.. seenIn.Select(txid => txid.Value)]);
        var sender = transaction.InputAddresses().FirstOrDefault();
        IReadOnlyList<OwnershipProposal> proposals = sender is null || sender == change.Address
            ? []
            :
            [
                new OwnershipProposal(
                    [sender, change.Address!],
                    merge ? EvidenceStrength.Merge : EvidenceStrength.Suggestion,
                    reason,
                    change.Index),
            ];
        return new HeuristicResult(Identity, transaction.Txid, proposals, [finding]) { Basis = seenIn };
    }

    /// <summary>Gives the signs that hold for one output and for none of the others.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="candidate">The output that may be change.</param>
    /// <param name="outputs">All outputs with an address.</param>
    /// <param name="context">What the case knows around the transaction.</param>
    /// <returns>
    /// The signs, in the order of <see cref="ChangeSign"/>, and the earlier transactions a fresh-address sign rests on.
    /// </returns>
    private static (List<ChangeSign> Signs, List<Txid> SeenIn) Signs(
        BitcoinTransaction transaction,
        BitcoinOutput candidate,
        IReadOnlyList<BitcoinOutput> outputs,
        IHeuristicContext context)
    {
        var others = outputs.Where(output => output.Index != candidate.Index).ToList();
        var inputAddresses = transaction.InputAddresses();
        var signs = new List<ChangeSign>();

        if (transaction.CommonInputType() is { } inputType
            && candidate.ScriptType == inputType
            && others.All(output => output.ScriptType != inputType))
        {
            signs.Add(ChangeSign.SameScriptTypeAsInputs);
        }

        if (!IsRound(candidate) && others.All(IsRound))
        {
            signs.Add(ChangeSign.OthersRound);
        }

        var seenIn = others.Select(output => context.SeenBefore(output.Address!, transaction)).ToList();
        if (!inputAddresses.Contains(candidate.Address!)
            && context.SeenBefore(candidate.Address!, transaction) is null
            && seenIn.All(seen => seen is not null))
        {
            signs.Add(ChangeSign.FreshAddress);
        }

        if (transaction.SmallestInput() is { } smallest
            && candidate.Amount.CompareTo(smallest) < 0
            && others.All(output => output.Amount.CompareTo(smallest) >= 0))
        {
            signs.Add(ChangeSign.OptimalChange);
        }

        if (inputAddresses.Contains(candidate.Address!)
            && others.All(output => !inputAddresses.Contains(output.Address!)))
        {
            signs.Add(ChangeSign.BackToInput);
        }

        var basis = signs.Contains(ChangeSign.FreshAddress)
            ? seenIn.Select(seen => seen!.Txid).Distinct().ToList()
            : [];
        return (signs, basis);
    }

    /// <summary>Says whether an output's amount is round (<see cref="TransactionShape.IsRound"/>).</summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsRound(BitcoinOutput output) => TransactionShape.IsRound(output.Amount);

    /// <summary>Writes the reason in plain Swedish.</summary>
    /// <param name="index">The change output's index.</param>
    /// <param name="signs">The signs that point at it.</param>
    /// <param name="against">The most signs that point at another output.</param>
    /// <param name="merge">Whether it is merged.</param>
    /// <param name="outputs">How many outputs with an address the transaction has.</param>
    /// <returns>The reason.</returns>
    private static string Reason(int index, IReadOnlyList<ChangeSign> signs, int against, bool merge, int outputs)
    {
        var listed = string.Join(", ", signs.Select(Describe));
        var verdict = (merge, outputs > AutomaticMergeOutputs) switch
        {
            (true, _) => "tecknen räcker, så växeln slås ihop med avsändaren",
            (false, true) => "tx:en har fler än två outputs, så det är bara ett förslag",
            _ => "tecknen räcker inte för att slå ihop, så det är bara ett förslag",
        };
        return string.Format(
            CultureInfo.InvariantCulture,
            "Output {0} är trolig växel: {1} av {2} tecken ({3}), {4} mot. {5}.",
            index,
            signs.Count,
            SignKinds,
            listed,
            against,
            char.ToUpperInvariant(verdict[0]) + verdict[1..]);
    }

    /// <summary>Describes a sign in plain Swedish.</summary>
    /// <param name="sign">The sign.</param>
    /// <returns>The description.</returns>
    private static string Describe(ChangeSign sign) => sign switch
    {
        ChangeSign.SameScriptTypeAsInputs => "samma skripttyp som inputs",
        ChangeSign.OthersRound => "de andra beloppen är jämna",
        ChangeSign.FreshAddress => "ny adress när de andra är kända",
        ChangeSign.OptimalChange => "mindre än varje input",
        ChangeSign.BackToInput => "tillbaka till en inputs adress",
        _ => throw new ArgumentOutOfRangeException(nameof(sign)),
    };
}
