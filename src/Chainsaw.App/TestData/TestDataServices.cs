#if DEBUG
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Chains.TestData;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Pricing.TestData;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Chainsaw.SharedKernel.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.App.TestData;

/// <summary>
/// Registers the test case, only in a Debug build: <c>/testdata</c>, the made-up chain and its source, and the
/// routing that keeps the made-up source to test cases and test cases away from the real sources and the background.
/// </summary>
public static class TestDataServices
{
    /// <summary>
    /// Adds the test case's services. Call it after the modules are added, since it wraps their Bitcoin source, watch
    /// gate and background watches.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The same container.</returns>
    /// <exception cref="InvalidOperationException">A module it wraps is not registered.</exception>
    public static IServiceCollection AddTestData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<TestActors>();
        services.AddSingleton(provider => TestStoryChain.Build(provider.GetRequiredService<TestActors>()));
        services.AddSingleton(provider => new TestChainHttp(
            provider.GetRequiredService<TestChain>(), provider.GetRequiredService<IClock>()));
        Decorate<IBitcoinSource>(services, (provider, real) => new TestDataBitcoinRouter(
            real,
            new Lazy<IBitcoinSource>(() => TestDataBitcoin.Create(
                provider.GetRequiredService<TestChainHttp>(),
                provider.GetRequiredService<IOperatorProfileStore>(),
                provider.GetRequiredService<IClock>()))));
        services.AddSingleton(provider => new TestRatesHttp(provider.GetRequiredService<IClock>()));
        TryDecorate<IPriceService>(services, (provider, real) => new TestDataPriceRouter(
            real,
            new Lazy<IPriceService>(() => TestDataPricing.Create(
                provider.GetRequiredService<TestRatesHttp>(),
                provider.GetRequiredService<IOperatorProfileStore>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<IPriceDefaults>()))));
        Decorate<ISanctionLists>(services, (provider, real) => new TestDataSanctionLists(
            real, provider.GetRequiredService<TestActors>()));
        Decorate<IWatchGate>(services, (_, real) => new TestDataWatchGate(real));
        Decorate<IBackgroundWatches>(services, (_, real) => new TestDataBackgroundWatches(real));
        services.AddSingleton(provider => new TestServices(
            provider.GetRequiredService<ICaseStore>(),
            provider.GetRequiredService<ITracingService>(),
            provider.GetRequiredService<IAttributionService>(),
            provider.GetService<ILabelLibrary>(),
            provider.GetRequiredService<IMonitoringService>(),
            provider.GetRequiredService<ICheckService>())
        {
            Comments = provider.GetService<IGraphCommentService>(),
            Trash = provider.GetService<IGraphTrash>(),
            Clustering = provider.GetService<IClusteringService>(),
            Bitcoin = provider.GetRequiredService<IBitcoinSource>(),
        });
        services.AddSingleton<TestCaseBuilder>();
        services.AddSingleton<IShellCommand, TestDataCommand>();
        return services;
    }

    /// <summary>
    /// Replaces the last registration of a service with one that wraps it, when the service is registered; a part of
    /// the program that is not there (the rates in a test without them) is left out.
    /// </summary>
    /// <typeparam name="T">The service.</typeparam>
    /// <param name="services">The container.</param>
    /// <param name="wrap">Wraps the registered service.</param>
    private static void TryDecorate<T>(IServiceCollection services, Func<IServiceProvider, T, T> wrap)
        where T : class
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(T)))
        {
            Decorate(services, wrap);
        }
    }

    /// <summary>Replaces the last registration of a service with one that wraps it.</summary>
    /// <typeparam name="T">The service.</typeparam>
    /// <param name="services">The container.</param>
    /// <param name="wrap">Wraps the registered service.</param>
    /// <exception cref="InvalidOperationException">The service is not registered.</exception>
    private static void Decorate<T>(IServiceCollection services, Func<IServiceProvider, T, T> wrap)
        where T : class
    {
        var registered = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(T))
            ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered.");
        services.Remove(registered);
        services.AddSingleton(provider => wrap(provider, Create<T>(provider, registered)));
    }

    /// <summary>Makes the service a registration describes.</summary>
    /// <typeparam name="T">The service.</typeparam>
    /// <param name="provider">The container.</param>
    /// <param name="registered">The registration.</param>
    /// <returns>The service.</returns>
    private static T Create<T>(IServiceProvider provider, ServiceDescriptor registered)
        where T : class
    {
        if (registered.ImplementationInstance is T instance)
        {
            return instance;
        }

        if (registered.ImplementationFactory is { } factory)
        {
            return (T)factory(provider);
        }

        return (T)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!);
    }
}
#endif
