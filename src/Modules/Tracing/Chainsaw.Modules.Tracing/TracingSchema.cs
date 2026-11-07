using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The Tracing module's tables in the case. The graph's state (start points, expansions and places) is ordinary data
/// that may change, since it is an aid and not a basis (compare Gawk's Builder); the user's links to other chains are
/// judgements and are sealed. Nothing computed from the chain is stored: it is built again from the sealed responses
/// and conclusions whenever it is asked for.
/// </summary>
internal sealed class TracingSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "tracing";

    /// <summary>The sealed chain of the user's links to other chains.</summary>
    public const string CrossChainChain = "tracing.cross_chain";

    /// <summary>The table of the user's links to other chains.</summary>
    public const string CrossChainTable = "tracing_cross_chain";

    /// <summary>The sealed columns of a link or its removal, in order.</summary>
    public static readonly IReadOnlyList<string> CrossChainColumns =
    [
        "action", "link_sequence", "from_txid", "from_output", "to_chain", "to_id", "reason", "journal_sequence",
        "recorded_utc",
    ];

    /// <summary>The first version of the tables.</summary>
    private const string Version1 = """
        CREATE TABLE tracing_start_points (
            kind TEXT NOT NULL,
            subject TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            added_utc TEXT NOT NULL,
            PRIMARY KEY (kind, subject)
        ) STRICT;
        CREATE TABLE tracing_expansions (
            kind TEXT NOT NULL,
            subject TEXT NOT NULL,
            transactions TEXT NOT NULL,
            complete INTEGER NOT NULL,
            expanded_utc TEXT NOT NULL,
            PRIMARY KEY (kind, subject)
        ) STRICT;
        CREATE TABLE tracing_positions (
            position_key TEXT NOT NULL,
            layout TEXT NOT NULL,
            x REAL NOT NULL,
            y REAL NOT NULL,
            updated_utc TEXT NOT NULL,
            PRIMARY KEY (position_key, layout)
        ) STRICT;
        CREATE TABLE tracing_cross_chain (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            link_sequence INTEGER,
            from_txid TEXT NOT NULL,
            from_output INTEGER NOT NULL,
            to_chain TEXT NOT NULL,
            to_id TEXT NOT NULL,
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
        [new(1, "Spårningens graf (start, expanderat, platser) och länkar till andra kedjor", Version1)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
        [new(CrossChainChain, CrossChainTable, CrossChainColumns)];
}
