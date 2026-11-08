using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The Cases module's own tables: what the case says about itself, the chains' heads, the journal and the title.
/// </summary>
internal sealed class CasesSchema : ICaseSchemaContributor
{
    /// <summary>The module name of the Cases module's own schema.</summary>
    public const string ModuleName = "cases";

    /// <summary>The journal, sealed like every other sealed table.</summary>
    public static readonly SealedChain Journal = new(
        CaseChains.Journal,
        "journal",
        [
            "utc", "local", "time_zone", "operator_name", "operator_title", "operator_organization",
            "kind", "summary", "details",
        ]);

    /// <summary>
    /// The case's titles, sealed like the journal: the last record is the title in force, the earlier ones stay.
    /// </summary>
    public static readonly SealedChain Title = new("cases.title", "case_title", ["utc", "title"]);

    /// <summary>The first version of the tables.</summary>
    private const string Version1 = """
        CREATE TABLE case_info (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            case_id TEXT NOT NULL,
            case_number TEXT NOT NULL,
            created_utc TEXT NOT NULL
        ) STRICT;
        CREATE TRIGGER seal_case_info_no_update BEFORE UPDATE ON case_info
            BEGIN SELECT RAISE(ABORT, 'the case information cannot be changed'); END;
        CREATE TRIGGER seal_case_info_no_delete BEFORE DELETE ON case_info
            BEGIN SELECT RAISE(ABORT, 'the case information cannot be removed'); END;
        CREATE TABLE sealed_chain (
            name TEXT PRIMARY KEY,
            table_name TEXT NOT NULL,
            columns TEXT NOT NULL
        ) STRICT;
        CREATE TRIGGER seal_sealed_chain_no_update BEFORE UPDATE ON sealed_chain
            BEGIN SELECT RAISE(ABORT, 'a sealed chain cannot be redefined'); END;
        CREATE TRIGGER seal_sealed_chain_no_delete BEFORE DELETE ON sealed_chain
            BEGIN SELECT RAISE(ABORT, 'a sealed chain cannot be unregistered'); END;
        CREATE TABLE chain_head (
            chain TEXT PRIMARY KEY,
            sequence INTEGER NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE journal (
            sequence INTEGER PRIMARY KEY,
            utc TEXT NOT NULL,
            local TEXT NOT NULL,
            time_zone TEXT NOT NULL,
            operator_name TEXT NOT NULL,
            operator_title TEXT NOT NULL,
            operator_organization TEXT NOT NULL,
            kind TEXT NOT NULL,
            summary TEXT NOT NULL,
            details TEXT,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <summary>The second version: the case's title (stage 11).</summary>
    private const string Version2 = """
        CREATE TABLE case_title (
            sequence INTEGER PRIMARY KEY,
            utc TEXT NOT NULL,
            title TEXT NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        """;

    /// <inheritdoc />
    public string Module => ModuleName;

    /// <inheritdoc />
    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [
            new(1, "Case information, chain heads and the journal", Version1),
            new(2, "The case's title", Version2),
        ];

    /// <inheritdoc />
    public IReadOnlyList<SealedChain> SealedChains { get; } = [Journal, Title];
}
