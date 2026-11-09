using System.Windows;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Security;
using Chainsaw.Modules.Network;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Chains;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Attribution;
using Chainsaw.Modules.Tracing;
using Chainsaw.Modules.Checks;
using Chainsaw.Modules.Pricing;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Monitoring;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Requests;
using Chainsaw.App.Commands;
using Chainsaw.App.Controls;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.Interop;
using Chainsaw.App.Links;
using Chainsaw.App.Navigation;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.SharedKernel;
using Chainsaw.SharedKernel.Commands;
#if DEBUG
using Chainsaw.App.TestData;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chainsaw.App;

/// <summary>
/// The application: one per Windows user, builds the host with every module and shows the main window, which starts
/// with the first-run setup when there is no vault (sketch FS1a) and with the lock screen otherwise (sketch ID7).
/// </summary>
public partial class App : Application
{
    /// <summary>How long the exit waits for the modules to stop, after the case has been saved.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long the exit waits for an opening or a creation under way to end, before the case is saved.</summary>
    private static readonly TimeSpan RunningWorkTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The host with every module, once built.</summary>
    private IHost? _host;

    /// <summary>The hold on being the one Chainsaw of this user, and the pipe that links arrive on.</summary>
    private SingleInstance? _instance;

    /// <summary>
    /// Becomes the one Chainsaw of this user, or hands the link to the one that runs and ends. Then builds and starts
    /// the host, shows the main window and takes the link Chainsaw was started with, which waits for the unlock; links
    /// that came over the pipe while the host was built are held and handed over after it.
    /// </summary>
    /// <param name="e">The arguments Chainsaw was started with, for example a <c>chainsaw:</c> link.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        // First of all, so that no crash from here on leaves a dump for Windows Error Reporting.
        ErrorReporting.Exclude();
        base.OnStartup(e);
        CrashGuard.Guard(this);
        CaseClipboard.Guard();
        if (AlertCard.FromArguments(e.Args, SingleInstance.IsRunning(), DateTimeOffset.Now) is { } card)
        {
            // Started by the background process as only the message in the middle of the screen (sketch BV4a): no
            // host, no vault and no hold on being the one Chainsaw; it ends when the card is closed.
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            new Views.AlertCardWindow(card).Show();
            return;
        }

