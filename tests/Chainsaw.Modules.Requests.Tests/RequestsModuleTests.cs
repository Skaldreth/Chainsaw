using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Requests.Tests;

public sealed class RequestsModuleTests
{
    [Fact]
    public void AddRequestsModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddRequestsModule());
    }

    [Fact]
    public void AddRequestsModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => RequestsModule.AddRequestsModule(null!));
    }
}
