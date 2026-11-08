using System.Text.Json.Serialization;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// Something an analysis recognised in a transaction besides who owns what: a CoinJoin, the change, a hop in a peel
/// chain. A finding is a conclusion, never a fact (principle 2), and says why.
/// </summary>
/// <remarks>
/// Findings are sealed as JSON with their kind, so only the kinds declared here can be stored. A new analysis whose
/// finding has no fields that other modules read gives a <see cref="GenericFinding"/> with its sort and its data as
/// JSON, so that it can be stored without a change here; one whose fields other modules read declares its kind here.
/// </remarks>
/// <param name="Reason">Why, in plain Swedish.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CoinJoinFinding), "coinjoin")]
[JsonDerivedType(typeof(ChangeFinding), "change")]
[JsonDerivedType(typeof(PeelChainFinding), "peel-chain")]
[JsonDerivedType(typeof(GenericFinding), "generic")]
public abstract record HeuristicFinding(string Reason)
{
    /// <summary>
    /// Gets the output the finding takes to be the sender's change, or <see langword="null"/> when it says nothing
    /// about change. Two findings or proposals of the same transaction that name different change cancel each other's
    /// merges.
    /// </summary>
    [JsonIgnore]
    public virtual int? ChangeOutput => null;
}

/// <summary>
/// The transaction is a CoinJoin: many owners pay into it together, so that whose output is whose cannot be read.
/// The trail ends here with the reason CoinJoin, and no inputs are merged.
/// </summary>
/// <param name="Reason">Why, in plain Swedish.</param>
/// <param name="Pattern">Which known kind of CoinJoin it looks like.</param>
/// <param name="Certainty">How sure the analysis is.</param>
/// <param name="Denomination">The amount the equal outputs have.</param>
/// <param name="EqualOutputs">How many outputs have exactly that amount.</param>
/// <param name="Inputs">How many inputs the transaction has.</param>
public sealed record CoinJoinFinding(
    string Reason,
    CoinJoinPattern Pattern,
    CoinJoinCertainty Certainty,
    Satoshi Denomination,
    int EqualOutputs,
    int Inputs) : HeuristicFinding(Reason)
{
    /// <summary>Gets whether the trail ends here: only a recognised CoinJoin (a known pattern) ends it.</summary>
    public bool EndsTrail => Certainty == CoinJoinCertainty.Recognised;
}

/// <summary>The known kinds of CoinJoin, as far as they can be told apart without labels.</summary>
public enum CoinJoinPattern
{
    /// <summary>
    /// Equal outputs and inputs from many places, without a known pattern; it may be a CoinJoin but also a batch of
    /// equal withdrawals, so it is never more than possible.
    /// </summary>
    Generic,

    /// <summary>Whirlpool (Samourai and its successors): five inputs, five equal outputs of a pool's amount.</summary>
    Whirlpool,

    /// <summary>Wasabi: many inputs and many outputs in groups of standard amounts.</summary>
    Wasabi,

    /// <summary>JoinMarket: a few equal outputs and up to one change output for each participant.</summary>
    JoinMarket,

    /// <summary>
    /// Wasabi 2 (WabiSabi): many inputs and outputs, most of them of Wasabi 2's standard amounts, which need not come
    /// in equal groups. Recognised from <c>coinjoin v2</c>.
    /// </summary>
    Wasabi2,
}

/// <summary>How sure the CoinJoin analysis is.</summary>
public enum CoinJoinCertainty
{
    /// <summary>
    /// A known pattern (Whirlpool, Wasabi, JoinMarket); the trail ends and nothing in the transaction is merged.
    /// </summary>
    Recognised,

    /// <summary>
    /// It may be a CoinJoin; the trail goes on, and nothing in the transaction is merged automatically, only proposed.
    /// </summary>
    Possible,
}

