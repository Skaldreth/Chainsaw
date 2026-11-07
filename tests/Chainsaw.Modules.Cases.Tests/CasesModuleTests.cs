using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
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

    [Fact]
    public void The_store_and_the_verifier_resolve_with_the_folder_from_the_options()
    {
        using var fixture = new CaseFixture();
        var services = new ServiceCollection()
            .AddSingleton<IVault>(fixture.Vault)
            .AddSingleton<IPurposeKeys>(fixture.PurposeKeys)
            .AddSingleton<IOperatorProfileStore>(fixture.Profiles)
            .AddSingleton<IClock>(fixture.Clock)
            .AddCasesModule(options => options.Directory = fixture.Directory);
        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ICaseStore>();
        using var opened = store.Create("A1");

        Assert.Equal(fixture.Directory, store.Directory);
        Assert.True(provider.GetRequiredService<ICaseVerifier>().Verify(opened).IsIntact);
    }
}
