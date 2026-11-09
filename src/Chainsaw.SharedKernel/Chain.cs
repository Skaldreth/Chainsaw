namespace Chainsaw.SharedKernel;

/// <summary>
/// The blockchains Chainsaw knows. Bitcoin is traced first; the others are listed so that the model (for example an
/// edge that continues on another chain) can name them from the start. New chains are added last, so that a chain's
/// number never changes.
/// </summary>
public enum Chain
{
    /// <summary>Bitcoin: unspent outputs (UTXO), amounts in satoshi.</summary>
    Bitcoin,

    /// <summary>Ethereum: accounts, amounts in wei, tokens such as ERC-20.</summary>
    Ethereum,

    /// <summary>TRON: accounts, amounts in sun, tokens such as TRC-20 (USDT).</summary>
    Tron,

    /// <summary>
    /// Litecoin: unspent outputs (UTXO) as Bitcoin, amounts in litoshi; its shielded part (MWEB) is where the trail
    /// ends.
    /// </summary>
    Litecoin,

    /// <summary>
    /// BNB Smart Chain (BSC): accounts on Ethereum's virtual machine, amounts in wei of BNB, tokens such as BEP-20.
    /// </summary>
    Bsc,

    /// <summary>
    /// Zcash: its transparent part keeps unspent outputs (UTXO) as Bitcoin, amounts in zatoshi; its shielded pools
    /// (Sprout, Sapling, Orchard) are where the trail ends.
    /// </summary>
    Zcash,

    /// <summary>
    /// Solana: accounts, amounts in lamports of SOL (9 decimals), tokens such as SPL (USDT and USDC) held in token
    /// accounts that belong to an owner.
    /// </summary>
    Solana,
}
