using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// Registers the module Pricing: the price of a chain's currency at a time in the chosen currency, from Bitstamp's
/// price history and the Riksbank's exchange rates, kept in the case with their source.
/// </summary>
/// <remarks>
/// The module needs the Network module's <c>IExitHttp</c> (its only way out) and the Security module's
/// <c>IOperatorProfileStore</c>, and adds its tables to every case through an <see cref="ICaseSchemaContributor"/>.
/// A source registered before the module takes the place of Bitstamp or the Riksbank (principle 8).
/// </remarks>
public static class PricingModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="options">The settings; by default kronor, an hour, and the crypto rate in dollars.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">The interval between requests to the Riksbank is negative.</exception>
    /// <remarks>
    /// A default source's base address is checked when the source is first used, so that a source registered in its
    /// place is never held up by it.
    /// </remarks>
    public static IServiceCollection AddPricingModule(
        this IServiceCollection services, PricingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var settings = options ?? new PricingOptions();
        if (settings.RiksbankInterval < TimeSpan.Zero)
        {
            throw new ArgumentException("The interval between requests cannot be negative.", nameof(options));
        }

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICaseSchemaContributor, PricingSchema>();
        services.TryAddSingleton<ICryptoRateSource>(_ => new BitstampRateSource(settings.BitstampBaseUri));
        services.TryAddSingleton<IFiatRateSource>(_ => new RiksbankRateSource(settings.RiksbankBaseUri));
        services.AddSingleton(_ => new PriceDefaults(settings));
        services.AddSingleton<IPriceDefaults>(provider => provider.GetRequiredService<PriceDefaults>());
        services.AddSingleton<IPriceService>(provider => new PriceService(
            provider.GetRequiredService<ICryptoRateSource>(),
            provider.GetRequiredService<IFiatRateSource>(),
            provider.GetRequiredService<IExitHttp>(),
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            settings,
            provider.GetRequiredService<PriceDefaults>(),
            TimeZoneInfo.Local));
        return services;
    }
}
