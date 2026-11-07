using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Chains.Tests;

public sealed class ChainsModuleTests
{
    [Fact]
    public void AddChainsModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddChainsModule());
    }

    [Fact]
    public void AddChainsModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => ChainsModule.AddChainsModule(null!));
    }
}
