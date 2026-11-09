using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// Watches on TRON after the review of round 8: a token Chainsaw does not know (spam, poisoning) is never a hit, a page
/// the source could not read whole keeps the check where it was, and a tip in USDT on a transaction that does not give
/// it is rejected instead of waiting for ever.
/// </summary>
public sealed class TronWatchTests : IDisposable
{
    private static readonly EthereumAddress Node = EthereumAddress.Parse(Chain.Tron, "41" + new string('a', 40));
    private static readonly EthereumAddress Sender = EthereumAddress.Parse(Chain.Tron, "41" + new string('b', 40));
    private static readonly EthereumBlockReference Block =
        new(80_000_040, null, new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_token_chainsaw_does_not_know_is_never_a_hit_on_tron()
    {
        await WatchAllAsync();
        var spam = Asset.Token(Chain.Tron, EthereumAddress.Parse(Chain.Tron, "41" + new string('c', 40)).Value,
            "USDT", 6);
        _harness.Tron.Holds(Node, EthereumHistory.Tokens, Transfer("spam", new AssetAmount(spam, 1_000_000)));
        _harness.Tron.BlockHashes[Block.Height] = Id(Block.Height);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Tron, 80_000_050);

        Assert.Empty(run.Hits);
    }

    [Fact]
    public async Task A_page_the_source_could_not_read_whole_keeps_the_check_where_it_was()
    {
        await WatchAllAsync();
        _harness.Tron.Incomplete = true;
        _harness.Tron.Holds(Node, EthereumHistory.Tokens,
            Transfer("placed", new AssetAmount(KnownTokens.TronUsdt, 1_000_000)));
        _harness.Tron.BlockHashes[Block.Height] = Id(Block.Height);
        var before = _harness.Service.GetChecked(_harness.Case).Values.Single();

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Tron, 80_000_050);

        Assert.Contains(run.Outcomes.SelectMany(outcome => outcome.Problems), problem =>
            problem.StartsWith("Väntar på källan för tx " + new string('e', 64), StringComparison.Ordinal));

        // A movement the source could not place may lie anywhere on the page, also below the one it did place.
        Assert.Equal(before.Height, _harness.Service.GetChecked(_harness.Case).Values.Single().Height);
    }

    [Fact]
    public async Task A_tip_in_usdt_that_the_whole_transaction_does_not_give_is_rejected()
    {
        var hash = new string('d', 64);
        var trx = new AssetAmount(Asset.Tronix, 5_000_000);
        var block = Block with { Hash = Id(Block.Height) };
        _harness.Tron.Transactions[hash] = new EthereumTransaction(
            EthereumTxHash.Parse(Chain.Tron, hash), block, Sender, Node, trx, AssetAmount.Zero(Asset.Tronix),
            EthereumOutcome.Succeeded,
            [new EthereumTransfer(EthereumTxHash.Parse(Chain.Tron, hash), EthereumTransferKind.Native, Sender, Node,
                trx, 0, block)])
        {
            // Another token in it could not be counted: the whole transaction is incomplete for that one only.
            IsComplete = false,
        };
        var usdt = new AssetAmount(KnownTokens.TronUsdt, 9_000_000);
        var match = new WatchMatch(
            hash, new WatchBlock(Block.Height, Id(Block.Height), Block.Time), WatchDirection.In, usdt,
            [Sender.ToChainAddress()]);
        var tip = new WatchTip(1, "w", Chain.Tron, match, Guid.NewGuid(), [], 1, DateTimeOffset.UnixEpoch);

        var finding = await TipLookup.ReadAsync(
            new WatchCheck(), _harness.Bitcoin, _harness.Ethereum, _harness.Tron, _harness.Case, tip,
            new HashSet<ChainAddress> { Node.ToChainAddress() },
            new WatchFilter(Chain.Tron, WatchDirection.In, [], [], null, null), 80_000_000, CancellationToken.None);

        Assert.Equal(TipVerdict.Rejected, finding.Verdict);
    }

    /// <summary>Watches the node for every asset coming in, started at block 80 000 000.</summary>
    /// <returns>A task that completes when the watch has started.</returns>
    private async Task WatchAllAsync()
    {
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, Node.ToChainAddress(), "T…node"),
            new WatchFilter(Chain.Tron, WatchDirection.In, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Tron, 80_000_000);
    }

    /// <summary>Makes a movement into the node from the sender in the block.</summary>
    /// <param name="label">What the transaction's id is made of.</param>
    /// <param name="amount">The amount.</param>
    /// <returns>The movement.</returns>
    private static EthereumTransfer Transfer(string label, AssetAmount amount) => new(
        EthereumTxHash.Parse(Chain.Tron, Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(label)))),
        EthereumTransferKind.Token, Sender, Node, amount, 0, Block);

    /// <summary>Gives a block's made-up id, its number in eight bytes first.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The id.</returns>
    private static string Id(long height) =>
        height.ToString("x16", System.Globalization.CultureInfo.InvariantCulture) + new string('9', 48);
}
