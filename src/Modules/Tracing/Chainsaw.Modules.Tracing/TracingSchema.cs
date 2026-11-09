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

    /// <summary>
    /// The sealed columns of a link or its removal, in order. For one checking the rows by hand: <c>from_txid</c> and
    /// <c>from_output</c> name where the money left. From a Bitcoin coin they are its txid and the output's index;
    /// from an Ethereum movement <c>from_txid</c> is the movement's kind and transaction, <c>native:0x…</c>,
    /// <c>internal:0x…</c> or <c>token:0x…</c>, and <c>from_output</c> its position in the transaction (0 for the
    /// ether the transaction sent, the trace's index for an internal one, the log's index for a token). A Bitcoin
    /// txid never holds a colon, so the two never mix. <c>to_chain</c> and <c>to_id</c> name the other chain's
    /// transaction.
    /// </summary>
    public static readonly IReadOnlyList<string> CrossChainColumns =
    [
        "action", "link_sequence", "from_txid", "from_output", "to_chain", "to_id", "reason", "journal_sequence",
        "recorded_utc",
    ];

    /// <summary>The sealed chain of the graph's comments (sketch KN1a).</summary>
    public const string CommentsChain = "tracing.comments";

    /// <summary>The table of the graph's comments.</summary>
    public const string CommentsTable = "tracing_comments";

    /// <summary>The sealed columns of a comment's record, in order.</summary>
    public static readonly IReadOnlyList<string> CommentColumns =
    [
        "action", "comment_id", "body", "anchor", "x", "y", "set_by", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The sealed chain of the user's choices not to follow a swap (sketch SW1a).</summary>
    public const string SwapStopsChain = "tracing.swap_stops";

    /// <summary>The table of the user's choices not to follow a swap.</summary>
    public const string SwapStopsTable = "tracing_swap_stops";

    /// <summary>
    /// The sealed columns of a choice or its taking back, in order: <c>action</c> is <c>stop</c> or <c>follow</c>,
    /// <c>stop_sequence</c> names the choice a taking back concerns, <c>chain</c> the swap's chain, and
    /// <c>transaction_hash</c> and <c>account</c> name the swap (its hash and the account that swapped, as the chain
    /// writes them).
    /// </summary>
    public static readonly IReadOnlyList<string> SwapStopColumns =
    [
        "action", "stop_sequence", "chain", "transaction_hash", "account", "reason", "journal_sequence",
        "recorded_utc",
    ];

    /// <summary>
    /// The sealed chain of the user's choices to follow the transaction that replaced a gone one (sketch GR9a).
    /// </summary>
    public const string ReplacementFollowsChain = "tracing.replacement_follows";

    /// <summary>The table of the user's choices to follow a replacer.</summary>
    public const string ReplacementFollowsTable = "tracing_replacement_follows";

    /// <summary>
    /// The sealed columns of a choice or its taking back, in order: <c>action</c> is <c>follow</c> or
    /// <c>take-back</c>, <c>follow_sequence</c> names the choice a taking back concerns, <c>gone_txid</c> the
    /// transaction the source no longer knows and <c>replacer_txid</c> the one the trace follows in its place.
    /// </summary>
    public static readonly IReadOnlyList<string> ReplacementFollowColumns =
    [
        "action", "follow_sequence", "gone_txid", "replacer_txid", "reason", "journal_sequence", "recorded_utc",
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

    /// <summary>
    /// The fourth version: which held-back transactions the user picked out of an expansion (sketch EX2), and when a
    /// subject was first expanded, so that an expansion done again keeps its place in the order the piles follow.
    /// </summary>
    private const string Version4 = """
        ALTER TABLE tracing_expansions ADD COLUMN chosen TEXT NOT NULL DEFAULT '[]';
        ALTER TABLE tracing_expansions ADD COLUMN first_expanded_utc TEXT;
        """;

    /// <summary>
    /// The fifth version: the graph's comments (sketch KN1a). What a comment says, its first place, its thread and its
    /// removal are sealed with who and when; where it was moved to and its size are ordinary data like the places.
    /// </summary>
    private const string Version5 = """
        CREATE TABLE tracing_comments (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            comment_id TEXT NOT NULL,
            body TEXT NOT NULL,
            anchor TEXT,
            x REAL,
            y REAL,
            set_by TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE tracing_comment_places (
            comment_id TEXT PRIMARY KEY,
            x REAL NOT NULL,
            y REAL NOT NULL,
            width REAL,
            height REAL,
            updated_utc TEXT NOT NULL
        ) STRICT;
        """;

    /// <summary>
    /// The sixth version: the user's choices that the traced value does not follow through a swap, and their taking
    /// back (sketch SW1a), sealed with who and when like the links to other chains.
    /// </summary>
    private const string Version6 = """
        CREATE TABLE tracing_swap_stops (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            stop_sequence INTEGER,
            chain TEXT NOT NULL,
            transaction_hash TEXT NOT NULL,
            account TEXT NOT NULL,
            reason TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <summary>
    /// The seventh version: the user's choices to follow the transaction that replaced one the source no longer knows,
    /// and their taking back (sketch GR9a), sealed with who and when like the choices about swaps.
    /// </summary>
    private const string Version7 = """
        CREATE TABLE tracing_replacement_follows (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            follow_sequence INTEGER,
            gone_txid TEXT NOT NULL,
            replacer_txid TEXT NOT NULL,
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
    [
        new(1, "Spårningens graf (start, expanderat, platser) och länkar till andra kedjor", Version1),
        new(2, "Grafens läge: spårningsmetod, vy, belopp och det som syns", Version2),
        new(3, "Det som hölls tillbaka vid en expansion och det som plockats ur högarna", Version3),
        new(4, "Utplockade tx ur det som hölls tillbaka, och när en expansion gjordes första gången", Version4),
        new(5, "Kommentarer i grafen, förseglade med vem och när, och var de står", Version5),
        new(6, "Val att spåret inte följer med genom en swap, förseglade med vem och när", Version6),
        new(7, "Val att spåret följer tx:en som ersatte en som inte finns längre, förseglade med vem och när",
            Version7),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
    [
        new(CrossChainChain, CrossChainTable, CrossChainColumns),
        new(CommentsChain, CommentsTable, CommentColumns),
        new(SwapStopsChain, SwapStopsTable, SwapStopColumns),
        new(ReplacementFollowsChain, ReplacementFollowsTable, ReplacementFollowColumns),
    ];
}
