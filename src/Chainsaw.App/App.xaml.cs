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
using Chainsaw.App.Controls;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chainsaw.App;

/// <summary>
/// The application: builds the host with every module and shows the main window, which starts with the first-run
/// setup when there is no vault (sketch FS1a) and with the lock screen otherwise (sketch ID7).
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

    /// <summary>
    /// Registers the clock, every module, the settings, the gate, the shell and the main window in one container.
    /// </summary>
    /// <returns>The host, not yet started.</returns>
    private IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                    .AddSingleton<IClock, SystemClock>()
                    .AddSingleton<IUiThread>(new WpfUiThread(Dispatcher))
                    .AddSingleton(new AppSettingsStore().Load())
                    .AddSingleton(BuildInfo.From(typeof(App).Assembly))
                    .AddSingleton(provider => new AutoLock(
                        provider.GetRequiredService<IClock>(),
                        provider.GetRequiredService<AppSettings>().AutoLockTimeout))
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
                    .AddSingleton<CaseSession>()
                    .AddSingleton<ShellViewModel>()
                    .AddSingleton<LockViewModel>()
                    .AddSingleton<FirstRunViewModel>()
                    .AddSingleton<GateViewModel>()
                .AddSingleton<MainWindow>())
            .Build();
}
