using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Attribution.Tests;

public sealed class AttributionModuleTests
{
    [Fact]
    public void AddAttributionModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddAttributionModule());
    }

    [Fact]
    public void AddAttributionModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => AttributionModule.AddAttributionModule(null!));
    }
}
