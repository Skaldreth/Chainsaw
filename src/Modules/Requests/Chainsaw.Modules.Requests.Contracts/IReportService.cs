using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Requests.Contracts;

/// <summary>
/// The report (sketch RP1b): an underlay for intelligence built from what is marked in the graph, with parts switched
/// on and off, exported as Markdown the way a request is, so that it never overwrites and every export is sealed in the
/// case with a line in the journal.
/// </summary>
/// <remarks>
/// A report takes only what its draft holds: the trails with time, UTC, amount, transaction, block height and hash,
/// the conclusions with why, the labels with their sources and the top hash of the hash chain, with the case number
/// and the operator who made it. Never the victim's name, the case's title or its notes (principle 4). The methods are
/// synchronous and touch the disk and the case; call them off the UI thread.
/// </remarks>
public interface IReportService
{
    /// <summary>Checks the case's hash chains and gives the journal's top, for the part <see cref="ReportPart.Ledger"/>.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>The top as checked.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Closed"/>.</exception>
    ReportLedger Check(ICase @case);

    /// <summary>Composes the report, as the preview shows it and the file will hold it.</summary>
    /// <param name="case">The open case, for its number.</param>
    /// <param name="draft">What the report is made from.</param>
    /// <returns>The document.</returns>
    RequestDocument Compose(ICase @case, ReportDraft draft);

    /// <summary>
    /// Checks the hash chains anew, composes the report at this moment and writes it to a new file, never over another:
    /// when the path is taken the file gets a number, <c>(2)</c>, <c>(3)</c> and so on. The file's SHA-256 is sealed
    /// in the case with a line in the journal, in one transaction; if that fails, the file is removed again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">What the report is made from.</param>
    /// <param name="path">Where the user wants the file; its folder must exist.</param>
    /// <returns>The export, as sealed.</returns>
    /// <exception cref="ArgumentException">The path is not a file's.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder may not be written to.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    ReportExport Export(ICase @case, ReportDraft draft, string path);

    /// <summary>Reads the exported reports of the case, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The exports.</returns>
    IReadOnlyList<ReportExport> ListExports(ICase @case);
}

/// <summary>
/// A report that was exported: sealed in the case with a line in the journal, so that the file can always be checked
/// against its hash (principle 1).
/// </summary>
/// <param name="Sequence">The record's place in the case's sealed chain of reports.</param>
/// <param name="FileName">The file's name as written, with its number when another file had the name.</param>
/// <param name="Path">Where the file was written.</param>
/// <param name="Sha256">The file's SHA-256, in lower-case hex.</param>
/// <param name="Transactions">The transactions of the trails it took along.</param>
/// <param name="Parts">The parts it held.</param>
/// <param name="Utc">When it was exported, in UTC.</param>
/// <param name="JournalSequence">The journal entry that records it.</param>
public sealed record ReportExport(
    long Sequence,
    string FileName,
    string Path,
    string Sha256,
    IReadOnlyList<string> Transactions,
    IReadOnlyList<ReportPart> Parts,
    DateTimeOffset Utc,
    long JournalSequence);
