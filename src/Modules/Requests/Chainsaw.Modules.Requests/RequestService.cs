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
/// Composes requests from the templates, exports them as Markdown that never overwrites, and seals every export in
/// the case with a line in the journal (sketch BG1b).
/// </summary>
/// <param name="clock">The time a request is made.</param>
/// <param name="profiles">The operator who makes it, written in the request.</param>
/// <param name="contacts">The verified ways to reach the recipients.</param>
internal sealed class RequestService(IClock clock, IOperatorProfileStore profiles, ExchangeContacts contacts)
    : IRequestService
{
    /// <summary>The file's encoding: UTF-8 without a byte order mark, which every program reads.</summary>
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc/>
    public RequestDocument Compose(ICase @case, RequestDraft draft)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        return RequestTemplates.Compose(draft, Context(@case, draft.MadeUtc ?? clock.UtcNow));
    }

    /// <inheritdoc/>
    public RequestExport Export(ICase @case, RequestDraft draft, string path)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var document = RequestTemplates.Compose(draft, Context(@case, draft.MadeUtc ?? clock.UtcNow));
        var bytes = FileEncoding.GetBytes(document.Markdown);
        var written = RequestFiles.WriteNew(path, bytes);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        try
        {
            return @case.Write(transaction => Seal(transaction, draft, written, sha));
        }
        catch
        {
            // The case does not know of the file, so it must not stay (principle 1).
            RequestFiles.TryDelete(written);
            throw;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<RequestExport> ListExports(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(ReadExports);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ExchangeContact> ContactsFor(string name) => contacts.For(name);

    /// <summary>Reads the exports, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The exports.</returns>
    internal static IReadOnlyList<RequestExport> ReadExports(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, kind, language, target_key, target_name, file_name, path, sha256, transactions,
                   journal_sequence, recorded_utc
            FROM requests_exports ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var exports = new List<RequestExport>();
        while (rows.Read())
        {
            exports.Add(new RequestExport(
                rows.GetInt64(0),
                Enum.Parse<RequestKind>(rows.GetString(1)),
                Enum.Parse<RequestLanguage>(rows.GetString(2)),
                rows.GetString(3),
                rows.GetString(4),
                rows.GetString(5),
                rows.GetString(6),
                rows.GetString(7),
                JsonSerializer.Deserialize<List<string>>(rows.GetString(8)) ?? [],
                DateTimeOffset.Parse(rows.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                rows.GetInt64(9)));
        }

        return exports;
    }

    /// <summary>Records the export in the journal and seals it, in the case's transaction.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="draft">What the request was made from.</param>
    /// <param name="path">Where the file was written.</param>
    /// <param name="sha">The file's SHA-256.</param>
    /// <returns>The export.</returns>
    private static RequestExport Seal(ICaseTransaction transaction, RequestDraft draft, string path, string sha)
    {
        var fileName = Path.GetFileName(path);
        var transactions = draft.Deposits.Select(deposit => deposit.Transaction).ToList();
        var details = JsonSerializer.Serialize(new
        {
            kind = draft.Kind.ToString(),
            language = draft.Language.ToString(),
            target = draft.ExportKey,
            file = fileName,
            path,
            sha256 = sha,
            transactions,
            trail = draft.Trail is not null,
        });
        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Begäran till {draft.Target.Name} exporterad som {fileName} (SHA-256 {sha}).");
        var journal = transaction.AppendJournal(new JournalDraft(RequestJournalKinds.Exported, summary, details));
        var utc = journal.Utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var record = transaction.AppendSealed(
            RequestsSchema.ExportsChain,
            [
                draft.Kind.ToString(),
                draft.Language.ToString(),
                draft.ExportKey,
                draft.Target.Name,
                fileName,
                path,
                sha,
                JsonSerializer.Serialize(transactions),
                journal.Sequence,
                utc,
            ]);
        return new RequestExport(
            record.Sequence, draft.Kind, draft.Language, draft.ExportKey, draft.Target.Name, fileName, path, sha,
            transactions, journal.Utc, journal.Sequence);
    }

    /// <summary>Gives who makes the request and when: the case's number, never its title.</summary>
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
