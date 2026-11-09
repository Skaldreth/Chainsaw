using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Solana's facts for a case, through the account model's contract (<see cref="IAccountSource"/>): SOL in lamports and
/// SPL tokens (USDT and USDC known by their mints), read from what each transaction changed in its accounts' balances
/// (<c>preBalances</c>/<c>postBalances</c> and <c>preTokenBalances</c>/<c>postTokenBalances</c>), which are the chain's
/// own record whatever instructions and inner instructions the transaction ran. A token account belongs to an owner,
/// and a token's movement is given between the owners. The sort of API is Solana's JSON-RPC, which every Solana node
/// answers. A block is a slot: its height is the slot's number and its hash the slot's <c>blockhash</c>. The interface
/// only names the chain, so that the container gives Solana's source where Solana is asked.
/// </summary>
public interface ISolanaSource : IAccountSource
{
    /// <summary>Gets the chain: <see cref="SharedKernel.Chain.Solana"/>.</summary>
    Chain IAccountSource.Chain => Chain.Solana;

    /// <summary>Gives where a page that goes back from a block starts: Solana's pages go back by slot.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The block's slot.</returns>
    long IAccountSource.PageStartOf(EthereumBlockReference block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Height;
    }
}

/// <summary>The settings of the Solana source.</summary>
public sealed record SolanaSourceOptions
{
    /// <summary>
    /// Solana's public JSON-RPC for its main network, the default: open without a key, archival (it answered for slot
    /// 100 000 000 from 2021 when tried, 2026-11-09), and limited per method: it said 10 <c>getTransaction</c> per ten
    /// seconds in its headers (<c>x-ratelimit-method-limit</c>). Any Solana node, one's own included, answers the same
    /// methods.
    /// </summary>
    public static readonly Uri PublicRpc = new("https://api.mainnet-beta.solana.com/");

    /// <summary>Gets Solana's endpoints until the user changes them in the settings (sketch KL2b).</summary>
    public IReadOnlyList<Uri> DefaultEndpoints { get; init; } = [PublicRpc];

    /// <summary>
    /// Gets how many slots deep an answer's slot must be before what rests on it is never fetched again. Every lookup
    /// asks with the commitment <c>finalized</c>, so a node only answers with what a supermajority of the stake has
    /// rooted and cannot be rolled back; one slot (the answer itself) is therefore enough.
    /// </summary>
    public int SafeConfirmations { get; init; } = 1;

    /// <summary>
    /// Gets how many signatures a page of an address's history asks for. Each one's transaction is fetched to read
    /// what it moved, so a page costs as many lookups as it has entries; 25 keeps a page within half a minute at the
    /// public endpoint's pace.
    /// </summary>
    public int PageSize { get; init; } = 25;

    /// <summary>
    /// Gets the shortest time between two requests to the default endpoint: the public JSON-RPC allows 10
    /// <c>getTransaction</c> per ten seconds per address, so Chainsaw asks a little slower than one a second; a source
    /// that asks Chainsaw to wait (429 with <c>Retry-After</c>) is waited for too.
    /// </summary>
    public TimeSpan DefaultPace { get; init; } = TimeSpan.FromSeconds(1.1);
}
