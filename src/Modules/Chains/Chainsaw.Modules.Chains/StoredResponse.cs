using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>A raw response read back from the case's sealed table.</summary>
/// <param name="Kind">What was asked (<see cref="ResponseKinds"/>).</param>
/// <param name="Subject">What it was asked about (a txid, an address and page, a block hash).</param>
/// <param name="Origin">Where and when it came from, and who fetched it.</param>
/// <param name="Body">The raw bytes, checked against their SHA-256.</param>
internal sealed record StoredResponse(string Kind, string Subject, ResponseOrigin Origin, byte[] Body);

/// <summary>What a response adds to the indexes over the sealed responses, read from the response itself.</summary>
/// <param name="Transactions">
/// The transactions in the response with their block's height and hash, if confirmed; the hash shows a move to another
/// block at the same height (sketch GR9a).
/// </param>
/// <param name="TipHeight">The chain's height, for a response that gives it.</param>
internal sealed record ResponseIndex(IReadOnlyList<(Txid Txid, long? Height, string? Hash)> Transactions, long? TipHeight)
{
    /// <summary>Gets an index entry that adds nothing.</summary>
    public static ResponseIndex None { get; } = new([], null);

    /// <summary>Gets the highest block the entry names, or <see langword="null"/> when it names none.</summary>
    public long? Highest => Transactions.Select(transaction => transaction.Height).Append(TipHeight).Max();
}

/// <summary>The kinds of request, stored with each response so that it can be found again.</summary>
internal static class ResponseKinds
{
    /// <summary>One transaction, <c>/tx/:txid</c>.</summary>
    public const string Transaction = "bitcoin.tx";

    /// <summary>Who spends a transaction's outputs, <c>/tx/:txid/outspends</c>.</summary>
    public const string OutputSpends = "bitcoin.outspends";

    /// <summary>A page of an address's transactions, <c>/address/:address/txs[/chain/:after]</c>.</summary>
    public const string AddressTransactions = "bitcoin.address-txs";

    /// <summary>A block, <c>/block/:hash</c>.</summary>
    public const string Block = "bitcoin.block";

    /// <summary>A transaction's own bytes as hex, <c>/tx/:txid/hex</c>.</summary>
    public const string TransactionHex = "bitcoin.tx-hex";

    /// <summary>A block's 80-byte header as hex, <c>/block/:hash/header</c>.</summary>
    public const string BlockHeader = "bitcoin.block-header";

    /// <summary>What the source says about an address as a whole, <c>/address/:address</c>.</summary>
    public const string AddressSummary = "bitcoin.address";

    /// <summary>The chain's height, <c>/blocks/tip/height</c>.</summary>
    public const string TipHeight = "bitcoin.tip-height";

    /// <summary>The newest block's hash, <c>/blocks/tip/hash</c>.</summary>
    public const string TipHash = "bitcoin.tip-hash";

    /// <summary>The hash of the block at a height, <c>/block-height/:height</c>, asked to confirm a block.</summary>
    public const string BlockAtHeight = "bitcoin.block-height";

    /// <summary>The proof that a transaction is in its block, <c>/tx/:txid/merkle-proof</c>.</summary>
    public const string MerkleProof = "bitcoin.merkle-proof";
}
