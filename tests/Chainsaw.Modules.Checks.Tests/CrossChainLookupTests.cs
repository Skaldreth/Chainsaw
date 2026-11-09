using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The cross-chain search (<see cref="CrossChainLookup"/>): it asks the other chain's source for the recipient a
/// swap's memo names and nothing else (principle 4), in the mode it is given, gives the payments to it, and says
/// honestly when the recipient could not be read (principle 9).
/// </summary>
public sealed class CrossChainLookupTests : IDisposable
{
    private static readonly BitcoinAddress SwapIn = Wpkh("lookup-swap-in");

    private static readonly EthereumAddress Recipient =
        EthereumAddress.Parse("0x" + Convert.ToHexStringLower(SHA256.HashData("recipient"u8).AsSpan(0, 20)));

    private readonly Harness _harness = new();
    private readonly FakeEthereum _ethereum = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Only_the_memos_recipient_is_asked_in_the_mode_given_and_its_payments_come_back()
    {
        var payout = new EthereumTransfer(
            EthereumTxHash.Parse("0x" + new string('c', 64)), EthereumTransferKind.Native,
            EthereumAddress.Parse("0x" + new string('d', 40)), Recipient, AssetAmount.Parse(Asset.Ether, "0.2"), 0,
            new EthereumBlockReference(21_000_000, "0x" + new string('e', 64), Block(900_000).Time.AddMinutes(20)));
        _ethereum.Page = [payout];

        var found = await Lookup().FindAsync(_harness.Case, Graph(), LookupMode.AsNeeded);

        var asked = Assert.Single(_ethereum.Asked.DistinctBy(item => item.Address));
        Assert.Equal((Recipient, LookupMode.AsNeeded), asked);
        var arrival = Assert.Single(found.Arrivals);
        Assert.Equal(Recipient.ToChainAddress(), arrival.To);
        Assert.Equal(CrossChainTargetState.Read, Assert.Single(found.Targets).State);
        Assert.Empty(found.Values);
    }

    [Fact]
    public async Task A_recipient_that_cannot_be_reached_is_said_and_nothing_else_is_asked()
    {
        _ethereum.Status = LookupStatus.Offline;

        var found = await Lookup().FindAsync(_harness.Case, Graph(), LookupMode.AsNeeded);

        var target = Assert.Single(found.Targets);
        Assert.Equal(CrossChainTargetState.Unreachable, target.State);
        Assert.Contains("Utgången", target.Problem, StringComparison.Ordinal);
        Assert.Empty(found.Arrivals);
    }

    [Fact]
    public async Task Reading_only_the_case_says_what_is_not_fetched_yet()
    {
        _ethereum.Status = LookupStatus.NotInCase;

        var found = await Lookup().FindAsync(_harness.Case, Graph(), LookupMode.CaseOnly);

        Assert.Equal(CrossChainTargetState.NotFetched, Assert.Single(found.Targets).State);
        Assert.All(_ethereum.Asked, item => Assert.Equal(LookupMode.CaseOnly, item.Mode));
    }

    [Fact]
    public async Task Without_money_into_a_bridge_nothing_is_asked()
    {
        var graph = Graph() with { Nodes = [] };

        var found = await Lookup().FindAsync(_harness.Case, graph, LookupMode.AsNeeded);

        Assert.Same(CrossChainArrivals.Empty, found);
        Assert.Empty(_ethereum.Asked);
    }

    private CrossChainLookup Lookup() => new(new NoBitcoin(), _ethereum);

    /// <summary>A payment of 0.01 BTC into a swap whose memo names the recipient on Ethereum.</summary>
    private static TraceGraph Graph()
    {
        var memo = Encoding.ASCII.GetBytes($"=:ETH.ETH:{Recipient.Value}");
        var deposit = Tx("lookup-deposit").In(Wpkh("lookup-payer"), 1_010_000).Out(SwapIn, 1_000_000).Build();
        deposit = deposit with
        {
            Outputs =
            [
                .. deposit.Outputs,
                new BitcoinOutput(1, BitcoinScriptType.OpReturn, null, Satoshi.Zero,
                    Convert.ToHexStringLower([0x6a, (byte)memo.Length, .. memo]), null),
            ],
        };
        var end = new TrailEndHint(TrailEndReason.OtherChain, "egen v1", "Swap eller brygga (Syntetisk)");
        return new TraceGraph(
            [],
            [
                new GraphNode(
                    "address:" + SwapIn.Value, GraphNodeKind.Address, [SwapIn], 1, null, null, null, false, null,
                    null, NodeExpansion.Expanded, [end], new Dictionary<GraphLayout, NodePosition>()),
            ],
            [],
            new UtxoGraph(
                new Dictionary<Txid, GraphTransaction> { [deposit.Txid] = new(deposit, LookupStatus.FromCase, null) },
                new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [],
            [],
            []);
    }

    /// <summary>An Ethereum source that answers one page and records what it was asked.</summary>
    private sealed class FakeEthereum : IEthereumSource
    {
        public List<(EthereumAddress Address, LookupMode Mode)> Asked { get; } = [];

        public IReadOnlyList<EthereumTransfer> Page { get; set; } = [];

        public LookupStatus Status { get; set; } = LookupStatus.Fetched;

        public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
            ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
            LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
        {
            Asked.Add((address, mode));
            var page = Status == LookupStatus.Fetched
                ? new EthereumAddressPage(address, history, null, history == EthereumHistory.Transactions ? Page : [],
                    [], null, false, false)
                : null;
            return Task.FromResult(new BitcoinLookup<EthereumAddressPage>(Status, page, [], null));
        }

        public Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
            ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
            ICase @case, long height, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A Bitcoin source the search must never ask here: the memo names a recipient on Ethereum.</summary>
    private sealed class NoBitcoin : IBitcoinSource
    {
        public BitcoinNetwork Network => BitcoinNetwork.Mainnet;

        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Bitcoin is not asked.");

        public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
            ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
            ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
            ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
