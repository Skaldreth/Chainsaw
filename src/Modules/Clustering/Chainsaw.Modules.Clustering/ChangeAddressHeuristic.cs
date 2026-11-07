using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Change address v1: points out the output that is probably the sender's change, from signs that tell the outputs
/// apart, and says how many signs hold. The change is merged with the sender only when the signs are enough
/// (<see cref="AutomaticMergeSigns"/>, two outputs and nothing against); otherwise it is a suggestion.
/// </summary>
internal sealed class ChangeAddressHeuristic : IUtxoHeuristic
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
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

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

        var sightings = Sightings.Of(transaction, outputs, context);
        var scored = outputs.Select(output => (Output: output, Signs: Signs(output, outputs, sightings)))
            .OrderByDescending(entry => entry.Signs.Signs.Count)
            .ThenBy(entry => entry.Output.Index)
            .ToList();
        var (change, (signs, seenIn)) = scored[0];
        var against = scored.Skip(1).Max(entry => entry.Signs.Signs.Count);
        if (signs.Count == 0 || signs.Count <= against)
        {
            // A tie rests on the same sightings as a finding would, so a narrower run must not replace it either.
            return HeuristicResult.Nothing(Identity, transaction.Txid) with { Basis = seenIn };
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
    /// <param name="candidate">The output that may be change.</param>
    /// <param name="outputs">All outputs with an address.</param>
    /// <param name="sightings">What the transaction and the case say once for all its outputs.</param>
    /// <returns>
    /// The signs, in the order of <see cref="ChangeSign"/>, and the earlier transactions where any output's address
    /// was seen, which the signs rest on.
    /// </returns>
    private static (List<ChangeSign> Signs, List<Txid> SeenIn) Signs(
        BitcoinOutput candidate, IReadOnlyList<BitcoinOutput> outputs, Sightings sightings)
    {
        var others = outputs.Where(output => output.Index != candidate.Index).ToList();
        var inputAddresses = sightings.InputAddresses;
        var signs = new List<ChangeSign>();

        if (sightings.InputType is { } inputType
            && candidate.ScriptType == inputType
            && others.All(output => output.ScriptType != inputType))
        {
            signs.Add(ChangeSign.SameScriptTypeAsInputs);
        }

        if (!IsRound(candidate) && others.All(IsRound))
        {
            signs.Add(ChangeSign.OthersRound);
        }

        if (!inputAddresses.Contains(candidate.Address!)
            && sightings.SeenBefore(candidate) is null
            && others.All(output => sightings.SeenBefore(output) is not null))
        {
            signs.Add(ChangeSign.FreshAddress);
        }

        if (sightings.SmallestInput is { } smallest
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

        // Every earlier sighting counts as basis, also of the outputs that did not get the sign: without one of them
        // the signs could tie, and a narrower run must not take the conclusion back.
        return (signs, sightings.Basis);
    }

    /// <summary>
    /// What a transaction and the case say once for all its outputs: the inputs' addresses, script type and smallest
    /// amount, and where each output's address was seen before. Read once per transaction rather than once per output
    /// and pair of outputs, since a batch payout with a few hundred outputs otherwise took a tenth of a second.
    /// </summary>
    /// <param name="InputAddresses">The inputs' addresses.</param>
    /// <param name="InputType">The one script type every input has, or <see langword="null"/>.</param>
    /// <param name="SmallestInput">The smallest input, when every input's amount is known.</param>
    /// <param name="Seen">The earliest earlier transaction each output's address was seen in, by output index.</param>
    /// <param name="Basis">The earlier transactions any output's address was seen in, each once.</param>
    private sealed record Sightings(
        IReadOnlySet<BitcoinAddress> InputAddresses,
        BitcoinScriptType? InputType,
        Satoshi? SmallestInput,
        IReadOnlyDictionary<int, BitcoinTransaction?> Seen,
        List<Txid> Basis)
    {
        /// <summary>Reads what a transaction and the case say about its outputs.</summary>
        /// <param name="transaction">The transaction.</param>
        /// <param name="outputs">Its outputs with an address.</param>
        /// <param name="context">What the case knows around the transaction.</param>
        /// <returns>The sightings.</returns>
        public static Sightings Of(
            BitcoinTransaction transaction, IReadOnlyList<BitcoinOutput> outputs, IHeuristicContext context)
        {
            var seen = outputs.ToDictionary(
                output => output.Index, output => context.SeenBefore(output.Address!, transaction));
            var basis = outputs.Select(output => seen[output.Index])
                .OfType<BitcoinTransaction>()
                .Select(earlier => earlier.Txid)
                .Distinct()
                .ToList();
            return new Sightings(
                transaction.InputAddresses().ToHashSet(),
                transaction.CommonInputType(),
                transaction.SmallestInput(),
                seen,
                basis);
        }

        /// <summary>Gives the earliest earlier transaction an output's address was seen in.</summary>
        /// <param name="output">The output.</param>
        /// <returns>The transaction, or <see langword="null"/> when the address is new.</returns>
        public BitcoinTransaction? SeenBefore(BitcoinOutput output) => Seen[output.Index];
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
