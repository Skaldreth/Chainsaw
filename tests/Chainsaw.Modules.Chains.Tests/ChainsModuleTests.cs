using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
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

    [Fact]
    public void The_module_registers_its_source_and_its_tables()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExitHttp>(new FakeExitHttp(new FakeClock()));
        services.AddSingleton<IOperatorProfileStore>(new FakeProfiles());
        services.AddChainsModule();

        using var provider = services.BuildServiceProvider();

        Assert.Equal(BitcoinNetwork.Mainnet, provider.GetRequiredService<IBitcoinSource>().Network);
        var schema = Assert.Single(provider.GetServices<ICaseSchemaContributor>());
        Assert.Equal("chains", schema.Module);
        Assert.Equal("chains.raw_responses", Assert.Single(schema.SealedChains).Name);
    }

    [Theory]
    [InlineData("http://mempool.space/api/")]
    [InlineData("https://user:pass@mempool.space/api/")]
    [InlineData("https://mempool.space/api/?case=1")]
    public void The_base_address_must_be_plain_https(string uri)
    {
        var options = new BitcoinSourceOptions { EsploraBaseUri = new Uri(uri) };

        Assert.Throws<ArgumentException>(() => new EsploraBitcoinSource(
            new FakeExitHttp(new FakeClock()), new FakeProfiles(), new FakeClock(), options, TimeZoneInfo.Utc));
    }

    [Fact]
    public void The_defaults_are_mempool_space_and_six_confirmations()
    {
        var options = new BitcoinSourceOptions();

        Assert.Equal(new Uri("https://mempool.space/api/"), options.EsploraBaseUri);
        Assert.Equal(new Uri("https://blockstream.info/api/"), BitcoinSourceOptions.Blockstream);
        Assert.Equal(6, options.SafeConfirmations);
    }
}
