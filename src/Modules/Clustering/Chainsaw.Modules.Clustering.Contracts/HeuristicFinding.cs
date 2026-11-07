using System.Text.Json.Serialization;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// Something an analysis recognised in a transaction besides who owns what: a CoinJoin, the change, a hop in a peel
/// chain. A finding is a conclusion, never a fact (principle 2), and says why.
/// </summary>
/// <param name="Reason">Why, in plain Swedish.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CoinJoinFinding), "coinjoin")]
[JsonDerivedType(typeof(ChangeFinding), "change")]
[JsonDerivedType(typeof(PeelChainFinding), "peel-chain")]
public abstract record HeuristicFinding(string Reason);

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
    /// <summary>Gets whether the trail ends here: only a recognised CoinJoin ends it.</summary>
    public bool EndsTrail => Certainty == CoinJoinCertainty.Recognised;
}

/// <summary>The known kinds of CoinJoin, as far as they can be told apart without labels.</summary>
public enum CoinJoinPattern
{
    /// <summary>Equal outputs and inputs from many places, without a known pattern.</summary>
    Generic,

    /// <summary>Whirlpool (Samourai and its successors): five inputs, five equal outputs of a pool's amount.</summary>
    Whirlpool,

    /// <summary>Wasabi: many inputs and many outputs in groups of standard amounts.</summary>
    Wasabi,

    /// <summary>JoinMarket: a few equal outputs and up to one change output for each participant.</summary>
    JoinMarket,
}

/// <summary>How sure the CoinJoin analysis is.</summary>
public enum CoinJoinCertainty
{
    /// <summary>The pattern is clear; the trail ends and nothing in the transaction is merged.</summary>
    Recognised,

    /// <summary>It may be a small CoinJoin; nothing in the transaction is merged automatically, only proposed.</summary>
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
public sealed record ChangeFinding(
    string Reason,
    int OutputIndex,
    IReadOnlyList<ChangeSign> Signs,
    int SignsAgainst,
    int SignsChecked) : HeuristicFinding(Reason)
{
    /// <summary>Gets how many signs point at the output.</summary>
    public int Score => Signs.Count;
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
}

/// <summary>A hop in a peel chain: a little is peeled off and the rest goes on to the next hop.</summary>
/// <param name="Reason">Why, in plain Swedish.</param>
/// <param name="ContinuingOutput">The output that goes on to the next hop: the change.</param>
/// <param name="PeelOutput">The output that is peeled off: a payment.</param>
/// <param name="HopsBefore">How many peel hops the case knows of before this one.</param>
/// <param name="HopsAfter">How many peel hops the case knows of after this one.</param>
public sealed record PeelChainFinding(
    string Reason,
    int ContinuingOutput,
    int PeelOutput,
    int HopsBefore,
    int HopsAfter) : HeuristicFinding(Reason)
{
    /// <summary>Gets how long the known chain is, this hop included.</summary>
    public int ChainLength => HopsBefore + 1 + HopsAfter;
}
