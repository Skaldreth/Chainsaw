using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// The Clustering module's tables in the case: everything the analyses concluded and every split the user made, both
/// sealed, so that nothing about why addresses are merged can change silently (principles 1–3). The clusters
/// themselves are not stored; they are built from these records whenever they are asked for.
/// </summary>
internal sealed class ClusteringSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "clustering";

    /// <summary>The sealed chain of the analyses' conclusions.</summary>
    public const string EvidenceChain = "clustering.evidence";

    /// <summary>The table of the analyses' conclusions.</summary>
    public const string EvidenceTable = "clustering_evidence";

    /// <summary>The sealed chain of the user's splits.</summary>
    public const string SplitsChain = "clustering.splits";

    /// <summary>The table of the user's splits.</summary>
    public const string SplitsTable = "clustering_splits";

    /// <summary>The sealed columns of a conclusion, in order.</summary>
    public static readonly IReadOnlyList<string> EvidenceColumns =
    [
        "record_kind", "heuristic", "heuristic_version", "txid", "addresses", "strength", "change_output", "reason",
        "adjustment", "finding", "fingerprint", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed columns of a split, in order.</summary>
    public static readonly IReadOnlyList<string> SplitColumns =
    [
        "first_address", "second_address", "heuristic", "reason", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The first version of the tables.</summary>
    private const string Version1 = """
        CREATE TABLE clustering_evidence (
            sequence INTEGER PRIMARY KEY,
            record_kind TEXT NOT NULL,
            heuristic TEXT NOT NULL,
            heuristic_version INTEGER NOT NULL,
            txid TEXT NOT NULL,
            addresses TEXT,
            strength TEXT,
            change_output INTEGER,
            reason TEXT,
            adjustment TEXT,
            finding TEXT,
            fingerprint TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE clustering_splits (
            sequence INTEGER PRIMARY KEY,
            first_address TEXT NOT NULL,
            second_address TEXT NOT NULL,
            heuristic TEXT NOT NULL,
            reason TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [new(1, "Klustringens slutsatser och uppbrytningar (förseglade)", Version1)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
    [
        new(EvidenceChain, EvidenceTable, EvidenceColumns),
        new(SplitsChain, SplitsTable, SplitColumns),
    ];
}
