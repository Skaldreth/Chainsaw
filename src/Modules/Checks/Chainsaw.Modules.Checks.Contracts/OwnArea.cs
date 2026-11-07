using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>The colours an own area can have (sketch KF1b): the graph's tints, never the accent.</summary>
public enum AreaTint
{
    /// <summary>Grey, as wallets and time.</summary>
    Grey,

    /// <summary>Sand, as layers of obfuscation.</summary>
    Sand,

    /// <summary>Blue, as withdrawal.</summary>
    Blue,

    /// <summary>Purple, as another chain.</summary>
    Lila,

    /// <summary>Green, the own areas' own colour.</summary>
    Green,

    /// <summary>Red, as risk: mixers and CoinJoin.</summary>
    Red,
}

/// <summary>
/// An area the user drew around actors (sketch KF1b, <em>egen yta</em>): the user's own judgement, sealed in the case
/// with who, when and why like a label, renamed and removed the same way, so that the old stays readable.
/// </summary>
/// <param name="Id">The area's stable id.</param>
/// <param name="Name">Its name, the latest given.</param>
/// <param name="Sort">The sort it is shown under; usually <see cref="AreaSort.Own"/>.</param>
/// <param name="Tint">Its colour.</param>
/// <param name="SameActor">Whether the user believes the actors are the same actor.</param>
/// <param name="Note">The user's note, which is the area's why; <see langword="null"/> when none was written.</param>
/// <param name="Members">
/// The actors it holds: <c>address:</c> and an address, or <c>tx:</c> and a txid, so that it follows the actors when
/// clusters change.
/// </param>
/// <param name="CreatedBy">Who drew it.</param>
/// <param name="CreatedUtc">When.</param>
/// <param name="Sequence">The record that drew it in the sealed chain <c>checks.areas</c>.</param>
public sealed record OwnArea(
    string Id,
    string Name,
    AreaSort Sort,
    AreaTint Tint,
    bool SameActor,
    string? Note,
    IReadOnlyList<string> Members,
    OperatorProfile CreatedBy,
    DateTimeOffset CreatedUtc,
    long Sequence);

/// <summary>An own area to draw.</summary>
/// <param name="Name">Its name; required.</param>
/// <param name="Sort">The sort it is shown under.</param>
/// <param name="Tint">Its colour.</param>
/// <param name="SameActor">Whether the user believes the actors are the same actor.</param>
/// <param name="Note">The note, the area's why; optional.</param>
/// <param name="Members">The actors (see <see cref="OwnArea.Members"/>); at least one.</param>
public sealed record OwnAreaDraft(
    string Name, AreaSort Sort, AreaTint Tint, bool SameActor, string? Note, IReadOnlyList<string> Members);

/// <summary>The rules for own areas and decisions: lengths and the members' form.</summary>
public static class CheckRules
{
    /// <summary>The longest name of an own area.</summary>
    public const int MaximumNameLength = 80;

    /// <summary>The longest note or reason.</summary>
    public const int MaximumTextLength = 1_000;

    /// <summary>The most actors an own area holds.</summary>
    public const int MaximumMembers = 500;

    /// <summary>The prefix of an address member.</summary>
    public const string AddressMember = "address:";

    /// <summary>The prefix of a transaction member.</summary>
    public const string TransactionMember = "tx:";

    /// <summary>Gives the member that stands for an address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>For example <c>address:bc1q…</c>.</returns>
    public static string MemberOf(Chains.Contracts.BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return AddressMember + address.Value;
    }

    /// <summary>Gives the member that stands for a transaction that is an actor of its own.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>For example <c>tx:4a5e…</c>.</returns>
    public static string MemberOf(Chains.Contracts.Txid txid)
    {
        ArgumentNullException.ThrowIfNull(txid);
        return TransactionMember + txid.Value;
    }
}
