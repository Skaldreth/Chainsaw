using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Tracing.Tests;

public sealed class TracingModuleTests
{
    [Fact]
    public void AddTracingModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddTracingModule());
    }

    [Fact]
    public void AddTracingModule_registers_the_exposure_analysis_with_the_account_methods()
    {
        using var provider = new ServiceCollection().AddTracingModule().BuildServiceProvider();

        var registration = Assert.Single(
            new ServiceCollection().AddTracingModule(), d => d.ServiceType == typeof(Contracts.IExposureAnalysis));
        Assert.Equal(typeof(ExposureAnalysis), registration.ImplementationType);
        Assert.IsType<ExposureAnalysis>(provider.GetRequiredService<Contracts.IExposureAnalysis>());
    }

    [Fact]
    public void AddTracingModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => TracingModule.AddTracingModule(null!));
    }
}
