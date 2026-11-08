namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>The settings of the TRON source.</summary>
public sealed record TronSourceOptions
{
    /// <summary>
    /// TronGrid's API for TRON, the default: open without a key (slowly; a key from the vault goes in the header
    /// <c>TRON-PRO-API-KEY</c>). Its wallet part (<c>wallet/…</c>) is what every TRON node answers, so a node of one's
    /// own can answer the transactions and blocks; an address's history (<c>v1/accounts/…</c>) is TronGrid's own.
    /// </summary>
    public static readonly Uri TronGrid = new("https://api.trongrid.io/");

    /// <summary>Gets TRON's endpoints until the user changes them in the settings (sketch KL1a).</summary>
    public IReadOnlyList<Uri> DefaultEndpoints { get; init; } = [TronGrid];

    /// <summary>
    /// Gets how many blocks deep a block must be before what rests on it is never fetched again. TRON's 27 block
    /// producers make a block every three seconds, and a block is final (solidified) once 19 of them have built on it;
    /// 20 is one more.
    /// </summary>
    public int SafeConfirmations { get; init; } = 20;

    /// <summary>
    /// Gets how long before the source's own clock a page of an address's history must end before it is never fetched
    /// again: TRON's sources page by time, not by block, and a page that ends ten minutes back rests only on final
    /// blocks (20 blocks take a minute).
    /// </summary>
    public TimeSpan SettledAge { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets how many entries a page of an address's history asks for (TronGrid gives at most 200).</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>
    /// Gets the shortest time between two requests to the default endpoint: TronGrid limits requests without a key
    /// hard, so Chainsaw's own careful choice is one a second; a source that asks Chainsaw to wait is waited for too.
    /// </summary>
    public TimeSpan DefaultPace { get; init; } = TimeSpan.FromSeconds(1);
}
