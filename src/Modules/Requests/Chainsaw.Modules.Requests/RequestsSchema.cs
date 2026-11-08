using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// The Requests module's table in the case: every exported request with its file's SHA-256, in a hash chain of its
/// own, so that no export can be changed or removed silently (principle 1).
/// </summary>
internal sealed class RequestsSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "requests";

    /// <summary>The sealed chain of exports.</summary>
    public const string ExportsChain = "requests.exports";

    /// <summary>The sealed chain of exported reports (sketch RP1b).</summary>
    public const string ReportsChain = "requests.reports";

    /// <summary>The sealed columns of an exported report, in order.</summary>
    public static readonly IReadOnlyList<string> ReportColumns =
    [
        "file_name", "path", "sha256", "transactions", "parts", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed columns of an export, in order.</summary>
    public static readonly IReadOnlyList<string> ExportColumns =
    [
        "kind", "language", "target_key", "target_name", "file_name", "path", "sha256", "transactions",
        "journal_sequence", "recorded_utc",
    ];

    /// <summary>The first version of the table.</summary>
    private const string Version1 = """
        CREATE TABLE requests_exports (
            sequence INTEGER PRIMARY KEY,
            kind TEXT NOT NULL,
            language TEXT NOT NULL,
            target_key TEXT NOT NULL,
            target_name TEXT NOT NULL,
            file_name TEXT NOT NULL,
            path TEXT NOT NULL,
            sha256 TEXT NOT NULL,
            transactions TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <summary>The second version: the exported reports (sketch RP1b).</summary>
    private const string Version2 = """
        CREATE TABLE requests_reports (
            sequence INTEGER PRIMARY KEY,
            file_name TEXT NOT NULL,
            path TEXT NOT NULL,
            sha256 TEXT NOT NULL,
            transactions TEXT NOT NULL,
            parts TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [new(1, "Exporterade begäranden", Version1), new(2, "Exporterade rapporter", Version2)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
        [
            new(ExportsChain, "requests_exports", ExportColumns),
            new(ReportsChain, "requests_reports", ReportColumns),
        ];
}
