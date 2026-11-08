using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Change address: points out the output that is probably the sender's change, from signs that tell the outputs
/// apart, and says how many signs hold. The change is merged with the sender only when the strong signs are enough
/// (<see cref="AutomaticMergeSigns"/>, two outputs and nothing against); otherwise it is a suggestion.
/// </summary>
/// <remarks>
/// Version 2 adds the wallet's fingerprint (<see cref="ChangeSign.SameFingerprintSpent"/>): the output that the same
/// kind of wallet spends later is the sender's. A fingerprint is weak (most wallets share one, and a wallet can change
/// it between transactions), so it never counts towards an automatic merge: it can only break a tie or strengthen a
/// suggestion. Version 1 stays registered, so that the case can go back to it if version 2 is drawn back.
/// </remarks>
internal sealed class ChangeAddressHeuristic : IUtxoHeuristic
{
    /// <summary>The heuristic's name and newest version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.ChangeAddress, 2);

    /// <summary>
    /// How many strong signs (every sign but the fingerprint) must point at the change, with none pointing elsewhere,
    /// before it is merged automatically.
    /// </summary>
    public const int AutomaticMergeSigns = 3;

    /// <summary>
    /// The number of outputs a transaction may have for its change to be merged automatically. With more outputs
    /// (a batch payout) the signs are weaker and every recipient is a stranger, so the change is only suggested.
    /// </summary>
    public const int AutomaticMergeOutputs = 2;

    /// <summary>Whether this version reads the wallet's fingerprint (version 2).</summary>
    private readonly bool _readsFingerprint;

    /// <summary>How many kinds of sign this version checks, to show the score against.</summary>
    private readonly int _signKinds;

    /// <summary>Creates the heuristic in a version.</summary>
    /// <param name="version">1 or 2 (the newest, the default).</param>
    /// <exception cref="ArgumentOutOfRangeException">Another version.</exception>
    public ChangeAddressHeuristic(int version = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(version, Identity.Version);
        Id = new HeuristicId(HeuristicNames.ChangeAddress, version);
        _readsFingerprint = version >= 2;
        _signKinds = Enum.GetValues<ChangeSign>().Length - (_readsFingerprint ? 0 : 1);
    }

