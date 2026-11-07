using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>The API keys in the vault, one per service.</summary>
/// <param name="vault">The vault that holds the keys.</param>
internal sealed class ApiKeyStore(Vault vault) : IApiKeyStore
{
    /// <summary>How services are compared: without regard to case, since <c>Esplora</c> and <c>esplora</c> are one.</summary>
    private static readonly StringComparer ServiceComparer = StringComparer.OrdinalIgnoreCase;

    /// <inheritdoc />
    public IReadOnlyList<ApiKeyInfo> List() =>
        vault.Read(content => content.ApiKeys
            .OrderBy(key => key.Service, ServiceComparer)
            .Select(key => new ApiKeyInfo(key.Service, key.Identifier))
            .ToList());

    /// <inheritdoc />
    public ApiKey? Find(string service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return vault.Read(content => content.ApiKeys.FirstOrDefault(key => ServiceComparer.Equals(key.Service, service)));
    }

    /// <inheritdoc />
    public void Save(ApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Service);
        ArgumentNullException.ThrowIfNull(key.Identifier);
        if (string.IsNullOrEmpty(key.Secret))
        {
            // The message names the field only; the secret is never part of an exception.
            throw new ArgumentException("The API key's secret must not be empty.", nameof(key));
        }

        vault.Update(content => content with { ApiKeys = [.. WithoutService(content, key.Service), key] });
    }

    /// <inheritdoc />
    public bool Remove(string service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var removed = false;
        vault.Update(content =>
        {
            var kept = WithoutService(content, service).ToList();
            removed = kept.Count != content.ApiKeys.Count;
            return removed ? content with { ApiKeys = kept } : content;
        });
        return removed;
    }

    /// <summary>Gives the stored keys except the one for a service.</summary>
    /// <param name="content">The vault content.</param>
    /// <param name="service">The service to leave out.</param>
    /// <returns>The other keys.</returns>
    private static IEnumerable<ApiKey> WithoutService(VaultContent content, string service) =>
        content.ApiKeys.Where(stored => !ServiceComparer.Equals(stored.Service, service));
}
