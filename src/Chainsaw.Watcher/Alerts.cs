using Chainsaw.Modules.Monitoring.Contracts;

namespace Chainsaw.Watcher;

/// <summary>Says that a watch has hit, the ways the user chose; never what or where.</summary>
internal interface IAlerts
{
    /// <summary>Says it, once, in each chosen way.</summary>
    /// <param name="ways">The ways chosen in the settings.</param>
    void Raise(WatchAlerts ways);
}

/// <summary>
/// One way of saying that a watch has hit (decided 2026-11-07): a notification in Windows, a sound, or a message in the
/// middle of the screen. Each says only <see cref="BackgroundNames.AlertTitle"/>.
/// </summary>
internal interface IAlertChannel
{
    /// <summary>Gets the way it is.</summary>
    WatchAlerts Way { get; }

    /// <summary>Says it without waiting for it to end; a failure stays inside.</summary>
    void Show();
}

/// <summary>Says it in each chosen way that is built; a way that fails never stops the others or the watching.</summary>
/// <param name="channels">The ways that are built.</param>
internal sealed class Alerts(IEnumerable<IAlertChannel> channels) : IAlerts
{
    /// <summary>The ways.</summary>
    private readonly IReadOnlyList<IAlertChannel> _channels = [.. channels];

    /// <inheritdoc/>
    public void Raise(WatchAlerts ways)
    {
        foreach (var channel in _channels.Where(channel => ways.HasFlag(channel.Way)))
        {
            try
            {
                channel.Show();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One way that fails (no sound card, no taskbar) leaves the others.
            }
        }
    }
}
