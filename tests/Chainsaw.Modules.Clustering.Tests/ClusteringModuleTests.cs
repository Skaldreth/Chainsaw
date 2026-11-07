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
}
