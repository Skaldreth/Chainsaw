using System.Globalization;

namespace Chainsaw.SharedKernel;

/// <summary>Whether an asset is a chain's own coin or a token issued on it.</summary>
public enum AssetKind
{
    /// <summary>The chain's own coin, which its fees are paid in (BTC, ETH, TRX).</summary>
    Native,

    /// <summary>
    /// A token issued on the chain by a contract (an ERC-20 on Ethereum, a TRC-20 on TRON) or a mint (an SPL token on
    /// Solana), named by that contract or mint.
    /// </summary>
    Token,
}

/// <summary>
/// Something an amount is counted in: a chain's own coin or a token on it. Two assets are the same when the chain,
/// the kind and the contract are: a token is known by its contract, never by its symbol (anyone can issue a token
/// called USDT, and two sources may spell one token's symbol differently). The decimals only say how its smallest
/// unit is written, which the chain does not change, so they do not tell two assets apart either.
/// </summary>
public sealed record Asset
{
    /// <summary>The most decimals an asset may have; Ethereum's 18 and a margin for tokens that use more.</summary>
    public const int MaximumDecimals = 36;

    /// <summary>Creates an asset after checking it.</summary>
    /// <param name="chain">The chain it lives on.</param>
    /// <param name="kind">Whether it is the chain's coin or a token.</param>
    /// <param name="symbol">The symbol as the user sees it, for example <c>BTC</c> or <c>USDT</c>.</param>
    /// <param name="decimals">How many decimals its smallest unit is, from 0.</param>
    /// <param name="contract">The token's contract or mint as the chain writes it; null for a native coin.</param>
    /// <exception cref="ArgumentException">
    /// The symbol is empty, the decimals are out of range, or a token lacks its contract or a coin has one.
    /// </exception>
    private Asset(Chain chain, AssetKind kind, string symbol, int decimals, string? contract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, MaximumDecimals);
        if (!Enum.IsDefined(chain))
        {
            throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain.");
        }

        if ((kind == AssetKind.Token) == string.IsNullOrWhiteSpace(contract))
        {
            throw new ArgumentException(
                "A token is named by its contract, and a native coin has none.", nameof(contract));
        }

        (Chain, Kind, Symbol, Decimals, Contract) = (chain, kind, symbol.Trim(), decimals, Normalise(chain, contract));
    }

    /// <summary>Gets bitcoin, counted in satoshi (8 decimals).</summary>
    public static Asset Bitcoin { get; } = new(Chain.Bitcoin, AssetKind.Native, "BTC", 8, null);

    /// <summary>Gets ether, counted in wei (18 decimals).</summary>
    public static Asset Ether { get; } = new(Chain.Ethereum, AssetKind.Native, "ETH", 18, null);

    /// <summary>Gets tronix, counted in sun (6 decimals).</summary>
    public static Asset Tronix { get; } = new(Chain.Tron, AssetKind.Native, "TRX", 6, null);

    /// <summary>Gets litecoin, counted in litoshi (8 decimals).</summary>
    public static Asset Litecoin { get; } = new(Chain.Litecoin, AssetKind.Native, "LTC", 8, null);

    /// <summary>Gets BNB, BSC's own coin, counted in wei (18 decimals).</summary>
    public static Asset Bnb { get; } = new(Chain.Bsc, AssetKind.Native, "BNB", 18, null);

    /// <summary>Gets SOL, Solana's own coin, counted in lamports (9 decimals).</summary>
    public static Asset Sol { get; } = new(Chain.Solana, AssetKind.Native, "SOL", 9, null);

    /// <summary>Gets the chain it lives on.</summary>
    public Chain Chain { get; }

    /// <summary>Gets whether it is the chain's coin or a token.</summary>
    public AssetKind Kind { get; }

    /// <summary>Gets the symbol as the user sees it; it does not tell two tokens apart.</summary>
    public string Symbol { get; }

    /// <summary>Gets how many decimals its smallest unit is: 8 for bitcoin, 18 for ether, 6 for USDT.</summary>
    public int Decimals { get; }

    /// <summary>Gets the token's contract or mint as the chain writes it; null for a native coin.</summary>
    public string? Contract { get; }

    /// <summary>Gives a token on a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="contract">The contract or mint as the chain writes it.</param>
    /// <param name="symbol">The symbol as the user sees it.</param>
    /// <param name="decimals">How many decimals its smallest unit is.</param>
    /// <returns>The token.</returns>
    /// <exception cref="ArgumentException">An argument is not valid.</exception>
    public static Asset Token(Chain chain, string contract, string symbol, int decimals) =>
        new(chain, AssetKind.Token, symbol, decimals, contract);

    /// <summary>
    /// Writes a contract the way the chain compares it: hex addresses on Ethereum's virtual machine (Ethereum and BSC)
    /// in lower case (the mixed case is only a checksum), other chains' as they are, since base58 is case sensitive.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="contract">The contract, or null.</param>
    /// <returns>The contract as it is compared, or null.</returns>
    private static string? Normalise(Chain chain, string? contract)
    {
        var trimmed = contract?.Trim();
        return chain.IsEvm() ? trimmed?.ToLowerInvariant() : trimmed;
    }

    /// <summary>Says whether two assets are the same: the same chain, kind and contract.</summary>
    /// <param name="other">The other asset.</param>
    /// <returns>Whether they are.</returns>
    public bool Equals(Asset? other) =>
        other is not null && Chain == other.Chain && Kind == other.Kind
        && string.Equals(Contract, other.Contract, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Chain, Kind, Contract);

    /// <summary>Writes the symbol, and for a token where it lives, for example <c>USDT (Tron:TR7…)</c>.</summary>
    /// <returns>The symbol, and the chain and contract of a token.</returns>
    public override string ToString() => Kind == AssetKind.Native
        ? Symbol
        : string.Create(CultureInfo.InvariantCulture, $"{Symbol} ({Chain}:{Contract})");
}
