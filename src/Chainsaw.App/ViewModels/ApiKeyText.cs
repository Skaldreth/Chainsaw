using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>What the rows of the sources say of an API key: its last characters, and where it is got.</summary>
public static class ApiKeyText
{
    /// <summary>What the field says when an address was pasted in place of a key.</summary>
    public const string AddressText = "Det här är en adress, inte en API-nyckel. Klistra in nyckeln från tjänsten.";

    /// <summary>What the field says when the vault cannot be written.</summary>
    public const string FailedText = "Valvet gick inte att skriva. Lås upp och försök igen.";

    /// <summary>How many characters of a key are shown, at its end.</summary>
    private const int TailLength = 4;

    /// <summary>Where each service's key is got, by the vault's name for it.</summary>
    private static readonly Dictionary<string, string> Places = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Etherscan"] = "Etherscan behöver en gratis nyckel · skaffas på etherscan.io › API Keys",
    };

    /// <summary>Gives a key's last characters behind dots, or only dots for a short key.</summary>
    /// <param name="secret">The key.</param>
    /// <returns>For example <c>••••FR61</c>.</returns>
    public static string Tail(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return secret.Length <= TailLength * 2 ? "••••" : "••••" + secret[^TailLength..];
    }

    /// <summary>Says that a service needs a key, and where it is got when Chainsaw knows.</summary>
    /// <param name="service">The vault's name for the key.</param>
    /// <returns>The text.</returns>
    public static string WhereToGet(string service) =>
        Places.TryGetValue(service, out var place) ? place : $"{service} behöver en nyckel";

    /// <summary>
    /// Tells whether what was written is a chain's address rather than a key: one with a checksum or a prefix that a
    /// key does not have. Solana's are left out, since any key in Base58 of that length would read as one.
    /// </summary>
    /// <param name="secret">What was written.</param>
    /// <returns><see langword="true"/> when it is an address.</returns>
    public static bool IsAddress(string secret) =>
        ChainStartPoint.Recognise(secret) is { Kind: StartPointKind.Address, Chain: not Chain.Solana };
}
