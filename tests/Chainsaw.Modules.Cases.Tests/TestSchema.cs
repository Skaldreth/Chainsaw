using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>A module with a table of its own and a sealed table, as the Chains module will have.</summary>
internal sealed class TestSchema(params CaseSchemaStep[] extraSteps) : ICaseSchemaContributor
{
    public const string Version1 = """
        CREATE TABLE test_notes (id INTEGER PRIMARY KEY, note TEXT NOT NULL) STRICT;
        CREATE TABLE test_sealed (
            sequence INTEGER PRIMARY KEY,
            note TEXT NOT NULL,
            amount INTEGER NOT NULL,
            hash BLOB NOT NULL
        ) STRICT;
        CREATE TABLE test_log (id INTEGER PRIMARY KEY, step INTEGER NOT NULL) STRICT;
        INSERT INTO test_log (step) VALUES (1);
        """;

    public string Module => "test";

    public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [new(1, "Testtabellerna", Version1), .. extraSteps];

    public IReadOnlyList<SealedChain> SealedChains { get; } =
        [new("test.sealed", "test_sealed", ["note", "amount"])];
}
