using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// The Clustering module's tables in the case: everything the analyses concluded, every split and every merge the user
/// made, all sealed, so that nothing about why addresses are merged can change silently (principles 1–3). The clusters
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

    /// <summary>The sealed chain of the user's undoing of splits.</summary>
    public const string SplitUndosChain = "clustering.split_undos";

    /// <summary>The table of the user's undoing of splits.</summary>
    public const string SplitUndosTable = "clustering_split_undos";

    /// <summary>The sealed chain of the user's merges (<c>user v1</c>).</summary>
    public const string MergesChain = "clustering.merges";

    /// <summary>The table of the user's merges.</summary>
    public const string MergesTable = "clustering_merges";

    /// <summary>The sealed chain of the user's undoing of merges.</summary>
    public const string MergeUndosChain = "clustering.merge_undos";

    /// <summary>The table of the user's undoing of merges.</summary>
    public const string MergeUndosTable = "clustering_merge_undos";

    /// <summary>The sealed chain of the versions drawn back.</summary>
    public const string WithdrawalsChain = "clustering.withdrawals";

    /// <summary>The table of the versions drawn back.</summary>
    public const string WithdrawalsTable = "clustering_withdrawals";

    /// <summary>The sealed chain of the withdrawals taken back.</summary>
    public const string WithdrawalUndosChain = "clustering.withdrawal_undos";

    /// <summary>The table of the withdrawals taken back.</summary>
    public const string WithdrawalUndosTable = "clustering_withdrawal_undos";

    /// <summary>The sealed columns of a withdrawal taken back, in order.</summary>
    public static readonly IReadOnlyList<string> WithdrawalUndoColumns =
    [
        "withdrawal_sequence", "reason", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed columns of a version drawn back, in order.</summary>
    public static readonly IReadOnlyList<string> WithdrawalColumns =
    [
        "heuristic", "heuristic_version", "reason", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed columns of a user's merge, in order.</summary>
    public static readonly IReadOnlyList<string> MergeColumns =
    [
        "addresses", "txid", "proposal", "proposal_version", "grounds", "reason", "origin_chain", "origin_sequence",
        "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed columns of an undoing of a merge, in order.</summary>
    public static readonly IReadOnlyList<string> MergeUndoColumns =
    [
        "merge_sequence", "reason", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed columns of an undoing of a split, in order.</summary>
    public static readonly IReadOnlyList<string> SplitUndoColumns =
    [
        "split_sequence", "reason", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed columns of a conclusion, in order.</summary>
    public static readonly IReadOnlyList<string> EvidenceColumns =
    [
        "record_kind", "heuristic", "heuristic_version", "txid", "addresses", "strength", "change_output", "reason",
        "adjustment", "finding", "basis", "fingerprint", "journal_sequence", "recorded_utc",
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
            basis TEXT NOT NULL,
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

    /// <summary>
    /// The second version: the user's undoing of a split (sketch AN1b), sealed as a record of its own that points at
    /// the split, which stays as it was. The reason is optional.
    /// </summary>
    private const string Version2 = """
        CREATE TABLE clustering_split_undos (
            sequence INTEGER PRIMARY KEY,
            split_sequence INTEGER NOT NULL,
            reason TEXT,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <summary>
    /// The third version: the user's own merges (<c>user v1</c>), for example of a wallet the user approved, and
    /// their undoing, each sealed as a record of its own. The reason and the origin are optional.
    /// </summary>
    private const string Version3 = """
        CREATE TABLE clustering_merges (
            sequence INTEGER PRIMARY KEY,
            addresses TEXT NOT NULL,
            txid TEXT NOT NULL,
            proposal TEXT NOT NULL,
            proposal_version INTEGER NOT NULL,
            grounds TEXT NOT NULL,
            reason TEXT,
            origin_chain TEXT,
            origin_sequence INTEGER,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE clustering_merge_undos (
            sequence INTEGER PRIMARY KEY,
            merge_sequence INTEGER NOT NULL,
            reason TEXT,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <summary>
    /// The fourth version: a version of an analysis drawn back, so that its conclusions stop counting without being
    /// removed, and the taking back of a withdrawal. The reasons are optional.
    /// </summary>
    private const string Version4 = """
        CREATE TABLE clustering_withdrawals (
            sequence INTEGER PRIMARY KEY,
            heuristic TEXT NOT NULL,
            heuristic_version INTEGER NOT NULL,
            reason TEXT,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE clustering_withdrawal_undos (
            sequence INTEGER PRIMARY KEY,
            withdrawal_sequence INTEGER NOT NULL,
            reason TEXT,
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
        new(1, "Klustringens slutsatser och uppbrytningar (förseglade)", Version1),
        new(2, "Ångrade uppbrytningar (förseglade)", Version2),
        new(3, "Användarens sammanslagningar och deras ångrande (förseglade)", Version3),
        new(4, "Versioner av analyser som dragits tillbaka och tillbakatagandena (förseglade)", Version4),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
    [
        new(EvidenceChain, EvidenceTable, EvidenceColumns),
        new(SplitsChain, SplitsTable, SplitColumns),
        new(SplitUndosChain, SplitUndosTable, SplitUndoColumns),
        new(MergesChain, MergesTable, MergeColumns),
        new(MergeUndosChain, MergeUndosTable, MergeUndoColumns),
        new(WithdrawalsChain, WithdrawalsTable, WithdrawalColumns),
        new(WithdrawalUndosChain, WithdrawalUndosTable, WithdrawalUndoColumns),
    ];
}
