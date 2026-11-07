using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// The check without the case (what the background process of stage 27 runs): addresses and a filter in, matches
/// out, from any feed.
/// </summary>
public sealed class WatchCheckTests
{
    private readonly WatchCheck _check = new();

    private readonly FakeFeed _feed = new();

    private static readonly WatchFilter Both = new(Chain.Bitcoin, WatchDirection.Both, [], [], null, null);

    [Fact]
    public async Task Only_blocks_above_the_start_count_and_each_transaction_once()
    {
        var a = Synthetic.Btc("a");
        var b = Synthetic.Btc("b");
        var other = Synthetic.Wpkh("other");
        var old = BitcoinCaseFeed.Of(Synthetic.Tx("old", 900_000).In(Synthetic.Wpkh("a"), 5_000).Out(other, 4_000)
            .Build());
        var shared = BitcoinCaseFeed.Of(Synthetic.Tx("shared", 900_005).In(Synthetic.Wpkh("a"), 5_000)
            .In(Synthetic.Wpkh("b"), 5_000).Out(other, 9_000).Build());
        _feed.Pages[a] = new WatchFeedPage(WatchFeedStatus.Read, [shared, old], [1, 2], false, null);
        _feed.Pages[b] = new WatchFeedPage(WatchFeedStatus.Read, [shared], [3], false, null);

        var result = await _check.CheckAsync([a, b], Both, _feed, after: 900_000, downTo: 899_990);

        var match = Assert.Single(result.Matches);
        Assert.Equal(shared.Id, match.Transaction);
        Assert.Equal(9_000, (long)match.Amount.Units);
        Assert.Equal([1L, 2, 3], result.Origins);
        Assert.Equal(900_005, result.Highest);
        Assert.Equal(WatchFeedStatus.Read, result.Status);
        Assert.All(_feed.Asked, asked => Assert.Equal(899_990, asked.DownTo));
    }

    [Fact]
    public async Task A_failed_address_is_said_and_the_others_still_count()
    {
        var a = Synthetic.Btc("a");
        var b = Synthetic.Btc("b");
        var hit = BitcoinCaseFeed.Of(Synthetic.Tx("hit", 900_009).In(Synthetic.Wpkh("other"), 5_000)
            .Out(Synthetic.Wpkh("b"), 4_000).Build());
        _feed.Pages[a] = new WatchFeedPage(WatchFeedStatus.Failed, [], [], false, null);
        _feed.Pages[b] = new WatchFeedPage(WatchFeedStatus.Read, [hit], [], false, null);

        var result = await _check.CheckAsync([a, b], Both, _feed, 900_000, 900_000);

        Assert.Single(result.Matches);
        Assert.Equal(WatchFeedStatus.Failed, result.Status);
        Assert.Contains(a.Value, Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_watched_asset_is_passed_to_the_feed()
    {
        var node = Synthetic.On(Synthetic.Eth("node"));
        var filter = new WatchFilter(Chain.Ethereum, WatchDirection.In, [], [], Synthetic.Usdt, null);

        await _check.CheckAsync([node], filter, _feed, 1, 1);

        Assert.Equal(Synthetic.Usdt, Assert.Single(_feed.Asked).Asset);
    }
}
