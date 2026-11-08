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
    /// <summary>How many times an export is made again when the case changed between the check and the seal.</summary>
    public const int MostAttempts = 3;

    /// <summary>The file's encoding: UTF-8 without a byte order mark, which every program reads.</summary>
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc/>
    public ReportLedger Check(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return LedgerOf(verifier.Verify(@case));
    }

    /// <summary>
    /// Gives the top of the case's hash chains: one SHA-256 over every chain's head, sorted by name, so that the report
    /// anchors the raw responses, rates and labels as well as the journal.
    /// </summary>
    /// <param name="verification">What the verifier found.</param>
    /// <returns>The top.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/>: the case was found sound but has no journal chain, which every case has.
    /// </exception>
    internal static ReportLedger LedgerOf(CaseVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var journal = verification.Chains.FirstOrDefault(chain => chain.Chain == CaseChains.Journal);
        if (journal is null && verification.Problems.Count == 0)
        {
            throw new CaseException(CaseError.Corrupt);
        }

        var lines = verification.Chains
            .OrderBy(chain => chain.Chain, StringComparer.Ordinal)
            .Select(chain => string.Create(
                CultureInfo.InvariantCulture, $"{chain.Chain}:{chain.HeadSequence}:{chain.HeadHash}"));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
        return new ReportLedger(
            digest, verification.Chains.Count, journal?.HeadSequence ?? 0, verification.CheckedUtc,
            verification.IsIntact);
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
        for (var attempt = 1; ; attempt++)
        {
            if (TryExport(@case, draft, path, lastAttempt: attempt >= MostAttempts) is { } export)
            {
                return export;
            }
        }
    }

    /// <summary>
    /// Checks the hash chains, writes the file and seals it. When the ledger part is on and the journal has grown since
    /// the check, the seal is rolled back and the file removed, so that the file never states a top that was already
    /// old when it was sealed; the last attempt seals all the same, since the file is true for the moment it names.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">What the report is made from.</param>
    /// <param name="path">Where the user wants the file.</param>
    /// <param name="lastAttempt">Whether this attempt seals even when the case has changed.</param>
    /// <returns>The export, or <see langword="null"/> when it is to be made again.</returns>
    private ReportExport? TryExport(ICase @case, ReportDraft draft, string path, bool lastAttempt)
    {
        var checkedDraft = draft.Has(ReportPart.Ledger) ? draft with { Ledger = Check(@case) } : draft;
        var document = ReportTemplate.Compose(checkedDraft, Context(@case, draft.MadeUtc ?? clock.UtcNow));
        var bytes = FileEncoding.GetBytes(document.Markdown);
        var written = RequestFiles.WriteNew(path, bytes);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        try
        {
            return @case.Write(transaction => Seal(transaction, checkedDraft, written, sha, lastAttempt));
        }
        catch (CaseChangedException)
        {
            RequestFiles.TryDelete(written);
            return null;
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
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/>: a row cannot be read.</exception>
    internal static IReadOnlyList<ReportExport> ReadExports(ICaseReader reader) => RequestService.Readable(() =>
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
    });

    /// <summary>Records the export in the journal and seals it, in the case's transaction.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="draft">What the report was made from, with the top hash it holds.</param>
    /// <param name="path">Where the file was written.</param>
    /// <param name="sha">The file's SHA-256.</param>
    /// <param name="lastAttempt">Whether it seals even when the journal has grown since the check.</param>
    /// <returns>The export.</returns>
    /// <exception cref="CaseChangedException">The journal grew since the check; the transaction is rolled back.
    /// </exception>
    private static ReportExport Seal(
        ICaseTransaction transaction, ReportDraft draft, string path, string sha, bool lastAttempt)
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
            ledgerJournalLine = draft.Ledger?.JournalLine,
            ledgerDigest = draft.Ledger?.Digest,
        });
        var summary = string.Create(
            CultureInfo.InvariantCulture, $"Rapport exporterad som {fileName} (SHA-256 {sha}).");
        var journal = transaction.AppendJournal(
            new JournalDraft(RequestJournalKinds.ReportExported, summary, details));
        if (draft.Ledger is { } ledger && journal.Sequence != ledger.JournalLine + 1 && !lastAttempt)
        {
            // Something was recorded between the check and this seal: the file's top is already old.
            throw new CaseChangedException();
        }

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

/// <summary>
/// Says that the case was written to between the check of its hash chains and the seal of a report, so that the
/// export is rolled back and made again.
/// </summary>
internal sealed class CaseChangedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CaseChangedException()
        : base("The case changed between the check and the seal.")
    {
    }
}
