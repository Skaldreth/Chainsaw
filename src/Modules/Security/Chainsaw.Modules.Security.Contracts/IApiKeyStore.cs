namespace Chainsaw.Modules.Security.Contracts;

/// <summary>
/// The API keys in the vault. Every member needs an unlocked vault and throws a <see cref="VaultException"/> with
/// <see cref="VaultError.Locked"/> otherwise. Changes are written to the vault file before they return.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>Lists the stored keys without their secrets, ordered by service.</summary>
    /// <returns>The services and identifiers.</returns>
    IReadOnlyList<ApiKeyInfo> List();

    /// <summary>Finds the key for a service.</summary>
    /// <param name="service">The service, compared without regard to case.</param>
    /// <returns>The key, or <see langword="null"/> when none is stored for the service.</returns>
    ApiKey? Find(string service);

    /// <summary>Stores a key, replacing any key already stored for the same service.</summary>
    /// <param name="key">The key; the service and the secret must not be empty.</param>
    void Save(ApiKey key);

    /// <summary>Removes the key for a service.</summary>
    /// <param name="service">The service, compared without regard to case.</param>
    /// <returns><see langword="true"/> when a key was removed.</returns>
    bool Remove(string service);
}
