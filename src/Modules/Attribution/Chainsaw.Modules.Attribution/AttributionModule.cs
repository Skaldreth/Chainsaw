using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// Registers the module Attribution: labels with their origin (own, sanction lists, open sources, behaviour and
/// other cases), the imported sanction lists, the label library that spreads labels between cases, and the
/// recogniser that ends the trail where a label says the money reaches a service, and the suppliers of attribution
/// (Chainabuse first) that are asked about an address.
/// </summary>
/// <remarks>
/// The module needs <c>IClusteringService</c> (Clustering), <c>IPurposeKeys</c> (Security) and an
/// <see cref="IClock"/>; the cases come from the Cases module. The suppliers also need <c>IExitHttp</c> (Network)
/// and <c>IApiKeyStore</c> (Security), asked for only when a supplier is first used.
/// </remarks>
public static class AttributionModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">
    /// Changes the options, for example the data folder; by default <c>%LOCALAPPDATA%\Chainsaw</c>.
    /// </param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAttributionModule(
        this IServiceCollection services, Action<AttributionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new AttributionOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton(options);
        services.AddSingleton<ICaseSchemaContributor, AttributionSchema>();
        services.AddSingleton<ISanctionLists, SanctionLists>();
        services.AddSingleton<IAttributionService, AttributionService>();
        services.AddSingleton<ILabelLibrary, LabelLibrary>();
        services.AddSingleton<ITrailEndClassifier, LabelTrailEnds>();
        services.AddSingleton<IAttributionProvider>(_ => new ChainabuseProvider(options.ChainabuseUri));
        services.AddSingleton<IAttributionProviders, AttributionProviders>();
        return services;
    }
}
