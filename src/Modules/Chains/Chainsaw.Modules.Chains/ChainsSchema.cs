using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// The Chains module's tables in the case: the sealed raw responses (principle 1) and two indexes over them that
/// Chainsaw can always rebuild from the responses, since they hold nothing the responses do not.
/// </summary>
internal sealed class ChainsSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "chains";

    /// <summary>The sealed chain of raw responses.</summary>
    public const string RawResponsesChain = SealedResponses.Chain;

    /// <summary>The table of the raw responses.</summary>
    public const string RawResponsesTable = "chains_raw_responses";

    /// <summary>The columns of a raw response that are sealed, in order.</summary>
    public static readonly IReadOnlyList<string> RawResponseColumns =
    [
        "chain", "source", "endpoint_id", "endpoint_name", "url", "kind", "subject", "status_code", "media_type",
        "requested_utc", "received_utc", "received_local", "time_zone", "exit_id", "exit_route", "fetched_by", "sha256",
        "body",
    ];

    /// <summary>The first version of the tables.</summary>
    private const string Version1 = """
        CREATE TABLE chains_raw_responses (
            sequence INTEGER PRIMARY KEY,
            chain TEXT NOT NULL,
            source TEXT NOT NULL,
            endpoint_id TEXT NOT NULL,
            endpoint_name TEXT NOT NULL,
            url TEXT NOT NULL,
            kind TEXT NOT NULL,
            subject TEXT NOT NULL,
            status_code INTEGER NOT NULL,
            media_type TEXT,
            requested_utc TEXT NOT NULL,
            received_utc TEXT NOT NULL,
            received_local TEXT NOT NULL,
            time_zone TEXT NOT NULL,
            exit_id TEXT,
            exit_route TEXT NOT NULL,
            fetched_by TEXT NOT NULL,
            sha256 BLOB NOT NULL,
            body BLOB NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE chains_lookup (
            chain TEXT NOT NULL,
            kind TEXT NOT NULL,
            subject TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            height INTEGER,
            PRIMARY KEY (chain, kind, subject, sequence)
        ) STRICT, WITHOUT ROWID;
        CREATE TABLE chains_bitcoin_seen (
            chain TEXT NOT NULL,
            txid TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            block_height INTEGER,
            PRIMARY KEY (chain, txid, sequence)
        ) STRICT, WITHOUT ROWID;
        CREATE INDEX chains_bitcoin_seen_height ON chains_bitcoin_seen (chain, block_height);
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [new(1, "Hämtade råsvar (förseglade) och deras register", Version1)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
        [new(RawResponsesChain, RawResponsesTable, RawResponseColumns)];
}
