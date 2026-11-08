using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The fourth version of Tracing's tables (which held-back transactions were picked, and when a subject was first
/// expanded): a case written by a build that had only the third opens, and its expansions read as before.
/// </summary>
public sealed class HeldBackMigrationTests : IDisposable
{
    private const string CaseNumber = "2026-0078";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-migration-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void A_case_from_the_third_version_opens_and_its_held_back_expansion_has_nothing_picked()
    {
        var address = Wpkh("migrated");
        using (var provider = Provider(new ThirdVersion()))
        using (var @case = provider.GetRequiredService<ICaseStore>().Create(CaseNumber))
        {
            @case.Write(transaction =>
            {
                using var command = transaction.CreateCommand("""
                    INSERT INTO tracing_expansions (kind, subject, transactions, complete, expanded_utc, held_back,
                        held_back_taken, held_back_available)
                    VALUES ('address', $subject, $transactions, 1, '2026-11-01T10:00:00.0000000+00:00', 1, 50, 120)
                    """);
                command.AddParameter("$subject", address.Value)
                    .AddParameter("$transactions", $"[\"{Txid("migrated-0").Value}\"]");
                return command.ExecuteNonQuery();
            });
        }

        using var upgraded = Provider(new TracingSchema());
        using var opened = upgraded.GetRequiredService<ICaseStore>().Open(CaseNumber);

        var expansion = Assert.Single(opened.Read(GraphStateStore.Read).Expansions);
        Assert.Equal(address, expansion.Address);
        Assert.Empty(expansion.Chosen);
        Assert.Equal(new ExpansionHoldBack(50, 120), expansion.HeldBack);
        Assert.Contains(opened.ReadJournal(), entry => entry.Kind == CaseJournalKinds.SchemaUpgraded);
    }

    /// <summary>Makes the Cases module with one Tracing schema, in the test's folder.</summary>
    private ServiceProvider Provider(ICaseSchemaContributor schema)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(new FakeProfiles());
        services.AddSingleton<IClock>(new FakeClock());
        services.AddCasesModule(options =>
        {
            options.Directory = _directory;
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
            options.TimeZone = CaseHarness.TimeZone;
        });
        services.AddSingleton(schema);
        return services.BuildServiceProvider();
    }

    /// <summary>Tracing's tables as a build with the first three versions wrote them.</summary>
    private sealed class ThirdVersion : ICaseSchemaContributor
    {
        private readonly TracingSchema _schema = new();

        public string Module => _schema.Module;

        public IReadOnlyList<CaseSchemaStep> Steps => [.. _schema.Steps.Where(step => step.Version <= 3)];

        public IReadOnlyList<SealedChain> SealedChains => _schema.SealedChains;
    }
}
