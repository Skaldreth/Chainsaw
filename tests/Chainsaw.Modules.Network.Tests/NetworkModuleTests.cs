using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Network.Tests;

public sealed class NetworkModuleTests
{
    [Fact]
    public void AddNetworkModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddNetworkModule());
    }

    [Fact]
    public void AddNetworkModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => NetworkModule.AddNetworkModule(null!));
    }
}
