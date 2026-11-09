namespace Chainsaw.Modules.Chains;

/// <summary>
/// What a chain's sources can be checked against without trusting any of them: the hash of the chain's first block,
/// which only an endpoint of this very chain and network answers with, and the highest the chain can have come by a
/// time. A height above that is not this chain's (a test network or another chain added by mistake, or an endpoint
/// that lies); trusted, it would make every answer look deep, so that none is fetched again and a reorganisation is
/// never noticed.
/// </summary>
internal static class ChainLimits
{
    /// <summary>
    /// Each known chain by its name in the tables: its first block's hash as its sort of API writes it, when that
    /// block was made, and an average time between blocks far shorter than the chain has ever had (Bitcoin aims at ten
    /// minutes, Ethereum has at most one block per twelve seconds since 2022, TRON one per three seconds since its main
    /// network began in June 2018, Litecoin aims at two and a half minutes, and BSC, whose blocks have come faster with
    /// each fork (three seconds at first, under a second since 2025), is bounded at a quarter of a second). Litecoin's
    /// and BSC's first blocks were read from litecoinspace.org and from BSC's public JSON-RPC node (2026-11-09). TRON keeps its slots so closely, with only a few missed, that its bound is set just
    /// under the slot (2.8 seconds): a tip three times too high is refused, while a true one never comes near.
    /// </summary>
    private static readonly Dictionary<string, (string Genesis, DateTimeOffset Made, TimeSpan Fastest)> Known =
        new(StringComparer.Ordinal)
        {
            ["bitcoin-mainnet"] = (
                "000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f",
                new DateTimeOffset(2009, 1, 3, 18, 15, 5, TimeSpan.Zero),
                TimeSpan.FromMinutes(5)),
            [BlockscoutEthereumSource.ChainName] = (
                "0xd4e56740f876aef8c010b86a40d5f56745a118d0906a34e69aec8c0db1cb8fa3",
                new DateTimeOffset(2015, 7, 30, 15, 26, 13, TimeSpan.Zero),
                TimeSpan.FromSeconds(6)),
            [RawResponseStore.LitecoinChainName] = (
                "12a765e31ffd4059bada1e25190f6e98c99d9714d334efa41a195a7e7e04bfe2",
                new DateTimeOffset(2011, 10, 7, 7, 31, 5, TimeSpan.Zero),
                TimeSpan.FromSeconds(75)),
            [BlockscoutBscSource.ChainName] = (
                "0x0d21840abff46b96c84b2ac9e10e4f5cdaeb5693cb665db62a2f3b02d2d57b5b",
                new DateTimeOffset(2020, 4, 20, 13, 46, 54, TimeSpan.Zero),
                TimeSpan.FromSeconds(0.25)),
            [TronGridTronSource.ChainName] = (
                "00000000000000001ebf88508a03865c71d452e25f4d51194196a1d22b6653dc",
                new DateTimeOffset(2018, 6, 25, 0, 0, 0, TimeSpan.Zero),
                TimeSpan.FromSeconds(2.8)),
        };

    /// <summary>Gives the hash of a chain's first block (height 0).</summary>
    /// <param name="chainName">The chain's name in the tables, for example <c>bitcoin-mainnet</c>.</param>
    /// <returns>The hash, or <see langword="null"/> for a chain without one known (the test networks).</returns>
    public static string? Genesis(string chainName) =>
        Known.TryGetValue(chainName, out var known) ? known.Genesis : null;

    /// <summary>
    /// Gives when a chain's first block was made and its fastest average time between blocks, from which
    /// <see cref="HighestPlausible"/> counts; for a check of many heights in one query.
    /// </summary>
    /// <param name="chainName">The chain's name in the tables.</param>
    /// <returns>The two, or <see langword="null"/> for a chain without a known bound.</returns>
    public static (DateTimeOffset Made, TimeSpan Fastest)? Pace(string chainName) =>
        Known.TryGetValue(chainName, out var known) ? (known.Made, known.Fastest) : null;

    /// <summary>Gives the highest a chain can have come by a time.</summary>
    /// <param name="chainName">The chain's name in the tables.</param>
    /// <param name="at">The time.</param>
    /// <returns>The height, or <see langword="null"/> for a chain without a known bound.</returns>
    public static long? HighestPlausible(string chainName, DateTimeOffset at) =>
        Known.TryGetValue(chainName, out var known)
            ? Math.Max(0, (at - known.Made).Ticks / known.Fastest.Ticks)
            : null;
}
