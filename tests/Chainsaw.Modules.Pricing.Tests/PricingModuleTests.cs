using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Pricing.Tests;

public sealed class PricingModuleTests
{
    [Fact]
    public void AddPricingModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddPricingModule());
    }

    [Fact]
    public void AddPricingModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => PricingModule.AddPricingModule(null!));
    }
}
