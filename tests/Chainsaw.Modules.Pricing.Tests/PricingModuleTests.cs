using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
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

    [Fact]
    public void Registers_the_service_the_sources_and_the_tables()
    {
        using var provider = Provider(new ServiceCollection());

        Assert.NotNull(provider.GetRequiredService<IPriceService>());
        Assert.Equal("bitstamp", provider.GetRequiredService<ICryptoRateSource>().Name);
        Assert.Equal("riksbank", provider.GetRequiredService<IFiatRateSource>().Name);
        var schema = Assert.Single(provider.GetServices<ICaseSchemaContributor>());
        Assert.Equal("pricing", schema.Module);
        Assert.Equal("pricing.raw_responses", Assert.Single(schema.SealedChains).Name);
    }

    [Fact]
    public void Uses_the_real_sources_by_default()
    {
        var options = new PricingOptions();

        Assert.Equal(FiatCurrency.Sek, options.Currency);
        Assert.Equal(PriceResolution.Hour, options.Resolution);
        Assert.Equal(FiatCurrency.Usd, options.KronaVia);
        Assert.Equal("https://www.bitstamp.net/api/v2/", options.BitstampBaseUri.AbsoluteUri);
        Assert.Equal("https://api.riksbank.se/swea/v1/", options.RiksbankBaseUri.AbsoluteUri);
    }

    [Fact]
    public void Lets_a_source_registered_first_replace_bitstamp()
    {
        var services = new ServiceCollection();
        var replacement = new BitstampRateSource(new Uri("https://other.test/"));
        services.AddSingleton<ICryptoRateSource>(replacement);

        using var provider = Provider(services);

        Assert.Same(replacement, provider.GetRequiredService<ICryptoRateSource>());
    }

    [Theory]
    [InlineData("http://www.bitstamp.net/api/v2/", "https://api.riksbank.se/swea/v1/")]
    [InlineData("https://www.bitstamp.net/api/v2/", "http://api.riksbank.se/swea/v1/")]
    public void Refuses_a_base_address_that_is_not_https(string bitstamp, string riksbank)
    {
        var options = new PricingOptions { BitstampBaseUri = new Uri(bitstamp), RiksbankBaseUri = new Uri(riksbank) };

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPricingModule(options));
    }

    private static ServiceProvider Provider(ServiceCollection services)
    {
        var clock = new FakeClock();
        services.AddSingleton<IExitHttp>(new FakeExitHttp(clock));
        services.AddSingleton<IOperatorProfileStore>(new FakeProfiles());
        services.AddSingleton<IClock>(clock);
        services.AddPricingModule();
        return services.BuildServiceProvider();
    }
}
