using System.Diagnostics;
using Chainsaw.Modules.Monitoring.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Watcher;

/// <summary>
/// Chainsaw's background watching (CLAUDE.md, <em>Bevaka</em>): a small, quiet process that Chainsaw starts and Windows
/// starts at sign-in while a watch is active. It knows only the plan Chainsaw left under DPAPI (the addresses, the
/// filters and the cases' ids), never opens a case or the vault, and ends by itself when there is nothing to watch.
/// </summary>
internal static class Program
{
    /// <summary>Asks a running background process to stop.</summary>
    private const string StopArgument = "--stop";

    /// <summary>Says that a watch has hit, once, the ways chosen, so that the user can hear and see them.</summary>
    private const string TestArgument = "--test-alert";

    /// <summary>Runs the background process.</summary>
    /// <param name="args"><c>--stop</c> or <c>--test-alert</c>, or none.</param>
    /// <returns>0.</returns>
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        if (args.Contains(StopArgument, StringComparer.Ordinal))
        {
            InstanceGuard.AskToStop();
            return 0;
        }

        if (args.Contains(TestArgument, StringComparer.Ordinal))
        {
            // Without the guard: the test works while the background runs, and never keeps it from starting.
            TestAlert();
            return 0;
        }

        using var guard = InstanceGuard.TryTake();
        if (guard is null)
        {
            // Never twice: one is already running for this user.
            return 0;
        }

        using var process = Process.GetCurrentProcess();
        process.PriorityClass = ProcessPriorityClass.BelowNormal;
        using var stop = new CancellationTokenSource();
        guard.OnStop(stop.Cancel);
        while (!stop.IsCancellationRequested)
        {
            using var services = WatcherServices.Build(WatcherServices.AppData);
            var sourcesFile = Path.Combine(WatcherServices.AppData, "sources.json");
            var run = new WatcherRun(services.GetRequiredService<WatcherLoop>(), sourcesFile, Task.Delay);
            if (run.RunAsync(stop.Token).GetAwaiter().GetResult() == RunEnd.Stopped)
            {
                break;
            }
        }

        return 0;
    }

    /// <summary>Says once that a watch has hit, the ways chosen in the settings (Windows and sound when none).</summary>
    private static void TestAlert()
    {
        using var services = WatcherServices.Build(WatcherServices.AppData);
        var plan = services.GetRequiredService<IBackgroundPlan>().Read();
        var ways = plan.Alerts == WatchAlerts.None ? WatchAlerts.Windows | WatchAlerts.Sound : plan.Alerts;
        // The ways show on threads of their own, which the process waits for before it ends.
        services.GetRequiredService<IAlerts>().Raise(ways);
    }
}
