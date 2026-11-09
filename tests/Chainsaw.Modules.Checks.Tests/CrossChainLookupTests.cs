using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The cross-chain search (<see cref="CrossChainLookup"/>): it reads the case first and asks the other chain's source
/// only for the recipient a swap's memo names, and nothing else (principle 4); not again within the minimum interval;
/// for at most eight recipients; including ether a contract paid out (a router); and it says honestly when the
/// recipient could not be read (principle 9).
/// </summary>
public sealed class CrossChainLookupTests : IDisposable
{
    private static readonly EthereumAddress Recipient = Account("recipient");

    private readonly Harness _harness = new();
    private readonly FakeEthereum _ethereum = new();
    private readonly FakeClock _clock = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Only_the_memos_recipient_is_asked_and_its_payments_come_back()
    {
        _ethereum.Pages[EthereumHistory.Transactions] = [Payout(EthereumTransferKind.Native, 'c')];

        var found = await Lookup().FindAsync(_harness.Case, Graph(Recipient), LookupMode.AsNeeded);

        Assert.All(_ethereum.Asked, item => Assert.Equal(Recipient, item.Address));
        Assert.Contains(_ethereum.Asked, item => item.Mode == LookupMode.AsNeeded);
        var arrival = Assert.Single(found.Arrivals);
        Assert.Equal(Recipient.ToChainAddress(), arrival.To);
        Assert.Equal(CrossChainTargetState.Read, Assert.Single(found.Targets).State);
        Assert.Empty(found.Values);
    }

    [Fact]
    public async Task Ether_a_router_paid_out_inside_a_transaction_is_read()
    {
        // THORChain pays ether out through its router's transferOut: an internal transaction, not the tx's value.
        _ethereum.Pages[EthereumHistory.Internal] = [Payout(EthereumTransferKind.Internal, 'f')];

        var found = await Lookup().FindAsync(_harness.Case, Graph(Recipient), LookupMode.AsNeeded);

        Assert.Equal(EthereumTransferKind.Internal, Assert.Single(found.Arrivals).Movement!.Kind);
        Assert.Contains(_ethereum.Asked, item => item.History == EthereumHistory.Internal);
    }

    [Fact]
    public async Task A_recipient_read_lately_is_not_asked_again()
    {
        _ethereum.CaseStatus = LookupStatus.FromCase;
        _ethereum.ReceivedUtc = _clock.UtcNow.AddMinutes(-5);

        await Lookup().FindAsync(_harness.Case, Graph(Recipient), LookupMode.AsNeeded);

        Assert.DoesNotContain(_ethereum.Asked, item => item.Mode == LookupMode.AsNeeded);
    }

    [Fact]
    public async Task A_recipient_read_long_ago_is_asked_again()
    {
        _ethereum.CaseStatus = LookupStatus.FromCase;
        _ethereum.ReceivedUtc = _clock.UtcNow - CrossChainLookup.MinimumInterval - TimeSpan.FromMinutes(1);

        await Lookup().FindAsync(_harness.Case, Graph(Recipient), LookupMode.AsNeeded);

        Assert.Contains(_ethereum.Asked, item => item.Mode == LookupMode.AsNeeded);
    }

    [Fact]
    public async Task At_most_eight_recipients_are_asked_but_every_one_is_read_from_the_case()
    {
        var recipients = Enumerable.Range(0, 10).Select(index => Account("many-" + index)).ToArray();

        var found = await Lookup().FindAsync(_harness.Case, Graph(recipients), LookupMode.AsNeeded);

        Assert.Equal(10, found.Targets.Count);
        Assert.Equal(10, _ethereum.Asked.Where(item => item.Mode == LookupMode.CaseOnly).DistinctBy(i => i.Address)
            .Count());
        Assert.Equal(CrossChainLookup.MostTargets, _ethereum.Asked
            .Where(item => item.Mode == LookupMode.AsNeeded).DistinctBy(item => item.Address).Count());
    }