    /// <inheritdoc/>
    public HeuristicId Id { get; }

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
            return HeuristicResult.Nothing(Id, transaction.Txid);
        }

        var sightings = Sightings.Of(transaction, outputs, context, _readsFingerprint);
        var scored = outputs.Select(output => (Output: output, Signs: Signs(output, outputs, sightings)))
            .OrderByDescending(entry => entry.Signs.Signs.Count)
            .ThenBy(entry => entry.Output.Index)
            .ToList();
        var (change, (signs, seenIn)) = scored[0];
        var against = scored.Skip(1).Max(entry => entry.Signs.Signs.Count);
        if (signs.Count == 0 || signs.Count <= against)
        {
            // A tie rests on the same sightings as a finding would, so a narrower run must not replace it either.
            return HeuristicResult.Nothing(Id, transaction.Txid) with { Basis = seenIn };
        }

        var strong = signs.Count(sign => sign != ChangeSign.SameFingerprintSpent);
        var merge = outputs.Count <= AutomaticMergeOutputs && strong >= AutomaticMergeSigns && against == 0;
        var reason = Reason(change.Index, signs, against, merge, outputs.Count);
        var finding = new ChangeFinding(
            reason, change.Index, signs, against, _signKinds, [.. seenIn.Select(txid => txid.Value)]);
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
        return new HeuristicResult(Id, transaction.Txid, proposals, [finding]) { Basis = seenIn };
    }

    /// <summary>Gives the signs that hold for one output and for none of the others.</summary>
    /// <param name="candidate">The output that may be change.</param>
    /// <param name="outputs">All outputs with an address.</param>
    /// <param name="sightings">What the transaction and the case say once for all its outputs.</param>
    /// <returns>
    /// The signs, in the order of <see cref="ChangeSign"/>, and the other transactions the signs rest on.
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

        if (sightings.SpentBySameWallet(candidate) == true
            && others.All(output => sightings.SpentBySameWallet(output) == false))
        {
            signs.Add(ChangeSign.SameFingerprintSpent);
        }

        // Every sighting counts as basis, also of the outputs that did not get the sign: without one of them the signs
        // could tie, and a narrower run must not take the conclusion back.
        return (signs, sightings.Basis);
    }

    /// <summary>
    /// What a transaction and the case say once for all its outputs: the inputs' addresses, script type and smallest
    /// amount, where each output's address was seen before, and the fingerprints of the transactions that spend the
    /// outputs. Read once per transaction rather than once per output and pair of outputs, since a batch payout with a
    /// few hundred outputs otherwise took a tenth of a second.
    /// </summary>
    /// <param name="InputAddresses">The inputs' addresses.</param>
    /// <param name="InputType">The one script type every input has, or <see langword="null"/>.</param>
    /// <param name="SmallestInput">The smallest input, when every input's amount is known.</param>
    /// <param name="Seen">The earliest earlier transaction each output's address was seen in, by output index.</param>
    /// <param name="SameWallet">
    /// By output index, whether the transaction that spends it has the same wallet fingerprint as this one; empty
    /// unless the case knows a spender of every output, since only then can the fingerprint sign be tested.
    /// </param>
    /// <param name="Basis">
    /// The earlier transactions any output's address was seen in, and the spenders when the fingerprint was tested,
    /// each once.
    /// </param>
    private sealed record Sightings(
        IReadOnlySet<BitcoinAddress> InputAddresses,
        BitcoinScriptType? InputType,
        Satoshi? SmallestInput,
        IReadOnlyDictionary<int, BitcoinTransaction?> Seen,
        IReadOnlyDictionary<int, bool> SameWallet,
        List<Txid> Basis)
    {
        /// <summary>Reads what a transaction and the case say about its outputs.</summary>
        /// <param name="transaction">The transaction.</param>
        /// <param name="outputs">Its outputs with an address.</param>
        /// <param name="context">What the case knows around the transaction.</param>
        /// <param name="readsFingerprint">Whether the version reads the wallet's fingerprint.</param>
        /// <returns>The sightings.</returns>
        public static Sightings Of(
            BitcoinTransaction transaction,
            IReadOnlyList<BitcoinOutput> outputs,
            IHeuristicContext context,
            bool readsFingerprint)
        {
            var seen = outputs.ToDictionary(
                output => output.Index, output => context.SeenBefore(output.Address!, transaction));
            var spenders = readsFingerprint ? Spenders(transaction, outputs, context) : [];
            var fingerprint = transaction.Fingerprint();
            var basis = outputs.Select(output => seen[output.Index])
                .OfType<BitcoinTransaction>()
                .Concat(spenders.Values)
                .Select(other => other.Txid)
                .Distinct()
                .ToList();
            return new Sightings(
                transaction.InputAddresses().ToHashSet(),
                transaction.CommonInputType(),
                transaction.SmallestInput(),
                seen,
                spenders.ToDictionary(entry => entry.Key, entry => entry.Value.Fingerprint() == fingerprint),
                basis);
        }

        /// <summary>
        /// Finds the transactions that spend the outputs, but only when the case knows one for every output: the
        /// fingerprint sign can only be tested then, and a spender that cannot change the conclusion must not make it
        /// be sealed again.
        /// </summary>
        /// <param name="transaction">The transaction.</param>
        /// <param name="outputs">Its outputs with an address.</param>
        /// <param name="context">What the case knows.</param>
        /// <returns>The spender of each output by index, or none.</returns>
        private static Dictionary<int, BitcoinTransaction> Spenders(
            BitcoinTransaction transaction, IReadOnlyList<BitcoinOutput> outputs, IHeuristicContext context)
        {
            var spenders = new Dictionary<int, BitcoinTransaction>();
            foreach (var output in outputs)
            {
                if (context.FindSpender(transaction.Txid, output.Index) is not { } spender)
                {
                    return [];
                }

                spenders[output.Index] = spender;
            }

            return spenders;
        }

        /// <summary>Gives the earliest earlier transaction an output's address was seen in.</summary>
        /// <param name="output">The output.</param>
        /// <returns>The transaction, or <see langword="null"/> when the address is new.</returns>
        public BitcoinTransaction? SeenBefore(BitcoinOutput output) => Seen[output.Index];

        /// <summary>Tells whether the transaction that spends an output has the same wallet fingerprint.</summary>
        /// <param name="output">The output.</param>
        /// <returns>
        /// Whether it has, or <see langword="null"/> when the fingerprint was not tested.
        /// </returns>
        public bool? SpentBySameWallet(BitcoinOutput output) =>
            SameWallet.TryGetValue(output.Index, out var same) ? same : null;
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
    private string Reason(int index, IReadOnlyList<ChangeSign> signs, int against, bool merge, int outputs)
    {
        var listed = string.Join(", ", signs.Select(Describe));
        var verdict = (merge, outputs > AutomaticMergeOutputs) switch
        {
            (true, _) => "tecknen räcker, så växeln slås ihop med avsändaren",
            (false, true) => "tx:en har fler än två outputs, så det är bara ett förslag",
            _ => "tecknen räcker inte för att slå ihop, så det är bara ett förslag",
        };
        var fingerprint = signs.Contains(ChangeSign.SameFingerprintSpent)
            ? " Plånbokens fingeravtryck är ett svagt tecken och räknas aldrig för att slå ihop."
            : string.Empty;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Output {0} är trolig växel: {1} av {2} tecken ({3}), {4} mot. {5}.{6}",
            index,
            signs.Count,
            _signKinds,
            listed,
            against,
            char.ToUpperInvariant(verdict[0]) + verdict[1..],
            fingerprint);
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
        ChangeSign.SameFingerprintSpent => "spenderas senare av samma sorts plånbok, de andra av en annan",
        _ => throw new ArgumentOutOfRangeException(nameof(sign)),
    };
}
