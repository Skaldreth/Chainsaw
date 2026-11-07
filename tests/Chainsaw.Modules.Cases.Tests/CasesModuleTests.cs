using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Cases.Tests;

public sealed class CasesModuleTests
{
    [Fact]
    public void AddCasesModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddCasesModule());
    }

    [Fact]
    public void AddCasesModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => CasesModule.AddCasesModule(null!));
    }
}
