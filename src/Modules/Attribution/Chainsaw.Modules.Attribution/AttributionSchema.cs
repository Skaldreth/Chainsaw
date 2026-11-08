using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// The Attribution module's tables in the case: every label and every taking back, sealed in one hash chain, so that a
/// label can never change silently and what was believed when can always be read; and every raw response from a
/// supplier of attribution, sealed in another (principle 1).
/// </summary>
internal sealed class AttributionSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "attribution";

    /// <summary>The sealed chain of labels.</summary>
    public const string LabelsChain = "attribution.labels";

    /// <summary>The table of labels.</summary>
    public const string LabelsTable = "attribution_labels";

    /// <summary>The sealed columns of a label or of its taking back, in order.</summary>
    public static readonly IReadOnlyList<string> LabelColumns =
    [
        "action", "label_sequence", "origin_id", "subject_kind", "subject", "kind", "name", "source",
        "original_source", "source_name", "source_version", "set_by", "set_utc", "reason", "note", "do_not_spread",
        "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed chain of the suppliers' raw responses.</summary>
    public const string ResponsesChain = "attribution.responses";

    /// <summary>The table of the suppliers' raw responses.</summary>
    public const string ResponsesTable = "attribution_responses";

    /// <summary>The sealed columns of a supplier's response, in order.</summary>
    public static readonly IReadOnlyList<string> ResponseColumns =
    [
        "provider", "url", "chain", "address", "status_code", "content_type", "requested_utc", "received_utc",
        "received_local", "time_zone", "exit_id", "route", "fetched_by", "sha256", "body",
    ];

    /// <summary>The sealed chain of the cases copies came from.</summary>
    public const string LabelCasesChain = "attribution.label_cases";

    /// <summary>The table of the cases copies came from.</summary>
    public const string LabelCasesTable = "attribution_label_cases";

    /// <summary>The sealed columns of where a copy came from, in order.</summary>
    public static readonly IReadOnlyList<string> LabelCaseColumns =
    [
        "label_sequence", "case_number", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The first version of the table.</summary>
    private const string Version1 = """
        CREATE TABLE attribution_labels (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            label_sequence INTEGER,
            origin_id TEXT NOT NULL,
            subject_kind TEXT NOT NULL,
            subject TEXT NOT NULL,
            kind TEXT NOT NULL,
            name TEXT,
            source TEXT,
            original_source TEXT,
            source_name TEXT,
            source_version TEXT,
            set_by TEXT,
            set_utc TEXT,
            reason TEXT,
            note TEXT,
            do_not_spread INTEGER,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <summary>The second version: the suppliers' raw responses.</summary>
    private const string Version2 = """
        CREATE TABLE attribution_responses (
            sequence INTEGER PRIMARY KEY,
            provider TEXT NOT NULL,
            url TEXT NOT NULL,
            chain TEXT NOT NULL,
            address TEXT NOT NULL,
            status_code INTEGER NOT NULL,
            content_type TEXT,
            requested_utc TEXT NOT NULL,
            received_utc TEXT NOT NULL,
            received_local TEXT NOT NULL,
            time_zone TEXT NOT NULL,
            exit_id TEXT,
            route TEXT NOT NULL,
            fetched_by TEXT NOT NULL,
            sha256 BLOB NOT NULL,
            body BLOB NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <summary>
    /// The third version: the case each copy from the library was set in (CLAUDE.md, <em>Allt följer med</em>), in a
    /// chain of its own so that the labels sealed before keep their hashes.
    /// </summary>
    private const string Version3 = """
        CREATE TABLE attribution_label_cases (
            sequence INTEGER PRIMARY KEY,
            label_sequence INTEGER NOT NULL,
            case_number TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [
            new(1, "Etiketter med ursprung och tillbakatagningar", Version1),
            new(2, "Underleverantörernas svar", Version2),
            new(3, "Ärendet varje kopia från biblioteket sattes i", Version3),
        ];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
    [
        new(LabelsChain, LabelsTable, LabelColumns),
        new(ResponsesChain, ResponsesTable, ResponseColumns),
        new(LabelCasesChain, LabelCasesTable, LabelCaseColumns),
    ];
}