        var argument = e.Args.Length > 0 ? e.Args[0] : string.Empty;
        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            SingleInstance.Send(argument);
            Shutdown();
            return;
        }

        _host = BuildHost();
        await _host.StartAsync();
        WithdrawFromUnsafeFolder(_host.Services);
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        // The alarm follows the open case from here on and says when it cannot be saved.
        _ = _host.Services.GetRequiredService<SaveAlarm>();
        var links = _host.Services.GetRequiredService<LinkInbox>();
        links.Receive(argument);
        _instance.Listen(text => Dispatcher.BeginInvoke(() =>
        {
            links.Receive(text);
            window.BringForward();
        }));
        window.Show();
    }

    /// <summary>
    /// Takes away the <c>Run</c> key and the link <c>chainsaw:</c> when others can change the program folder (decision
    /// 16), so that Windows never starts Chainsaw's programs from it on its own; the settings say why.
    /// </summary>
    /// <param name="services">The host's services.</param>
    private static void WithdrawFromUnsafeFolder(IServiceProvider services)
    {
        if (services.GetRequiredService<ProgramFolderVerdict>().IsSafe)
        {
            return;
        }

        try
        {
            services.GetRequiredService<LinkRegistration>().Withdraw();
            services.GetRequiredService<IAutostart>().Set(null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            // The registry refused; the settings still say that Chainsaw does not start from this folder.
        }
    }

    /// <summary>
    /// Saves and closes the open case first and waits for it, so that Chainsaw never ends before the case is saved;
    /// then stops the host so that every module can close what it holds.
    /// </summary>
    /// <param name="e">The exit arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                WaitOnDispatcher(_host.Services.GetRequiredService<CaseWorkflow>().Running, RunningWorkTimeout);
                _host.Services.GetRequiredService<CaseSession>().CloseNow();
                StopModules(_host);
            }
        }
        finally
        {
            // Disposing the host disposes the case store, which closes every case still open, also one that a lock
            // or a creation left closing in the background.
            try
            {
                _host?.Dispose();
            }
            finally
            {
                _instance?.Dispose();
                base.OnExit(e);
            }
        }
    }

    /// <summary>
    /// Waits for work under way (an opening or a creation of a case) while the UI thread goes on serving what the work
    /// posts to it, for at most a limit; a case it makes is then open and is saved and closed like any other. A
    /// dispatcher that has already begun to shut down is not waited on.
    /// </summary>
    /// <param name="work">The work.</param>
    /// <param name="limit">The longest wait.</param>
    private void WaitOnDispatcher(Task work, TimeSpan limit)
    {
        if (work.IsCompleted || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        var frame = new System.Windows.Threading.DispatcherFrame();
        using var timer = new System.Threading.Timer(
            _ => frame.Continue = false, null, limit, Timeout.InfiniteTimeSpan);
        _ = work.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        try
        {
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher cannot run a frame now; the case is saved as it stands.
        }
    }

    /// <summary>
    /// Stops the modules on the thread pool, since a handler that posts to this thread must never wait for it; a module
    /// that fails to stop does not keep the others, or the host's disposal, from running.
    /// </summary>
    /// <param name="host">The host.</param>
    private static void StopModules(IHost host)
    {
        try
        {
            Task.Run(() => host.StopAsync()).Wait(StopTimeout);
        }
        catch (AggregateException)
        {
            // A module failed to stop; the host's disposal still closes what is held.
        }
    }

    /// <summary>
    /// Registers the clock, every module, the settings, the gate, the shell and the main window in one container. The
    /// settings' file is read once, here, so that the way out, the currency and the lock hold from the start.
    /// </summary>
    /// <returns>The host, not yet started.</returns>
    private IHost BuildHost()
    {
        var store = new AppSettingsStore();
        var settings = store.Load();
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                .AddSingleton<IClock, SystemClock>()
                .AddSingleton<IUiThread>(new WpfUiThread(Dispatcher))
                .AddSingleton(store)
                .AddSingleton(settings)
                .AddSingleton(new AttributionSwitches(settings.AttributionProviders))
                .AddSingleton(BuildInfo.From(typeof(App).Assembly))
                .AddSingleton(provider => StartAutoLock(provider.GetRequiredService<IClock>(), settings))
                .AddCasesModule()
                .AddSecurityModule()
                .AddNetworkModule(options =>
                {
                    options.Route = settings.ExitRoute;
                    options.BackgroundExitsFile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw",
                        BackgroundNames.Folder, NetworkOptions.BackgroundExitsFileName);
                    options.ProxyLoginFor = settings.ProxyLoginFor;
                })
                .AddChainsModule(sources: options => options.Directory = Path.GetDirectoryName(store.FilePath))
                .AddClusteringModule()
                .AddAttributionModule()
                .AddTracingModule()
                .AddChecksModule()
                .AddPricingModule(new PricingOptions
                {
                    Currency = settings.Currency,
                    Resolution = settings.Resolution,
                    StateDirectory = Path.GetDirectoryName(store.FilePath),
                })
                .AddMonitoringModule()
                .AddBackgroundWatchFiles()
                .AddRequestsModule()
                .AddSingleton<CaseSession>()
                .AddSingleton<CaseWorkflow>()
                .AddSingleton<LockSignal>()
                .AddSingleton<IStartPointPrefetch, StartPointPrefetch>()
                .AddSingleton<NewCaseViewModel>()
                .AddSingleton<StartPointInput>()
                .AddSingleton<IShellCommand, NewCaseCommand>()
                .AddSingleton<IShellCommand, OpenCaseCommand>()
                .AddSingleton<IShellCommand, CloseCaseCommand>()
                .AddSingleton<IShellCommand, VerifyCommand>()
                .AddSingleton<IShellCommand, LockCommand>()
                .AddSingleton<IShellCommand, GuideCommand>()
                .AddSingleton<CommandRegistry>()
                .AddSingleton<WpfFilePicker>()
                .AddSingleton<IFilePicker>(provider => provider.GetRequiredService<WpfFilePicker>())
                .AddSingleton<ISaveFilePicker>(provider => provider.GetRequiredService<WpfFilePicker>())
                .AddSingleton<IFolderView, WpfFolderView>()
                .AddSingleton<ICaseFilePicker>(provider => provider.GetRequiredService<WpfFilePicker>())
                .AddSingleton<SanctionListImport>()
                .AddSingleton<ExitWay>()
                .AddSingleton<ExitPanelViewModel>()
                .AddSingleton<CasePickerViewModel>()
                .AddSingleton<CasePasswordViewModel>()
                .AddSingleton<CasePropertiesViewModel>()
                .AddSingleton<CaseBranchViewModel>()
                .AddSingleton<CaseDialogsViewModel>()
                .AddSingleton<SourcesViewModel>()
                .AddSingleton(_ => ProgramFolderCheck.Check(AppContext.BaseDirectory))
                .AddSingleton<ILinkRegistry, CurrentUserLinkRegistry>()
                .AddSingleton(provider => new LinkRegistration(
                    provider.GetRequiredService<ILinkRegistry>(),
                    Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Chainsaw.exe"),
                    provider.GetRequiredService<ProgramFolderVerdict>().IsSafe))
                .AddSingleton<AboutViewModel>()
                .AddSingleton<IAlertPreview>(provider => new WpfAlertPreview(
                    () => provider.GetRequiredService<ShellViewModel>().Show(AppView.Monitoring)))
                .AddSingleton<RatesSummaryViewModel>()
                .AddSingleton(provider => new ProxyLoginViewModel(
                    provider.GetRequiredService<IWindowsProxy>(),
                    provider.GetRequiredService<INetworkPresence>(),
                    provider.GetRequiredService<IUiThread>(),
                    $"{Environment.UserDomainName}\\{Environment.UserName}"))
                .AddSingleton<SettingsViewModel>()
                .AddSingleton<IGraphSource, GraphSource>()
                .AddSingleton<IGraphChainState, GraphChainState>()
                .AddSingleton<IGraphEditor, GraphEditor>()
                .AddSingleton<ICheckSource, CheckSource>()
                .AddSingleton<IGraphComments, GraphComments>()
                .AddSingleton<Toaster>()
                .AddSingleton<SaveAlarm>()
                .AddSingleton<ITrashSource, TrashSource>()
                .AddSingleton<CaseTrash>()
                .AddSingleton<GraphViewModel>()
                .AddSingleton<IWatchSource, WatchSource>()
                .AddSingleton<WatchBoard>()
                .AddSingleton<WatchFormViewModel>()
                .AddSingleton<MonitoringViewModel>()
                .AddSingleton<RequestsViewModel>()
                .AddSingleton<LabelsViewModel>()
                .AddSingleton<ExposureViewModel>()
                .AddSingleton<TimelineViewModel>()
                .AddSingleton<FlowViewModel>()
                .AddSingleton<ReportViewModel>()
                .AddSingleton<IBackgroundProcess, WindowsBackgroundProcess>()
                .AddSingleton<IAutostart>(provider =>
                    new RunKeyAutostart(provider.GetRequiredService<ProgramFolderVerdict>().IsSafe))
                .AddSingleton(provider => new BackgroundParts(
                    provider.GetRequiredService<IBackgroundWatches>(),
                    provider.GetRequiredService<IBackgroundPlan>(),
                    provider.GetRequiredService<ICaseHolds>(),
                    provider.GetRequiredService<IWatchQueue>(),
                    provider.GetRequiredService<IBackgroundStatusFile>(),
                    provider.GetRequiredService<IBackgroundProcess>(),
                    provider.GetRequiredService<IAutostart>(),
                    provider.GetRequiredService<IClock>())
                {
                    Exits = provider.GetRequiredService<IBackgroundExits>(),
                    Progress = provider.GetRequiredService<IBackgroundProgressFile>(),
                })
                .AddSingleton(provider => new BackgroundLink(
                    provider.GetRequiredService<BackgroundParts>(),
                    provider.GetRequiredService<CaseSession>(),
                    provider.GetRequiredService<WatchBoard>(),
                    provider.GetRequiredService<IUiThread>(),
                    settings))
                .AddSingleton(provider => new WatchRunner(
                    provider.GetRequiredService<WatchBoard>(),
                    provider.GetRequiredService<IClock>(),
                    import: provider.GetRequiredService<BackgroundLink>().ImportAsync,
                    news: provider.GetRequiredService<IBlockNews>()))
                .AddSingleton(_ => GuideLibrary.Load())
                .AddSingleton<GuidePanelViewModel>()
                .AddSingleton<CommandSearchViewModel>()
                .AddSingleton<GuideViewModel>()
                .AddSingleton<ShellViewModel>()
                .AddSingleton<LinkInbox>()
                .AddSingleton<LockViewModel>()
                .AddSingleton<FirstRunViewModel>()
                .AddSingleton<GateViewModel>()
                .AddSingleton<MainWindow>()
#if DEBUG
                // /testdata and its made-up source exist only in a Debug build; they wrap the modules' services, so
                // they come last.
                .AddTestData()
#endif
                )
            .Build();
    }

    /// <summary>Creates the automatic lock with the time and the lock with Windows from the settings.</summary>
    /// <param name="clock">The clock.</param>
    /// <param name="settings">The settings read at the start.</param>
    /// <returns>The automatic lock.</returns>
    private static AutoLock StartAutoLock(IClock clock, AppSettings settings)
    {
        var autoLock = new AutoLock(clock, settings.AutoLockTimeout);
        autoLock.LockWithWindows(settings.LockWithWindows);
        return autoLock;
    }
}
