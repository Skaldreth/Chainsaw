using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// Composes reports from what is marked in the graph, exports them as Markdown that never overwrites, and seals every
/// export in the case with a line in the journal (sketch RP1b), the way <see cref="RequestService"/> does a request.
/// </summary>
/// <param name="clock">The time a report is made.</param>
/// <param name="profiles">The operator who makes it, written in the report.</param>
/// <param name="verifier">The check of the case's hash chains, whose top the report gives.</param>
internal sealed class ReportService(IClock clock, IOperatorProfileStore profiles, ICaseVerifier verifier)
    : IReportService
{
    /// <summary>The chain whose top is the report's top hash: the journal, which every write in the case adds to.</summary>
    public const string JournalChain = "cases.journal";

    /// <summary>The file's encoding: UTF-8 without a byte order mark, which every program reads.</summary>
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc/>
    public ReportLedger Check(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var verification = verifier.Verify(@case);
        var journal = verification.Chains.FirstOrDefault(chain => chain.Chain == JournalChain);
        return new ReportLedger(
            journal?.HeadSequence ?? 0, journal?.HeadHash, verification.CheckedUtc, verification.IsIntact);
    }

    /// <inheritdoc/>
    public RequestDocument Compose(ICase @case, ReportDraft draft)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        return ReportTemplate.Compose(draft, Context(@case, draft.MadeUtc ?? clock.UtcNow));
    }

    /// <inheritdoc/>
    public ReportExport Export(ICase @case, ReportDraft draft, string path)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // The top hash is checked anew, so that the file says how the case stood just before it was written.
        var checkedDraft = draft.Has(ReportPart.Ledger) ? draft with { Ledger = Check(@case) } : draft;
        var document = ReportTemplate.Compose(checkedDraft, Context(@case, draft.MadeUtc ?? clock.UtcNow));
        var bytes = FileEncoding.GetBytes(document.Markdown);
        var written = RequestFiles.WriteNew(path, bytes);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        try
        {
            return @case.Write(transaction => Seal(transaction, checkedDraft, written, sha));
        }
        catch
        {
            // The case does not know of the file, so it must not stay (principle 1).
            RequestFiles.TryDelete(written);
            throw;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReportExport> ListExports(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(ReadExports);
    }

    /// <summary>Reads the exported reports, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The exports.</returns>
    internal static IReadOnlyList<ReportExport> ReadExports(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, file_name, path, sha256, transactions, parts, journal_sequence, recorded_utc
            FROM requests_reports ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var exports = new List<ReportExport>();
        while (rows.Read())
        {
            var parts = JsonSerializer.Deserialize<List<string>>(rows.GetString(5)) ?? [];
            exports.Add(new ReportExport(
                rows.GetInt64(0),
                rows.GetString(1),
                rows.GetString(2),
                rows.GetString(3),
                JsonSerializer.Deserialize<List<string>>(rows.GetString(4)) ?? [],
                [.. parts.Select(Enum.Parse<ReportPart>)],
                DateTimeOffset.Parse(rows.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                rows.GetInt64(6)));
        }

        return exports;
    }

    /// <summary>Records the export in the journal and seals it, in the case's transaction.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="draft">What the report was made from, with the top hash it holds.</param>
    /// <param name="path">Where the file was written.</param>
    /// <param name="sha">The file's SHA-256.</param>
    /// <returns>The export.</returns>
    private static ReportExport Seal(ICaseTransaction transaction, ReportDraft draft, string path, string sha)
    {
        var fileName = Path.GetFileName(path);
        var transactions = draft.Transactions;
        var parts = Enum.GetValues<ReportPart>().Where(draft.Has).ToList();
        var partNames = parts.Select(part => part.ToString()).ToList();
        var details = JsonSerializer.Serialize(new
        {
            file = fileName,
            path,
            sha256 = sha,
            transactions,
            parts = partNames,
            trails = draft.Trails.Count,
            ledgerLine = draft.Ledger?.Line,
            ledgerHash = draft.Ledger?.Hash,
        });
        var summary = string.Create(
            CultureInfo.InvariantCulture, $"Rapport exporterad som {fileName} (SHA-256 {sha}).");
        var journal = transaction.AppendJournal(
            new JournalDraft(RequestJournalKinds.ReportExported, summary, details));
        var utc = journal.Utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var record = transaction.AppendSealed(
            RequestsSchema.ReportsChain,
            [
                fileName,
                path,
                sha,
                JsonSerializer.Serialize(transactions),
                JsonSerializer.Serialize(partNames),
                journal.Sequence,
                utc,
            ]);
        return new ReportExport(
            record.Sequence, fileName, path, sha, transactions, parts, journal.Utc, journal.Sequence);
    }

    /// <summary>Gives who makes the report and when: the case's number, never its title.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="utc">When.</param>
    /// <returns>The context.</returns>
    private RequestContext Context(ICase @case, DateTimeOffset utc) =>
        new(@case.Info.CaseNumber, Operator(), utc);

    /// <summary>Reads the operator's profile; none when the vault is locked or no profile is saved.</summary>
    /// <returns>The profile, or <see langword="null"/>.</returns>
    private OperatorProfile? Operator()
    {
        try
        {
            return profiles.Get();
        }
        catch (VaultException)
        {
            return null;
        }
    }
}
