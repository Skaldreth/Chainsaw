using System.Net.NetworkInformation;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Follows whether the computer has a network, from <see cref="NetworkChange.NetworkAvailabilityChanged"/>, and reads
/// it at the start with <see cref="NetworkInterface.GetIsNetworkAvailable()"/>. Windows counts a network as available
/// when any interface other than loopback and tunnels is up, so this says <em>no network</em> only when the computer
/// truly has none, never that the internet answers.
/// </summary>
internal sealed class NetworkPresence : INetworkPresence, IDisposable
{
    /// <summary>Guards the value, so that a change and a read never cross.</summary>
    private readonly Lock _gate = new();

    /// <summary>Whether the computer has a network, as last told.</summary>
    private bool _available;

    /// <summary>Whether the module has been disposed, after which no change is followed.</summary>
    private bool _disposed;

    /// <summary>Creates the presence and starts following Windows.</summary>
    public NetworkPresence()
    {
        // Followed first and read after, so that a change in between is never lost.
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        _available = NetworkInterface.GetIsNetworkAvailable();
    }

    /// <inheritdoc />
    public event EventHandler<bool>? Changed;

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _available;
            }
        }
    }

    /// <summary>Stops following Windows.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
    }

    /// <summary>Keeps the new value and tells it, once per real change.</summary>
    /// <param name="sender">Not used.</param>
    /// <param name="e">Whether the computer has a network now.</param>
    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => Set(e.IsAvailable);

    /// <summary>Keeps a value and tells it when it differs from the last.</summary>
    /// <param name="available">Whether the computer has a network now.</param>
    internal void Set(bool available)
    {
        lock (_gate)
        {
            if (_disposed || _available == available)
            {
                return;
            }

            _available = available;
        }

        foreach (var handler in Changed?.GetInvocationList().Cast<EventHandler<bool>>() ?? [])
        {
            try
            {
                handler(this, available);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One listener's failure must not keep the change from the others.
            }
        }
    }
}