    [Fact]
    public async Task A_litecoin_memo_is_read_from_litecoins_source()
    {
        var recipient = LitecoinAddress("ltc-recipient");
        var payout = Tx("ltc-payout").In(LitecoinAddress("ltc-pool"), 2_000_000).Out(recipient, 1_900_000).Build();
        var litecoin = new FakeLitecoin { Page = [payout] };

        var found = await new CrossChainLookup(new NoBitcoin(), _ethereum, litecoin: litecoin, clock: _clock)
            .FindAsync(_harness.Case, GraphOfMemos($"=:LTC.LTC:{recipient.Value}"), LookupMode.AsNeeded);

        var arrival = Assert.Single(found.Arrivals);
        Assert.Equal(Chain.Litecoin, arrival.Transaction.Chain);
        Assert.Equal(Asset.Litecoin, arrival.Amount.Asset);
        Assert.Equal(recipient.ToChainAddress(), arrival.To);
        Assert.Contains(litecoin.Asked, mode => mode == LookupMode.AsNeeded);
    }

    [Fact]
    public async Task A_recipient_that_cannot_be_read_stops_no_other()
    {
        _ethereum.Pages[EthereumHistory.Transactions] = [Payout(EthereumTransferKind.Native, 'c')];
        var litecoin = new FakeLitecoin { Failure = new FormatException("Ett svar som inte gick att läsa.") };
        var graph = GraphOfMemos(
            $"=:LTC.LTC:{LitecoinAddress("ltc-recipient").Value}", $"=:ETH.ETH:{Recipient.Value}");

        var found = await new CrossChainLookup(new NoBitcoin(), _ethereum, litecoin: litecoin, clock: _clock)
            .FindAsync(_harness.Case, graph, LookupMode.AsNeeded);

        Assert.Equal(CrossChainTargetState.Unreachable,
            found.Targets.Single(target => target.Address.Chain == Chain.Litecoin).State);
        Assert.Equal(Recipient.ToChainAddress(), Assert.Single(found.Arrivals).To);
    }

    [Fact]
    public async Task A_recipient_without_a_source_for_its_chain_is_said()
    {
        var found = await Lookup().FindAsync(
            _harness.Case, GraphOfMemos($"=:LTC.LTC:{LitecoinAddress("ltc-recipient").Value}"), LookupMode.AsNeeded);

        Assert.Contains("ingen källa", Assert.Single(found.Targets).Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recipient_that_cannot_be_reached_is_said()
    {
        _ethereum.Status = LookupStatus.Offline;

        var found = await Lookup().FindAsync(_harness.Case, Graph(Recipient), LookupMode.AsNeeded);

        var target = Assert.Single(found.Targets);
        Assert.Equal(CrossChainTargetState.Unreachable, target.State);
        Assert.Contains("Utgången", target.Problem, StringComparison.Ordinal);
        Assert.Empty(found.Arrivals);
    }

    [Fact]
    public async Task Reading_only_the_case_asks_no_source_and_says_what_is_not_fetched_yet()
    {
        var found = await Lookup().FindAsync(_harness.Case, Graph(Recipient), LookupMode.CaseOnly);

        Assert.Equal(CrossChainTargetState.NotFetched, Assert.Single(found.Targets).State);
        Assert.All(_ethereum.Asked, item => Assert.Equal(LookupMode.CaseOnly, item.Mode));
    }

    [Fact]
    public async Task Without_money_into_a_bridge_nothing_is_asked()
    {
        var graph = Graph(Recipient) with { Nodes = [] };

        var found = await Lookup().FindAsync(_harness.Case, graph, LookupMode.AsNeeded);

        Assert.Same(CrossChainArrivals.Empty, found);
        Assert.Empty(_ethereum.Asked);
    }

    private CrossChainLookup Lookup() => new(new NoBitcoin(), _ethereum, clock: _clock);

    private static EthereumAddress Account(string label) =>
        EthereumAddress.Parse("0x" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(label))
            .AsSpan(0, 20)));

    private static EthereumTransfer Payout(EthereumTransferKind kind, char digit) => new(
        EthereumTxHash.Parse("0x" + new string(digit, 64)), kind,
        EthereumAddress.Parse("0x" + new string('d', 40)), Recipient, AssetAmount.Parse(Asset.Ether, "0.2"), 0,
        new EthereumBlockReference(21_000_000, "0x" + new string('e', 64), Block(900_000).Time.AddMinutes(20)));

    /// <summary>A Litecoin address derived from a label.</summary>
    private static BitcoinAddress LitecoinAddress(string label) => BitcoinAddress.FromWitness(
        0, SHA256.HashData(Encoding.UTF8.GetBytes(label)).AsSpan(0, 20), BitcoinNetwork.Litecoin);

