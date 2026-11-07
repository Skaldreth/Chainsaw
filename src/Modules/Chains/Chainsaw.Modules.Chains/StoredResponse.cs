using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>A raw response read back from the case's sealed table.</summary>
/// <param name="Kind">What was asked (<see cref="ResponseKinds"/>).</param>
/// <param name="Subject">What it was asked about (a txid, an address and page, a block hash).</param>
/// <param name="Origin">Where and when it came from, and who fetched it.</param>
/// <param name="Body">The raw bytes, checked against their SHA-256.</param>
internal sealed record StoredResponse(string Kind, string Subject, ResponseOrigin Origin, byte[] Body);

/// <summary>What a response adds to the indexes over the sealed responses, read from the response itself.</summary>
/// <param name="Transactions">The transactions in the response with their block height, if confirmed.</param>
/// <param name="TipHeight">The chain's height, for a response that gives it.</param>
internal sealed record ResponseIndex(IReadOnlyList<(Txid Txid, long? Height)> Transactions, long? TipHeight)
{
    /// <summary>Gets an index entry that adds nothing.</summary>
    public static ResponseIndex None { get; } = new([], null);
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

    /// <summary>The chain's height, <c>/blocks/tip/height</c>.</summary>
    public const string TipHeight = "bitcoin.tip-height";
}
