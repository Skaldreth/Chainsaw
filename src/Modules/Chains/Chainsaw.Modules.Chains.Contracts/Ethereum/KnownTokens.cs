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
/// USDT and USDC, whose issuers can freeze them on Ethereum and TRON, and the same two on BSC, which Binance issues as
/// bridged tokens (<em>Binance-Peg</em>) and which Chainsaw knows no way to freeze. A token that is not here is shown
/// with its contract and the symbol the source gave, and is never taken for one of these.
/// </summary>
public static class KnownTokens
{
    /// <summary>Where the BEP-20 tokens' contracts were read: CoinGecko's records of the bridged tokens.</summary>
    public const string BscSource =
        "https://api.coingecko.com/api/v3/coins/binance-bridged-usdt-bnb-smart-chain och "
        + "https://api.coingecko.com/api/v3/coins/binance-bridged-usdc-bnb-smart-chain (läst 2026-11-09)";

    /// <summary>Tether's USDT on Ethereum, 6 decimals.</summary>
    public static Asset EthereumUsdt { get; } =
        Asset.Token(Chain.Ethereum, "0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT", 6);

    /// <summary>Circle's USDC on Ethereum, 6 decimals.</summary>
    public static Asset EthereumUsdc { get; } =
        Asset.Token(Chain.Ethereum, "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", "USDC", 6);

    /// <summary>
    /// Wrapped ether (WETH9) on Ethereum, 18 decimals: ether as a token, which an exchange's pool holds and its router
    /// unwraps to ether, so that a swap to ether moves WETH inside the transaction.
    /// </summary>
    public static Asset EthereumWeth { get; } =
        Asset.Token(Chain.Ethereum, "0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2", "WETH", 18);

    /// <summary>
    /// Wrapped BNB (WBNB) on BSC, 18 decimals: BNB as a token, as WETH is ether on Ethereum, so that a swap to BNB moves
    /// WBNB inside the transaction. The contract was read from CoinGecko's record of WBNB (2026-11-09).
    /// </summary>
    public static Asset BscWbnb { get; } =
        Asset.Token(Chain.Bsc, "0xbb4cdb9cbd36b01bd1cbaebf2de08d9173bc095c", "WBNB", 18);

    /// <summary>Gives a chain's own coin as a token (WETH on Ethereum, WBNB on BSC), when Chainsaw knows it.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The wrapped coin, or <see langword="null"/> on a chain without one Chainsaw knows.</returns>
    public static Asset? WrappedNative(Chain chain) => chain switch
    {
        Chain.Ethereum => EthereumWeth,
        Chain.Bsc => BscWbnb,
        _ => null,
    };

    /// <summary>Says whether an asset is a chain's own coin as a token (WETH, WBNB).</summary>
    /// <param name="asset">The asset.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsWrappedNative(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset == WrappedNative(asset.Chain);
    }

    /// <summary>Tether's USDT on TRON (TRC-20), 6 decimals, for when TRON is built.</summary>
    public static Asset TronUsdt { get; } =
        Asset.Token(Chain.Tron, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "USDT", 6);

    /// <summary>
    /// USDT on BSC (BEP-20, 18 decimals): Binance's bridged token (<em>Binance-Peg BSC-USD</em>), not Tether's own,
    /// whose contract was read from CoinGecko (<see cref="BscSource"/>). Tether does not issue USDT on BSC, so
    /// Chainsaw does not mark it <em>Kan frysas</em>.
    /// </summary>
    public static Asset BscUsdt { get; } =
        Asset.Token(Chain.Bsc, "0x55d398326f99059ff775485246999027b3197955", "USDT", 18);

    /// <summary>
    /// USDC on BSC (BEP-20, 18 decimals): Binance's bridged token (<em>Binance-Peg USD Coin</em>), not Circle's own,
    /// whose contract was read from CoinGecko (<see cref="BscSource"/>); not marked <em>Kan frysas</em> either.
    /// </summary>
    public static Asset BscUsdc { get; } =
        Asset.Token(Chain.Bsc, "0x8ac76a51cc950d9822d68b83fe1ad97b32cd580d", "USDC", 18);

    /// <summary>Where Ethereum's DAI and WBTC were read: the chain explorer Blockscout's token records.</summary>
    public const string EthereumSource =
        "https://eth.blockscout.com/api?module=token&action=getToken&contractaddress=… (läst 2026-11-09)";

    /// <summary>
    /// DAI on Ethereum, 18 decimals: Sky's (formerly MakerDAO's) dollar stablecoin, read from Blockscout
    /// (<see cref="EthereumSource"/>: <em>Dai</em>, 18 decimals). Its contract has no blacklist, so it is not marked
    /// <em>Kan frysas</em>.
    /// </summary>
    public static Asset EthereumDai { get; } =
        Asset.Token(Chain.Ethereum, "0x6b175474e89094c44da98b954eedeac495271d0f", "DAI", 18);

    /// <summary>
    /// Wrapped bitcoin (WBTC) on Ethereum, 8 decimals, read from Blockscout (<see cref="EthereumSource"/>:
    /// <em>Wrapped Bitcoin</em>, 8 decimals). Chainsaw knows no way for its custodian to freeze it at an address, so it
    /// is not marked <em>Kan frysas</em>.
    /// </summary>
    public static Asset EthereumWbtc { get; } =
        Asset.Token(Chain.Ethereum, "0x2260fac5e5542a773aa44fbcfedf7c193bc2c599", "WBTC", 8);

    /// <summary>
    /// Every token Chainsaw knows by its contract, with its mark (or none, when it is shown by its symbol) and, when
    /// its issuer can freeze it, the issuer.
    /// </summary>
    private static readonly Dictionary<Asset, (string? Glyph, TokenIssuer? Freezer)> Known = new()
    {
        [EthereumUsdt] = ("₮", new TokenIssuer("Tether", "₮")),
        [EthereumUsdc] = ("$", new TokenIssuer("Circle", "$")),
        [TronUsdt] = ("₮", new TokenIssuer("Tether", "₮")),
        [BscUsdt] = ("₮", null),
        [BscUsdc] = ("$", null),
        [EthereumDai] = (null, null),
        [EthereumWbtc] = (null, null),
    };

    /// <summary>Gives the issuer that can freeze a token, when Chainsaw knows its contract.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>
    /// The issuer, or <see langword="null"/> for a coin, a token Chainsaw does not know, or a known token whose issuer
    /// Chainsaw knows no way to freeze (USDT and USDC on BSC).
    /// </returns>
    public static TokenIssuer? IssuerOf(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Known.TryGetValue(asset, out var known) ? known.Freezer : null;
    }

    /// <summary>
    /// Says whether Chainsaw knows a token by its contract (USDT and USDC on Ethereum, TRON and BSC, and DAI and WBTC on
    /// Ethereum), whether or not its issuer can freeze it: a known token's movements are read and watched, a
    /// stranger's are only shown.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <returns><see langword="true"/> for a known token.</returns>
    public static bool IsKnown(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Known.ContainsKey(asset);
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
        return Known.Keys.FirstOrDefault(known => known.Equals(asset)) ?? asset;
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

        return Known.TryGetValue(asset, out var known) ? known.Glyph : null;
    }
}
