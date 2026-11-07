using System.Net.NetworkInformation;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Follows <see cref="NetworkChange.NetworkAvailabilityChanged"/> and <see cref="NetworkChange.NetworkAddressChanged"/>
/// and reads <see cref="NetworkInterface.GetIsNetworkAvailable()"/>. Windows counts a network as available when any
/// interface other than loopback and tunnels is up, so this says <em>no network</em> only when the computer truly has
/// none, never that the internet answers.
/// </summary>
internal sealed class WindowsNetworkSignals : INetworkSignals, IDisposable
{
    /// <summary>Starts following Windows.</summary>
    public WindowsNetworkSignals()
    {
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool HasNetwork => NetworkInterface.GetIsNetworkAvailable();

    /// <summary>Stops following Windows.</summary>
    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
    }

    /// <summary>Tells that the network came or went.</summary>
    /// <param name="sender">Not used.</param>
    /// <param name="e">Not used: the listener reads <see cref="HasNetwork"/> again.</param>
    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Tells that an address changed.</summary>
    /// <param name="sender">Not used.</param>
    /// <param name="e">Not used.</param>
    private void OnAddressChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
