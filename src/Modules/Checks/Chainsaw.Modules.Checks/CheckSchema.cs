using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The Checks module's tables in the case: every finding the analyses sealed, every decision about a suggestion and
/// every own area, each in its own hash chain, so that nothing about them can change silently (principles 1–3).
/// </summary>
internal sealed class CheckSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "checks";

    /// <summary>The sealed chain of findings.</summary>
    public const string ResultsChain = "checks.results";

    /// <summary>The table of findings.</summary>
    public const string ResultsTable = "checks_results";

    /// <summary>The sealed chain of decisions.</summary>
    public const string DecisionsChain = "checks.decisions";

    /// <summary>The table of decisions.</summary>
    public const string DecisionsTable = "checks_decisions";

    /// <summary>The sealed chain of own areas.</summary>
    public const string AreasChain = "checks.areas";

    /// <summary>The table of own areas.</summary>
    public const string AreasTable = "checks_areas";

    /// <summary>The sealed columns of a finding, in order.</summary>
    public static readonly IReadOnlyList<string> ResultColumns =
    [
        "analysis", "analysis_version", "finding_key", "kind", "content", "fingerprint", "journal_sequence",
        "recorded_utc",
    ];

    /// <summary>The sealed columns of a decision, in order.</summary>
    public static readonly IReadOnlyList<string> DecisionColumns =
    [
        "analysis", "analysis_version", "finding_key", "candidate_key", "verdict", "reason", "decided_by",
        "result_sequence", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed columns of an own area's record, in order.</summary>
    public static readonly IReadOnlyList<string> AreaColumns =
    [
        "action", "area_id", "name", "sort", "tint", "same_actor", "note", "members", "reason", "set_by",
        "journal_sequence", "recorded_utc",
    ];

    /// <summary>The first version of the tables.</summary>
    private const string Version1 = """
        CREATE TABLE checks_results (
            sequence INTEGER PRIMARY KEY,
            analysis TEXT NOT NULL,
            analysis_version INTEGER NOT NULL,
            finding_key TEXT NOT NULL,
            kind TEXT NOT NULL,
            content TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE checks_decisions (
            sequence INTEGER PRIMARY KEY,
            analysis TEXT NOT NULL,
            analysis_version INTEGER NOT NULL,
            finding_key TEXT NOT NULL,
            candidate_key TEXT,
            verdict TEXT NOT NULL,
            reason TEXT,
            decided_by TEXT NOT NULL,
            result_sequence INTEGER NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE checks_areas (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            area_id TEXT NOT NULL,
            name TEXT,
            sort TEXT,
            tint TEXT,
            same_actor INTEGER,
            note TEXT,
            members TEXT,
            reason TEXT,
            set_by TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [new(1, "Kontroller och förslag, beslut och egna ytor", Version1)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
    [
        new(ResultsChain, ResultsTable, ResultColumns),
        new(DecisionsChain, DecisionsTable, DecisionColumns),
        new(AreasChain, AreasTable, AreaColumns),
    ];
}