    /// <summary>One payment of 0.01 BTC into a swap per recipient, each with a memo naming it on Ethereum.</summary>
    private static TraceGraph Graph(params EthereumAddress[] recipients) =>
        GraphOfMemos([.. recipients.Select(recipient => $"=:ETH.ETH:{recipient.Value}")]);

    /// <summary>One payment of 0.01 BTC into a swap per memo.</summary>
    private static TraceGraph GraphOfMemos(params string[] memos)
    {
        var swapIn = Wpkh("lookup-swap-in");
        var transactions = memos.Select((text, index) =>
        {
            var memo = Encoding.ASCII.GetBytes(text);
            var deposit = Tx("lookup-deposit-" + index).In(Wpkh("lookup-payer"), 1_010_000).Out(swapIn, 1_000_000)
                .Build();
            return deposit with
            {
                Outputs =
                [
                    .. deposit.Outputs,
                    new BitcoinOutput(1, BitcoinScriptType.OpReturn, null, Satoshi.Zero,
                        Convert.ToHexStringLower([0x6a, (byte)memo.Length, .. memo]), null),
                ],
            };
        });
        var end = new TrailEndHint(TrailEndReason.OtherChain, "egen v1", "Swap eller brygga (Syntetisk)");
        return new TraceGraph(
            [],
            [
                new GraphNode(
                    "address:" + swapIn.Value, GraphNodeKind.Address, [swapIn], 1, null, null, null, false, null,
                    null, NodeExpansion.Expanded, [end], new Dictionary<GraphLayout, NodePosition>()),
            ],
            [],
            new UtxoGraph(
                transactions.ToDictionary(tx => tx.Txid, tx => new GraphTransaction(tx, LookupStatus.FromCase, null)),
                new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [],
            [],
            []);
    }

    /// <summary>An Ethereum source that answers one page per history and records what it was asked.</summary>
    private sealed class FakeEthereum : IEthereumSource
    {
        public List<(EthereumAddress Address, EthereumHistory History, LookupMode Mode)> Asked { get; } = [];

        public Dictionary<EthereumHistory, IReadOnlyList<EthereumTransfer>> Pages { get; } = [];

        /// <summary>Gets or sets what a source asked through the exit answers.</summary>
        public LookupStatus Status { get; set; } = LookupStatus.Fetched;

        /// <summary>Gets or sets what reading only the case answers.</summary>
        public LookupStatus CaseStatus { get; set; } = LookupStatus.NotInCase;

        /// <summary>Gets or sets when the case's answer was received, or null for none.</summary>
        public DateTimeOffset? ReceivedUtc { get; set; }

        public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
            ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
            LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
        {
            Asked.Add((address, history, mode));
            var status = mode == LookupMode.CaseOnly ? CaseStatus : Status;
            var page = status is LookupStatus.Fetched or LookupStatus.FromCase
                ? new EthereumAddressPage(address, history, null, Pages.GetValueOrDefault(history) ?? [], [], null,
                    false, false)
                : null;
            IReadOnlyList<ResponseOrigin> origins = ReceivedUtc is { } at && mode == LookupMode.CaseOnly
                ? [new ResponseOrigin(1, "test", new Uri("https://example.invalid/"), 200, at, at,
                    new OperatorProfile("Test", "Test", "Test"), new string('0', 64))]
                : [];
            return Task.FromResult(new BitcoinLookup<EthereumAddressPage>(status, page, origins, null));
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

    /// <summary>A Litecoin source that answers one page, or fails, and records how it was asked.</summary>
    private sealed class FakeLitecoin : ILitecoinSource
    {
        public List<LookupMode> Asked { get; } = [];

        public IReadOnlyList<BitcoinTransaction> Page { get; init; } = [];

        public Exception? Failure { get; init; }

        public BitcoinNetwork Network => BitcoinNetwork.Litecoin;

        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
            GetAddressTransactionsAsync(@case, address, after, LookupMode.AsNeeded, cancellationToken);

        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after, LookupMode mode,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(mode);
            if (Failure is not null)
            {
                throw Failure;
            }

            var page = mode == LookupMode.AsNeeded ? new BitcoinAddressPage(address, null, Page, null) : null;
            var status = page is null ? LookupStatus.NotInCase : LookupStatus.Fetched;
            return Task.FromResult(new BitcoinLookup<BitcoinAddressPage>(status, page, [], null));
        }

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
