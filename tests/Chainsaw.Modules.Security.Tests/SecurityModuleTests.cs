using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Security.Tests;

public sealed class SecurityModuleTests
{
    [Fact]
    public void AddSecurityModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddSecurityModule());
    }

    [Fact]
    public void AddSecurityModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => SecurityModule.AddSecurityModule(null!));
    }
}
