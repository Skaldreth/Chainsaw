using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Requests.Contracts;

/// <summary>
/// Requests for information to exchanges and for freezing to token issuers (sketch BG1b): composed from templates in
/// Swedish and English, exported as Markdown that never overwrites, and sealed in the case with a line in the journal.
/// </summary>
/// <remarks>
/// A request takes only what it needs: the recipient, its addresses, the deposits chosen with their times (local and
/// UTC), amounts, transactions, heights and hashes, optionally the trail in one sentence, and the case number with the
/// operator who made it. Never the victim's name, the case's title or its notes (principle 4). The methods are
/// synchronous and touch the disk and the case; call them off the UI thread.
/// </remarks>
public interface IRequestService
{
    /// <summary>Composes a request from its template, as the preview shows it and the file will hold it.</summary>
    /// <param name="case">The open case, for its number.</param>
    /// <param name="draft">What the request is made from.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentException">
    /// A request for information has no deposits, or a freeze has no holding.
    /// </exception>
    RequestDocument Compose(ICase @case, RequestDraft draft);

    /// <summary>
    /// Composes the request at this moment and writes it to a new file, never over another: when the path is taken
    /// the file gets a number, <c>(2)</c>, <c>(3)</c> and so on. The file's SHA-256 is sealed in the case with a line
    /// in the journal, in one transaction; if that fails, the file is removed again, so that no file exists that the
    /// case does not know of.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">What the request is made from.</param>
    /// <param name="path">Where the user wants the file; its folder must exist.</param>
    /// <returns>The export, as sealed.</returns>
    /// <exception cref="ArgumentException">As for <see cref="Compose"/>, or the path is not a file's.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder may not be written to.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    RequestExport Export(ICase @case, RequestDraft draft, string path);

    /// <summary>Reads the exported requests of the case, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The exports.</returns>
    IReadOnlyList<RequestExport> ListExports(ICase @case);

    /// <summary>
    /// Gives the verified ways for law enforcement to reach a recipient, each with its source and the date it was
    /// checked; empty when Chainsaw knows of none, which the view says honestly.
    /// </summary>
    /// <param name="name">The recipient's name.</param>
    /// <returns>The ways, or an empty list.</returns>
    IReadOnlyList<ExchangeContact> ContactsFor(string name);
}

/// <summary>
/// A verified way for law enforcement to reach an exchange or an issuer. Only what can be checked is ever added:
/// every way names its source and when it was checked, and none is made up (CLAUDE.md, BG1b).
/// </summary>
/// <param name="Recipient">The exchange or issuer, as its labels name it.</param>
/// <param name="Way">The way, for example a portal's address.</param>
/// <param name="Source">Where it was read: the recipient's own page.</param>
/// <param name="Checked">The day it was last checked.</param>
public sealed record ExchangeContact(string Recipient, string Way, Uri Source, DateOnly Checked);

/// <summary>The kinds of journal entries the module Requests writes.</summary>
public static class RequestJournalKinds
{
    /// <summary>A request was exported to a file.</summary>
    public const string Exported = "requests.exported";
}
