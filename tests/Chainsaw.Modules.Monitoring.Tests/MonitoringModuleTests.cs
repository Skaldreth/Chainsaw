using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Monitoring.Tests;

public sealed class MonitoringModuleTests
{
    [Fact]
    public void AddMonitoringModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddMonitoringModule());
    }

    [Fact]
    public void AddMonitoringModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => MonitoringModule.AddMonitoringModule(null!));
    }
}
