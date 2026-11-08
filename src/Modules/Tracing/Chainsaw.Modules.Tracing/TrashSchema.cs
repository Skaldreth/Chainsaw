using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The trash's table in the case (sketch SO1a): every throw and every putting back is a sealed record, so that what is
/// hidden stays hidden when the case is opened again and nothing is ever removed (principle 1). It is a contributor of
/// its own, beside <see cref="TracingSchema"/>, so that its versions never mix with the graph's state.
/// </summary>
internal sealed class TrashSchema : ICaseSchemaContributor
{
    /// <summary>The trash's name in the case.</summary>
    public const string ModuleName = "tracing_trash";

    /// <summary>The sealed chain of throws and puttings back.</summary>
    public const string Chain = "tracing_trash.records";

    /// <summary>The table of throws and puttings back.</summary>
    public const string Table = "tracing_trash";

    /// <summary>The sealed columns of a record, in order.</summary>
    public static readonly IReadOnlyList<string> Columns =
        ["action", "throw_sequence", "members", "operator", "journal_sequence", "recorded_utc"];

    /// <summary>The first version: the sealed table.</summary>
    private const string Version1 = """
        CREATE TABLE tracing_trash (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            throw_sequence INTEGER,
            members TEXT NOT NULL,
            operator TEXT NOT NULL,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [new(1, "Soptunnan: det som slängts ur grafen och lagts tillbaka", Version1)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } = [new(Chain, Table, Columns)];
}
