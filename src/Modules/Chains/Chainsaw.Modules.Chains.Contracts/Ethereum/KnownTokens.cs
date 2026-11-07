using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A token's issuer when it can freeze the token at an address: the issuer's contract has a blacklist that stops an
/// address from moving it, used on request (CLAUDE.md, <em>Kan frysas</em>).
/// </summary>
/// <param name="Name">The issuer, for example <c>Tether</c>.</param>
/// <param name="Glyph">The token's mark as the graph shows it first in an amount, for example <c>₮</c>.</param>
public sealed record TokenIssuer(string Name, string Glyph);

/// <summary>
/// The tokens Chainsaw recognises by their contract, never by their symbol (anyone can issue a token called USDT):
/// USDT and USDC, whose issuers can freeze them. A token that is not here is shown with its contract and the symbol
/// the source gave, and is never taken for one of these.
/// </summary>
public static class KnownTokens
{
    /// <summary>Tether's USDT on Ethereum, 6 decimals.</summary>
    public static Asset EthereumUsdt { get; } =
        Asset.Token(Chain.Ethereum, "0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT", 6);

    /// <summary>Circle's USDC on Ethereum, 6 decimals.</summary>
    public static Asset EthereumUsdc { get; } =
        Asset.Token(Chain.Ethereum, "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", "USDC", 6);

    /// <summary>Tether's USDT on TRON (TRC-20), 6 decimals, for when TRON is built.</summary>
    public static Asset TronUsdt { get; } =
        Asset.Token(Chain.Tron, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "USDT", 6);

    /// <summary>The tokens whose issuer can freeze them, by asset (chain and contract).</summary>
    private static readonly Dictionary<Asset, TokenIssuer> Freezable = new()
    {
        [EthereumUsdt] = new TokenIssuer("Tether", "₮"),
        [EthereumUsdc] = new TokenIssuer("Circle", "$"),
        [TronUsdt] = new TokenIssuer("Tether", "₮"),
    };

    /// <summary>Gives the issuer that can freeze a token, when Chainsaw knows its contract.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The issuer, or <see langword="null"/> for a coin or a token Chainsaw does not know.</returns>
    public static TokenIssuer? IssuerOf(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Freezable.GetValueOrDefault(asset);
    }

    /// <summary>
    /// Gives a known token with Chainsaw's own symbol and decimals, so that a source's spelling never changes how it
    /// is shown; any other asset is given back as it is.
    /// </summary>
    /// <param name="asset">The asset as a source gave it.</param>
    /// <returns>The known token, or the asset.</returns>
    public static Asset Canonical(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Freezable.Keys.FirstOrDefault(known => known.Equals(asset)) ?? asset;
    }

    /// <summary>
    /// Gives an asset's mark as the graph shows it first in an amount (sketch TK1a): <c>Ξ</c> for ether, <c>₮</c> for
    /// USDT and <c>$</c> for USDC; <see langword="null"/> for any other, which is shown by its symbol.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The mark, or <see langword="null"/>.</returns>
    public static string? GlyphOf(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Equals(Asset.Ether))
        {
            return "Ξ";
        }

        return IssuerOf(asset)?.Glyph;
    }
}
