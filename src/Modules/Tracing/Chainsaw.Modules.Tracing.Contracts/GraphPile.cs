namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>Which side of the actor a pile lies on: where its money came from, or where it went.</summary>
public enum PileSide
{
    /// <summary>The actors that paid the expanded actor, or took part in the transactions it received in.</summary>
    In,

    /// <summary>The actors the expanded actor paid, or that took part in the transactions it spent in.</summary>
    Out,
}

/// <summary>
/// A pile (sketch EX2b): the many new counterparties one expansion brought on one side of an actor, kept in one
/// square node like a deck of cards, so that the graph never grows out of hand (principle 7). It is arrangement, not a
/// conclusion: every transfer to and from its members stays in the graph as the fact it is, and nothing is removed
/// (principle 1); the user picks members out of it, and the case remembers which.
/// </summary>
/// <param name="Key">The pile's stable key: <c>pile:</c>, the side and the expanded actor's position key.</param>
/// <param name="Anchor">The expanded actor's node key, where the pile lies.</param>
/// <param name="Side">The side it lies on.</param>
/// <param name="Members">The node keys still in the pile, in the graph's order.</param>
/// <param name="PickedOut">The node keys that belong to it by the rule but that the user picked out.</param>
/// <param name="Positions">Where the user placed the pile, per layout; empty when it has not been placed.</param>
public sealed record GraphPile(
    string Key,
    string Anchor,
    PileSide Side,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> PickedOut,
    IReadOnlyDictionary<GraphLayout, NodePosition> Positions)
{
    /// <summary>The prefix of a pile's key.</summary>
    public const string KeyPrefix = "pile:";

    /// <summary>
    /// The most new counterparties one expansion draws as nodes of their own on one side of an actor; more than this
    /// go into a pile. Eight still reads as a fan around the actor; a few dozen is the long column the rule is for.
    /// </summary>
    public const int MostLoose = 8;

    /// <summary>
    /// Gives the key an address is remembered by when it is picked out of a pile, so that an actor is put back under
    /// every address it has, whichever represents its cluster now.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The address node's key, <c>address:</c> and the address.</returns>
    public static string MemberKey(Chains.Contracts.BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return AddressKeyPrefix + address.Value;
    }

    /// <summary>The prefix of a single address's node key.</summary>
    public const string AddressKeyPrefix = "address:";
}

/// <summary>
/// What an expansion held back at an actor (sketch EX1): drawn as a dashed clue node <c>+N tx</c> beside it, so that
/// what is missing is never silent (principles 7 and 9). Asking for it brings in the next part.
/// </summary>
/// <param name="Key">The clue's key: <c>held-back:</c> and the actor's position key.</param>
/// <param name="Anchor">The actor's node key.</param>
/// <param name="Remaining">
/// How many transactions are still held back in all, when the sources said how many there are; <see langword="null"/>
/// when it is only known that there are more.
/// </param>
/// <param name="Next">The lookups that bring in the next part, one per held-back expansion of the actor.</param>
/// <param name="Positions">Where the user placed the clue, per layout; empty when it has not been placed.</param>
public sealed record HeldBackClue(
    string Key,
    string Anchor,
    long? Remaining,
    IReadOnlyList<ExpansionRequest> Next,
    IReadOnlyDictionary<GraphLayout, NodePosition> Positions)
{
    /// <summary>The prefix of a clue's key.</summary>
    public const string KeyPrefix = "held-back:";

    /// <summary>
    /// Gets the address whose history can be read for what was held back (sketch EX1), or <see langword="null"/> when
    /// the clue is a transaction's or an Ethereum address's, where the next part is brought in instead.
    /// </summary>
    public Chains.Contracts.BitcoinAddress? Address { get; init; }

    /// <summary>Gets how many held-back transactions the user picked out (sketch EX2).</summary>
    public int Chosen { get; init; }

    /// <summary>
    /// Gets the date the clue says under its count: the oldest picked transaction's when some were picked, else the
    /// oldest brought in, since what was held back is older; <see langword="null"/> when no time is known.
    /// </summary>
    public DateTimeOffset? Oldest { get; init; }

    /// <summary>How many more transactions one press on a clue brings in, the same as a first expansion.</summary>
    public const int NextPart = 50;
}
