using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>An exit together with its configuration from the vault.</summary>
/// <param name="Exit">The exit.</param>
/// <param name="Configuration">The tunnel's configuration with its private key.</param>
internal sealed record ExitWithConfiguration(ExitInfo Exit, string Configuration)
{
    /// <summary>Gives the record without the configuration, so that it can be logged or shown safely.</summary>
    /// <returns>The exit with the configuration masked.</returns>
    public override string ToString() => $"ExitWithConfiguration {{ Exit = {Exit}, Configuration = *** }}";
}
