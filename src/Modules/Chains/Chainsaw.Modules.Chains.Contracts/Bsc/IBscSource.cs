using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// BSC's (BNB Smart Chain's) facts for a case, through the account model's contract (<see cref="IAccountSource"/>):
/// BNB in wei, BEP-20 tokens from their <c>Transfer</c> events (USDT and USDC known by their contracts), and BNB sent
/// by contracts. BSC runs Ethereum's virtual machine and writes its addresses and transactions as Ethereum does, so the
/// sort of API is the same as Ethereum's (Blockscout's). The interface only names the chain, so that the container
/// gives BSC's source where BSC is asked.
/// </summary>
public interface IBscSource : IAccountSource
{
    /// <summary>Gets the chain: <see cref="SharedKernel.Chain.Bsc"/>.</summary>
    Chain IAccountSource.Chain => Chain.Bsc;
}

/// <summary>The settings of the BSC source.</summary>
public sealed record BscSourceOptions
{
    /// <summary>
    /// Gets BSC's endpoints until the user adds them in the settings (sketch KL2b). None by default: no open
    /// Blockscout for BSC is known (Blockscout's own list of chains has none for chain 56, 2026-11-09), BscScan's
    /// API has moved to Etherscan's, which for BSC needs a paid key sent in the address, and BSC's public JSON-RPC
    /// nodes keep no history per address. The user adds an endpoint that speaks Blockscout's API: one run in-house or
    /// one bought, with its key in the vault.
    /// </summary>
    public IReadOnlyList<Uri> DefaultEndpoints { get; init; } = [];

    /// <summary>
    /// Gets how many blocks deep a block must be before what rests on it is never fetched again. BSC's validators
    /// vote a block final within a few blocks (fast finality, BEP-126), and its blocks come in well under two seconds;
    /// 50 blocks is a wide margin of well under two minutes.
    /// </summary>
    public int SafeConfirmations { get; init; } = 50;

    /// <summary>Gets how many entries a page of an address's history asks for.</summary>
    public int PageSize { get; init; } = 100;
}
