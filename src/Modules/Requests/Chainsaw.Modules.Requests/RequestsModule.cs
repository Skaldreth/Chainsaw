using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// Registers the module Requests: requests for information to exchanges and for freezing to issuers, built from
/// templates, exported as Markdown and sealed in the case (sketch BG1b).
/// </summary>
public static class RequestsModule
{
    /// <summary>
    /// Adds the module's services to the container: <see cref="IRequestService"/>, <see cref="IReportService"/>
    /// and the module's tables in the case.
    /// The container must also hold the clock, the operator's profile store and the case verifier.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddRequestsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(_ => new ExchangeContacts());
        services.TryAddSingleton<IRequestService, RequestService>();
        services.TryAddSingleton<IReportService, ReportService>();
        services.AddSingleton<ICaseSchemaContributor, RequestsSchema>();
        return services;
    }
}
