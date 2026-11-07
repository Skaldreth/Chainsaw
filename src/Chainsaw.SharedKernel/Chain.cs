namespace Chainsaw.SharedKernel;

/// <summary>
/// The blockchains Chainsaw knows. Bitcoin is traced first; the others are listed so that the model (for example an
/// edge that continues on another chain) can name them from the start.
/// </summary>
public enum Chain
{
    /// <summary>Bitcoin: unspent outputs (UTXO), amounts in satoshi.</summary>
    Bitcoin,

    /// <summary>Ethereum: accounts, amounts in wei, tokens such as ERC-20.</summary>
    Ethereum,

    /// <summary>TRON: accounts, amounts in sun, tokens such as TRC-20 (USDT).</summary>
    Tron,
}
