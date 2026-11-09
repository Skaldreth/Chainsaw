using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Clustering.Tests;

public sealed class ClusteringModuleTests
{
    [Fact]
    public void AddClusteringModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddClusteringModule());
    }

    [Fact]
    public void AddClusteringModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => ClusteringModule.AddClusteringModule(null!));
    }

    [Fact]
    public void The_module_registers_five_heuristics_coinjoin_first_and_its_sealed_tables()
    {
        using var provider = new ServiceCollection().AddClusteringModule().BuildServiceProvider();

        var service = provider.GetRequiredService<IClusteringService>();
        Assert.Equal(
            ["coinjoin v2", "common-input-ownership v1", "change-address v2", "peel-chain v2", "payjoin v2"],
            service.Heuristics.Select(heuristic => heuristic.ToString()));
        var schema = Assert.Single(provider.GetServices<ICaseSchemaContributor>());
        Assert.Equal("clustering", schema.Module);
        Assert.Equal(
            [
                "clustering.evidence", "clustering.splits", "clustering.split_undos", "clustering.merges",
                "clustering.merge_undos", "clustering.withdrawals", "clustering.withdrawal_undos",
                "clustering.held_back_decisions",
            ],
            schema.SealedChains.Select(chain => chain.Name));
    }

    [Fact]
    public void Two_analyses_with_the_same_name_and_version_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new ClusteringService(
            [new CommonInputOwnershipHeuristic(), new CommonInputOwnershipHeuristic()]));
    }
}
