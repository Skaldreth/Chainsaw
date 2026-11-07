using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>An exit as it is kept in the vault: its description and its configuration, encrypted together.</summary>
/// <param name="Name">The name the user knows the exit by.</param>
/// <param name="Place">The country or city.</param>
/// <param name="Kind">The sort of tunnel.</param>
/// <param name="Configuration">The tunnel's configuration with its private key.</param>
internal sealed record StoredExit(string Name, string Place, ExitKind Kind, string Configuration)
{
    /// <summary>Gives the record without the configuration, so that it can be logged or shown safely.</summary>
    /// <returns>The description with the configuration masked.</returns>
    public override string ToString() => $"StoredExit {{ Name = {Name}, Place = {Place}, Kind = {Kind} }}";
}
