using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>Named secrets in the vault, kept in the encrypted content next to the API keys.</summary>
/// <param name="vault">The vault that holds the secrets.</param>
internal sealed class VaultSecrets(Vault vault) : IVaultSecrets
{
    /// <inheritdoc />
    public IReadOnlyList<string> List(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return vault.Read(content => content.SecretsOrEmpty.Keys
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList());
    }

    /// <inheritdoc />
    public string? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return vault.Read(content => content.SecretsOrEmpty.GetValueOrDefault(name));
    }

    /// <inheritdoc />
    public void Save(string name, string value)
    {
        RequireValidName(name);
        if (string.IsNullOrEmpty(value) || value.Length > IVaultSecrets.MaximumValueLength)
        {
            // The message names the rule only; the value is never part of an exception.
            throw new ArgumentException(
                $"The secret must not be empty or longer than {IVaultSecrets.MaximumValueLength} characters.",
                nameof(value));
        }

        vault.Update(content =>
        {
            var secrets = new Dictionary<string, string>(content.SecretsOrEmpty, StringComparer.Ordinal)
            {
                [name] = value,
            };
            return content with { Secrets = secrets };
        });
    }

    /// <inheritdoc />
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var removed = false;
        vault.Update(content =>
        {
            removed = content.SecretsOrEmpty.ContainsKey(name);
            if (!removed)
            {
                return content;
            }

            var secrets = new Dictionary<string, string>(content.SecretsOrEmpty, StringComparer.Ordinal);
            secrets.Remove(name);
            return content with { Secrets = secrets };
        });
        return removed;
    }

    /// <summary>Refuses a name outside the rule, so that names stay stable, readable and free of separators.</summary>
    /// <param name="name">The name to check.</param>
    /// <exception cref="ArgumentException">The name is not allowed.</exception>
    private static void RequireValidName(string? name)
    {
        if (!PurposeKeys.IsValidName(name, IVaultSecrets.MaximumNameLength))
        {
            throw new ArgumentException(
                "The name must be lower-case ASCII letters, digits and / - . _, starting with a letter or digit.",
                nameof(name));
        }
    }
}
