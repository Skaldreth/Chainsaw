using System.Reflection;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The organisation's public recovery key built into this assembly, from the resource
/// <c>organization-recovery-key.pem</c>. The file holds an explanation and, once the administrator has made a key
/// pair, the public key; without the key no recovery key is configured.
/// </summary>
internal static class EmbeddedRecoveryKey
{
    /// <summary>The name of the embedded resource.</summary>
    public const string ResourceName = "Chainsaw.Modules.Cases.organization-recovery-key.pem";

    /// <summary>Reads the built-in key.</summary>
    /// <returns>The key, or <see langword="null"/> when none is configured.</returns>
    /// <exception cref="InvalidOperationException">
    /// The resource is missing or holds something that is not a P-384 public key; a build like that must not run,
    /// since it would make cases that the administrator cannot open.
    /// </exception>
    public static RecoveryPublicKey? Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource {ResourceName} is missing from the build.");
        using var reader = new StreamReader(stream);
        try
        {
            return RecoveryPublicKey.FromPem(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException
            or FormatException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"The resource {ResourceName} does not hold a P-384 public key.", exception);
        }
    }
}
