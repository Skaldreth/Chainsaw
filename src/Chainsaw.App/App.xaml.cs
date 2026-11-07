using System.Windows;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Security;
using Chainsaw.Modules.Network;
using Chainsaw.Modules.Chains;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Attribution;
using Chainsaw.Modules.Tracing;
using Chainsaw.Modules.Pricing;
using Chainsaw.Modules.Monitoring;
using Chainsaw.Modules.Requests;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chainsaw.App;

/// <summary>
/// The application: builds the host with every module and shows the main window. The shell itself is drawn after the
/// sketches SH1a and SH1b are decided; until then the window is empty.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>Builds and starts the host, then shows the main window.</summary>
    /// <param name="e">The arguments Chainsaw was started with, for example a <c>chainsaw:</c> link.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = BuildHost();
        await _host.StartAsync();
        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    /// <summary>Stops the host so that every module can close what it holds.</summary>
    /// <param name="e">The exit arguments.</param>
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>Registers the clock, every module and the main window in one container.</summary>
    /// <returns>The host, not yet started.</returns>
    private static IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                    .AddSingleton<IClock, SystemClock>()
                .AddCasesModule()
                .AddSecurityModule()
                .AddNetworkModule()
                .AddChainsModule()
                .AddClusteringModule()
                .AddAttributionModule()
                .AddTracingModule()
                .AddPricingModule()
                .AddMonitoringModule()
                .AddRequestsModule()
                    .AddSingleton<MainWindow>())
            .Build();
}
