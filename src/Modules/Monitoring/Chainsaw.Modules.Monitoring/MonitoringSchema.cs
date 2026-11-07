using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// The Monitoring module's tables in the case: every record of a watch and every hit, each in its own hash chain, so
/// that nothing about them can change silently (principle 1); and, as plain data beside them, when each watch was
/// last checked and which hits the user has seen.
/// </summary>
internal sealed class MonitoringSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "monitoring";

    /// <summary>The sealed chain of the watches' records.</summary>
    public const string WatchesChain = "monitoring.watches";

    /// <summary>The sealed chain of hits.</summary>
    public const string HitsChain = "monitoring.hits";

    /// <summary>The sealed columns of a watch's record, in order.</summary>
    public static readonly IReadOnlyList<string> WatchColumns =
    [
        "action", "watch_id", "subject", "filter", "start_height", "reason", "set_by", "journal_sequence",
        "recorded_utc",
    ];

    /// <summary>The sealed columns of a hit, in order.</summary>
    public static readonly IReadOnlyList<string> HitColumns =
    [
        "watch_id", "chain", "txid", "block_height", "block_hash", "block_time", "direction", "asset", "amount",
        "counterparties", "origins", "journal_sequence", "recorded_utc",
    ];

    /// <summary>The first version of the tables.</summary>
    private const string Version1 = """
        CREATE TABLE monitoring_watches (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            watch_id TEXT NOT NULL,
            subject TEXT,
            filter TEXT,
            start_height INTEGER,
            reason TEXT,
            set_by TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE monitoring_hits (
            sequence INTEGER PRIMARY KEY,
            watch_id TEXT NOT NULL,
            chain TEXT NOT NULL,
            txid TEXT NOT NULL,
            block_height INTEGER NOT NULL,
            block_hash TEXT NOT NULL,
            block_time TEXT NOT NULL,
            direction TEXT NOT NULL,
            asset TEXT NOT NULL,
            amount TEXT NOT NULL,
            counterparties TEXT NOT NULL,
            origins TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE monitoring_checked (
            watch_id TEXT PRIMARY KEY,
            checked_utc TEXT NOT NULL,
            height INTEGER NOT NULL,
            status TEXT NOT NULL,
            problem TEXT
        ) STRICT;
        CREATE TABLE monitoring_seen (
            watch_id TEXT PRIMARY KEY,
            through_sequence INTEGER NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } = [new(1, "Bevakningar och träffar", Version1)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
    [
        new(WatchesChain, "monitoring_watches", WatchColumns),
        new(HitsChain, "monitoring_hits", HitColumns),
    ];
}
