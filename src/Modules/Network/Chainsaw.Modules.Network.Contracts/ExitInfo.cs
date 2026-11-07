namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// An exit (VPN) that Chainsaw's requests can leave the computer through, without its configuration. Everything here
/// is kept encrypted in the vault together with the configuration.
/// </summary>
/// <param name="Id">The exit's id, given when it is added.</param>
/// <param name="Name">The name the user knows the exit by.</param>
/// <param name="Place">The country or city where the exit leaves the tunnel; empty when unknown.</param>
/// <param name="Kind">The sort of tunnel.</param>
public sealed record ExitInfo(Guid Id, string Name, string Place, ExitKind Kind);
