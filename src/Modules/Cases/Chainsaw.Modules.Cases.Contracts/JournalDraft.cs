namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>What a module wants recorded in the journal; the time and the operator are added by the case.</summary>
/// <param name="Kind">
/// A stable code for what happened, for example <c>case.created</c>: lower-case ASCII letters, digits and
/// <c>. - _</c>, starting with a letter, at most 64 characters. Kinds that start with <c>case.</c> are written only
/// by the Cases module (<see cref="CaseJournalKinds"/>).
/// </param>
/// <param name="Summary">One line in plain language; not empty, at most 1 000 characters.</param>
/// <param name="Details">Optional details, preferably JSON, at most 1 MiB of text.</param>
public sealed record JournalDraft(string Kind, string Summary, string? Details = null);
