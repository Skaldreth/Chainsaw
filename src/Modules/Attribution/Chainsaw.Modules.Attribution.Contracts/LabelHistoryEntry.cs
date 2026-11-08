using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// That a label was taken back: a sealed record of its own, with who, when and why, so that the label stays readable
/// and what was believed when is never lost (principle 1).
/// </summary>
/// <param name="Sequence">The record's place in the case's sealed chain of labels.</param>
/// <param name="By">
/// Who took it back; <see langword="null"/> for a record sealed before who was stored with it, when the journal entry
/// (<paramref name="JournalSequence"/>) says who.
/// </param>
/// <param name="Utc">When, in UTC.</param>
/// <param name="Reason">Why, or <see langword="null"/> when no reason was given; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry that records it.</param>
public sealed record LabelRetraction(
    long Sequence, OperatorProfile? By, DateTimeOffset Utc, string? Reason, long JournalSequence);

/// <summary>A label in a case's history, with its taking back when it has been taken back (sketch AN1b).</summary>
/// <param name="Label">The sealed label.</param>
/// <param name="Retraction">Its taking back, or <see langword="null"/> while it counts.</param>
public sealed record LabelHistoryEntry(LabelEntry Label, LabelRetraction? Retraction)
{
    /// <summary>Gets whether the label is taken back and no longer counts.</summary>
    public bool IsRetracted => Retraction is not null;

    /// <summary>
    /// Gets whether the label is the user's own judgement in this case: one set with the form, as own or from an open
    /// source. A label from a sanction list, from another case or from an analysis of behaviour is not.
    /// </summary>
    public bool IsUsersOwn => Label.Origin.Source is LabelSource.Own or LabelSource.OpenSource;

    /// <summary>
    /// Gets whether the user can take the label back: it is the user's own, or a supplier's report the user does not
    /// believe, and not taken back already (a label taken back is set anew instead).
    /// </summary>
    public bool CanRetract => (IsUsersOwn || Label.Origin.Source == LabelSource.Provider) && !IsRetracted;

    /// <summary>
    /// Gets whether the user can mark the label <em>Sprid inte</em> or let it spread again: the user's own, still
    /// counting, and not on a victim, whose label never spreads whatever it is marked.
    /// </summary>
    public bool CanChangeSpread => IsUsersOwn && !IsRetracted && Label.Kind != LabelKind.Victim;
}
