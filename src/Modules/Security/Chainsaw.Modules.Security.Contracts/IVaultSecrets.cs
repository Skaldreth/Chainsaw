namespace Chainsaw.Modules.Security.Contracts;

/// <summary>
/// Named secrets in the vault for other modules, for example the configuration of an exit (VPN). The name and the
/// value are both encrypted with the rest of the vault; nothing about a secret is written in plain text. Every member
/// needs an unlocked vault and throws a <see cref="VaultException"/> with <see cref="VaultError.Locked"/> otherwise.
/// Changes are written to the vault file before they return.
/// </summary>
/// <remarks>
/// A module keeps its secrets under its own prefix, for example <c>network/</c>, so that two modules never share a
/// name. API keys for the sources have their own store, <see cref="IApiKeyStore"/>.
/// </remarks>
public interface IVaultSecrets
{
    /// <summary>The longest name that is accepted.</summary>
    const int MaximumNameLength = 128;

    /// <summary>The longest value that is accepted, in characters (256 KiB), so that the vault stays small.</summary>
    const int MaximumValueLength = 256 * 1024;

    /// <summary>Lists the names of the stored secrets that start with a prefix, in ordinal order.</summary>
    /// <param name="prefix">The prefix, for example <c>network/exits/</c>; empty lists every name.</param>
    /// <returns>The names, without the values.</returns>
    IReadOnlyList<string> List(string prefix);

    /// <summary>Finds a secret.</summary>
    /// <param name="name">The secret's name, compared ordinally.</param>
    /// <returns>The value, or <see langword="null"/> when no secret has the name.</returns>
    string? Find(string name);

    /// <summary>Stores a secret, replacing any secret with the same name.</summary>
    /// <param name="name">
    /// The name: lower-case ASCII letters, digits and <c>/ - . _</c>, starting with a letter or digit, at most
    /// <see cref="MaximumNameLength"/> characters.
    /// </param>
    /// <param name="value">The value; not empty and at most <see cref="MaximumValueLength"/> characters.</param>
    /// <exception cref="ArgumentException">The name or the value is not allowed.</exception>
    void Save(string name, string value);

    /// <summary>Removes a secret.</summary>
    /// <param name="name">The secret's name.</param>
    /// <returns><see langword="true"/> when a secret was removed.</returns>
    bool Remove(string name);
}
