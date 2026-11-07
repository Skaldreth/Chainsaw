using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// The Attribution module's table in the case: every label and every taking back, sealed in one hash chain, so that a
/// label can never change silently and what was believed when can always be read (principle 1).
/// </summary>
internal sealed class AttributionSchema : ICaseSchemaContributor
{
    /// <summary>The module's name in the case.</summary>
    public const string ModuleName = "attribution";

    /// <summary>The sealed chain of labels.</summary>
    public const string LabelsChain = "attribution.labels";

    /// <summary>The table of labels.</summary>
    public const string LabelsTable = "attribution_labels";

    /// <summary>The sealed columns of a label or of its taking back, in order.</summary>
    public static readonly IReadOnlyList<string> LabelColumns =
    [
        "action", "label_sequence", "origin_id", "subject_kind", "subject", "kind", "name", "source",
        "original_source", "source_name", "source_version", "set_by", "set_utc", "reason", "note", "do_not_spread",
        "journal_sequence", "recorded_utc",
    ];

    /// <summary>The first version of the table.</summary>
    private const string Version1 = """
        CREATE TABLE attribution_labels (
            sequence INTEGER PRIMARY KEY,
            action TEXT NOT NULL,
            label_sequence INTEGER,
            origin_id TEXT NOT NULL,
            subject_kind TEXT NOT NULL,
            subject TEXT NOT NULL,
            kind TEXT NOT NULL,
            name TEXT,
            source TEXT,
            original_source TEXT,
            source_name TEXT,
            source_version TEXT,
            set_by TEXT,
            set_utc TEXT,
            reason TEXT,
            note TEXT,
            do_not_spread INTEGER,
            journal_sequence INTEGER NOT NULL,
            recorded_utc TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc/>
    public string Module => ModuleName;

    /// <inheritdoc/>
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [new(1, "Etiketter med ursprung och tillbakatagningar", Version1)];

    /// <inheritdoc/>
    public IReadOnlyList<SealedChain> SealedChains { get; } = [new(LabelsChain, LabelsTable, LabelColumns)];
}
