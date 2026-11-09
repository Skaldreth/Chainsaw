namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>The settings of the Zcash source (Blockbook's API).</summary>
public sealed record ZcashSourceOptions
{
    /// <summary>
    /// The open Blockbook for Zcash that Zelcore runs, which answers without a key (tried 2026-11-09: <c>api/</c>,
    /// <c>api/v2/tx</c>, <c>api/v2/tx-specific</c>, <c>api/v2/address</c> and <c>api/v2/block</c>).
    /// </summary>
    public static readonly Uri ZelcoreBlockbook = new("https://blockbook.zec.zelcore.io/");

    /// <summary>Gets the endpoints Zcash has until the user changes them.</summary>
    public IReadOnlyList<Uri> DefaultEndpoints { get; init; } = [ZelcoreBlockbook];

    /// <summary>
    /// Gets how many blocks deep an answer's block must be before it is never fetched again; Zcash's blocks come
    /// every 75 seconds, so 24 blocks is half an hour, as Bitcoin's 6 are an hour.
    /// </summary>
    public int SafeConfirmations { get; init; } = 24;

    /// <summary>
    /// Gets how many of an address's transactions Blockbook gives on a page; Blockbook gives at most 1 000.
    /// </summary>
    public int PageSize { get; init; } = 1_000;
}
