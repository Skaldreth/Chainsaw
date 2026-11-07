using System.Runtime.Versioning;
using Chainsaw.Modules.Monitoring.Contracts;

namespace Chainsaw.Watcher;

/// <summary>
/// Makes sure the background process runs once per Windows user (a named mutex in the session), and lets Chainsaw ask
/// it to stop (a named event): <c>Chainsaw.Watcher.exe --stop</c>, or the settings' switch.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class InstanceGuard : IDisposable
{
    /// <summary>The mutex held while it runs.</summary>
    private readonly Mutex _mutex;

    /// <summary>The event that asks it to stop.</summary>
    private readonly EventWaitHandle _stop;

    /// <summary>The wait registered on the event.</summary>
    private RegisteredWaitHandle? _waiting;

    /// <summary>Creates the guard over a mutex it holds.</summary>
    /// <param name="mutex">The mutex.</param>
    private InstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
        _stop = new EventWaitHandle(false, EventResetMode.AutoReset, BackgroundNames.StopEvent);
    }

    /// <summary>Takes the mutex, when no other background process holds it.</summary>
    /// <returns>The guard, or <see langword="null"/> when one already runs.</returns>
    public static InstanceGuard? TryTake()
    {
        var mutex = new Mutex(true, BackgroundNames.Mutex, out var first);
        if (first)
        {
            return new InstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Asks a running background process to stop.</summary>
    public static void AskToStop()
    {
        if (EventWaitHandle.TryOpenExisting(BackgroundNames.StopEvent, out var stop))
        {
            using (stop)
            {
                stop.Set();
            }
        }
    }

    /// <summary>Calls an action when it is asked to stop.</summary>
    /// <param name="stop">The action.</param>
    public void OnStop(Action stop) =>
        _waiting = ThreadPool.RegisterWaitForSingleObject(_stop, (_, _) => stop(), null, Timeout.Infinite, true);

    /// <inheritdoc/>
    public void Dispose()
    {
        _waiting?.Unregister(null);
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
