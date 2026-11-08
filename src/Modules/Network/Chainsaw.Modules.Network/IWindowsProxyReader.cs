using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Reads the proxy Windows is set up with for the current user. The tests replace it, since the cloud machine is not
/// Windows and the gate on the login must be shown with every kind of setting.
/// </summary>
internal interface IWindowsProxyReader
{
    /// <summary>Reads the setting now.</summary>
    /// <returns>The setting; <see cref="WindowsProxySetting.None"/> where it cannot be read.</returns>
    WindowsProxySetting Read();
}
