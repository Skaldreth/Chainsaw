using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>The codes of the chains' own currencies.</summary>
public static class ChainCurrencies
{
    /// <summary>Gives the code of a chain's own currency.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>For example <c>BTC</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The chain is unknown.</exception>
    public static string Code(this Chain chain) => chain switch
    {
        Chain.Bitcoin => "BTC",
        Chain.Ethereum => "ETH",
        Chain.Tron => "TRX",
        Chain.Litecoin => "LTC",
        Chain.Bsc => "BNB",
        _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain."),
    };
}
