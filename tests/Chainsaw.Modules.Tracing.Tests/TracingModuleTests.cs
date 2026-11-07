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
    public void AddTracingModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => TracingModule.AddTracingModule(null!));
    }
}
