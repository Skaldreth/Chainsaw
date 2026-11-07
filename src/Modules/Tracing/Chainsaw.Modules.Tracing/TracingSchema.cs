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

    /// <summary>
    /// The second version: how the user left the graph (method, layout, amounts and the part on screen), one row.
    /// Ordinary data like the places; a change of method also gets a line in the journal.
    /// </summary>
    private const string Version2 = """
        CREATE TABLE tracing_view (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            method TEXT NOT NULL,
            layout TEXT NOT NULL,
            amount_labels TEXT NOT NULL,
            offset_x REAL,
            offset_y REAL,
            zoom REAL,
            updated_utc TEXT NOT NULL
        ) STRICT;
        """;

    /// <summary>
    /// The third version: what an expansion held back (how many transactions it took and how many there are), so that
    /// the clue <c>+N tx</c> is drawn when the case is opened again (sketch EX1), and which actors the user picked out
    /// of the piles (sketch EX2b), and whether the graph shows the outputs that can never be spent. Ordinary data
    /// like the places.
    /// </summary>
    private const string Version3 = """
        ALTER TABLE tracing_expansions ADD COLUMN held_back INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE tracing_expansions ADD COLUMN held_back_taken INTEGER;
        ALTER TABLE tracing_expansions ADD COLUMN held_back_available INTEGER;
        ALTER TABLE tracing_view ADD COLUMN show_unspendable INTEGER NOT NULL DEFAULT 0;
        CREATE TABLE tracing_picked (
            member_key TEXT PRIMARY KEY,
            picked_utc TEXT NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
    [
        new(1, "Spårningens graf (start, expanderat, platser) och länkar till andra kedjor", Version1),
        new(2, "Grafens läge: spårningsmetod, vy, belopp och det som syns", Version2),
        new(3, "Det som hölls tillbaka vid en expansion och det som plockats ur högarna", Version3),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
        [new(CrossChainChain, CrossChainTable, CrossChainColumns)];
}