/// <summary>The output that is probably the sender's change, and the signs that point at it.</summary>
/// <param name="Reason">Why, in plain Swedish.</param>
/// <param name="OutputIndex">The change output's index in the transaction.</param>
/// <param name="Signs">The signs that hold for this output and for no other.</param>
/// <param name="SignsAgainst">The most signs that point at any other output instead.</param>
/// <param name="SignsChecked">
/// How many kinds of sign the version checks, so that the score can be read (for example 3 of 5).
/// </param>
/// <param name="SeenIn">
/// The txids of the earlier transactions where the outputs' addresses were seen; the fresh-address sign rests on them.
/// </param>
public sealed record ChangeFinding(
    string Reason,
    int OutputIndex,
    IReadOnlyList<ChangeSign> Signs,
    int SignsAgainst,
    int SignsChecked,
    IReadOnlyList<string> SeenIn) : HeuristicFinding(Reason)
{
    /// <summary>Gets how many signs point at the output.</summary>
    public int Score => Signs.Count;

    /// <inheritdoc/>
    public override int? ChangeOutput => OutputIndex;
}

/// <summary>A sign that an output is the sender's change. A sign only counts when it tells the outputs apart.</summary>
public enum ChangeSign
{
    /// <summary>The output has the same script type as every input, and the other outputs do not.</summary>
    SameScriptTypeAsInputs,

    /// <summary>The other outputs have round amounts and this one does not.</summary>
    OthersRound,

    /// <summary>
    /// The output's address had not been seen before this transaction in the case, and the other outputs' had.
    /// </summary>
    FreshAddress,

    /// <summary>
    /// The output is smaller than every input and the others are not; had it been the payment, a smaller input
    /// would not have been needed (optimal change).
    /// </summary>
    OptimalChange,

    /// <summary>The output goes back to one of the inputs' addresses, and the others do not.</summary>
    BackToInput,

    /// <summary>
    /// The output is spent later by a transaction with the same wallet fingerprint as this one (version, kind of
    /// locktime and whether it signals RBF), and every other output is spent by one with another fingerprint: the
    /// same wallet went on with it. Counted from <c>change-address v2</c>.
    /// </summary>
    SameFingerprintSpent,
}

/// <summary>A hop in a peel chain: a little is peeled off and the rest goes on to the next hop.</summary>
/// <param name="Reason">Why, in plain Swedish.</param>
/// <param name="ContinuingOutput">The output that goes on to the next hop: the change.</param>
/// <param name="PeelOutput">The output that is peeled off: a payment.</param>
/// <param name="HopsBefore">How many peel hops the case knows of before this one.</param>
/// <param name="HopsAfter">How many peel hops the case knows of after this one.</param>
/// <param name="Hops">The txids of the other hops, the ones before first (nearest first), then the ones after.</param>
/// <param name="ExtraInputs">
/// How many inputs besides the coin from the hop before topped this hop up (<c>peel-chain v2</c>); 0 in version 1.
/// </param>
public sealed record PeelChainFinding(
    string Reason,
    int ContinuingOutput,
    int PeelOutput,
    int HopsBefore,
    int HopsAfter,
    IReadOnlyList<string> Hops,
    int ExtraInputs = 0) : HeuristicFinding(Reason)
{
    /// <summary>Gets whether the hop is inside the chain, with a known hop on both sides.</summary>
    public bool IsInner => HopsBefore > 0 && HopsAfter > 0;

    /// <summary>Gets how long the known chain is, this hop included.</summary>
    public int ChainLength => HopsBefore + 1 + HopsAfter;

    /// <inheritdoc/>
    public override int? ChangeOutput => ContinuingOutput;
}

/// <summary>
/// A finding of a sort that has no kind of its own here: its sort names it and its data is the analysis's own JSON,
/// sealed as it is. It is a conclusion like every finding (principle 2), and its reason says why.
/// </summary>
/// <param name="Reason">Why, in plain Swedish.</param>
/// <param name="Sort">The finding's sort, stable within the analysis, in lower case with hyphens.</param>
/// <param name="Data">The analysis's own data as JSON, or <see langword="null"/>.</param>
public sealed record GenericFinding(string Reason, string Sort, string? Data = null) : HeuristicFinding(Reason)
{
    /// <summary>Gets the sort.</summary>
    public string Sort { get; } = string.IsNullOrWhiteSpace(Sort)
        ? throw new ArgumentException("A finding has a sort.", nameof(Sort))
        : Sort;
}
