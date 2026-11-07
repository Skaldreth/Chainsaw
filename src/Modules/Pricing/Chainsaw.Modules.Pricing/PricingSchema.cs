using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// The Pricing module's tables in the case: the sealed raw responses from the rate sources (principle 1) and two
/// indexes over them that only say where a usable answer is; they hold nothing the responses do not.
/// </summary>
internal sealed class PricingSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "pricing";

    /// <summary>The sealed chain of raw responses.</summary>
    public const string RawResponsesChain = "pricing.raw_responses";

    /// <summary>The table of the raw responses.</summary>
    public const string RawResponsesTable = "pricing_raw_responses";

    /// <summary>The columns of a raw response that are sealed, in order.</summary>
    public static readonly IReadOnlyList<string> RawResponseColumns =
    [
        "source", "url", "kind", "subject", "status_code", "media_type", "requested_utc", "received_utc",
        "received_local", "time_zone", "exit_id", "fetched_by", "sha256", "body",
    ];

    /// <summary>The first version of the tables.</summary>
    private const string Version1 = """
        CREATE TABLE pricing_raw_responses (
            sequence INTEGER PRIMARY KEY,
            source TEXT NOT NULL,
            url TEXT NOT NULL,
            kind TEXT NOT NULL,
            subject TEXT NOT NULL,
            status_code INTEGER NOT NULL,
            media_type TEXT,
            requested_utc TEXT NOT NULL,
            received_utc TEXT NOT NULL,
            received_local TEXT NOT NULL,
            time_zone TEXT NOT NULL,
            exit_id TEXT NOT NULL,
            fetched_by TEXT NOT NULL,
            sha256 BLOB NOT NULL,
            body BLOB NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE pricing_lookup (
            source TEXT NOT NULL,
            kind TEXT NOT NULL,
            subject TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            PRIMARY KEY (source, kind, subject, sequence)
        ) STRICT, WITHOUT ROWID;
        CREATE TABLE pricing_fiat_ranges (
            source TEXT NOT NULL,
            series TEXT NOT NULL,
            date_from TEXT NOT NULL,
            date_to TEXT NOT NULL,
            asked_to TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            PRIMARY KEY (source, series, sequence)
        ) STRICT, WITHOUT ROWID;
        CREATE INDEX pricing_fiat_ranges_days ON pricing_fiat_ranges (source, series, date_from, date_to);
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [new(1, "Hämtade kurser (förseglade råsvar) och deras register", Version1)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } =
        [new(RawResponsesChain, RawResponsesTable, RawResponseColumns)];
}
