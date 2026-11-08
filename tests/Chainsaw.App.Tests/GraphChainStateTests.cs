using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The real <see cref="GraphChainState"/> over fake sources (finding I3 of the GR9a review): the balance of a cluster,
/// its cap and its failures, today's rate with its fall back from the hour to the day, Ethereum's known tokens only,
/// and the marks read from the case without asking.
/// </summary>
public sealed class GraphChainStateTests
{
    /// <summary>Now, in the tests.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 14, 32, 0, TimeSpan.Zero);

    private readonly FakeBitcoin _bitcoin = new();

    private readonly FakeEthereum _ethereum = new();

    private readonly FakePrices _prices = new();

    private readonly TestCase _case = new("2026-0117");

    private GraphChainState State => new(_bitcoin, _ethereum, _prices);

    [Fact]
    public async Task A_clusters_balance_adds_its_addresses_and_takes_the_oldest_time()
    {
        _bitcoin.Funded[K1] = (5_000_000, 1_000_000);
        _bitcoin.Funded[K2] = (2_000_000, 0);
        _bitcoin.Received[K2] = Now.AddMinutes(-5);
        _bitcoin.MempoolIn[K1] = 10_000;

        var holdings = await State.HoldingsAsync(_case, Node(KKey, GraphNodeKind.Cluster, [K1, K2], hop: 1), default);

        Assert.Equal(6_000_000, (long)Assert.Single(holdings.Held).Units);
        Assert.Equal(Now.AddMinutes(-5), holdings.FetchedAt);
        Assert.Null(holdings.Problem);
        Assert.Equal(10_000, (long)holdings.Pending!.Value.Units);
        Assert.Equal(921_344, holdings.Tip!.Height);
    }

    [Fact]
    public async Task Only_outgoing_money_in_the_mempool_waits_as_a_negative_amount()
    {
        _bitcoin.Funded[V] = (5_000_000, 0);
        _bitcoin.MempoolOut[V] = 1_000_000;

        var holdings = await State.HoldingsAsync(_case, Node(VKey, GraphNodeKind.Address, [V], hop: 0), default);
        var card = HoldingsCards.Build(
            VKey, holdings, new Dictionary<Asset, Price>(), CardRateKind.FromCase, null, Now, TimeZoneInfo.Utc);

        Assert.Equal(-1_000_000, (long)holdings.Pending!.Value.Units);
        Assert.Equal(5_000_000, (long)holdings.Held[0].Units);
        Assert.StartsWith(HoldingsCards.PendingPrefix + "−0,0100 BTC", card.Rows[0].Pending, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_large_cluster_is_asked_for_its_first_ten_addresses_and_says_so()
    {
        var addresses = Enumerable.Range(20, 12).Select(Address).ToList();
        foreach (var address in addresses)
        {
            _bitcoin.Funded[address] = (100, 0);
        }

        var holdings = await State.HoldingsAsync(_case, Node(KKey, GraphNodeKind.Cluster, addresses, hop: 1), default);

        Assert.Equal(GraphChainState.MostAddresses, _bitcoin.Summaries);
        Assert.Equal(1_000, (long)holdings.Held[0].Units);
        Assert.Contains("12 adresser", holdings.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_address_that_cannot_be_had_fails_the_balance_with_why()
    {
        _bitcoin.Funded[K1] = (5_000_000, 0);
        _bitcoin.Failing.Add(K2);

        var holdings = await State.HoldingsAsync(_case, Node(KKey, GraphNodeKind.Cluster, [K1, K2], hop: 1), default);

        Assert.False(holdings.HasBalance);
        Assert.Equal("Källan bad Chainsaw vänta.", holdings.Problem);
    }

    [Fact]
    public async Task Todays_rate_falls_back_from_the_hour_to_the_day_and_leaves_out_what_has_none()
    {
        _prices.NotYet = Now.AddHours(-1);

        var rates = await State.TodayRatesAsync(_case, [Asset.Bitcoin, Asset.Ether], Now, default);

        Assert.Equal([Now.AddHours(-1), Now.AddDays(-1), Now.AddHours(-1)], _prices.Asked);
        Assert.Equal(Now.AddDays(-1), rates[Asset.Bitcoin].At);
        Assert.False(rates.ContainsKey(Asset.Ether));
    }

    [Fact]
    public async Task Ethereum_shows_ether_and_only_the_tokens_whose_issuer_is_known()
    {
        var node = Node("account:a", GraphNodeKind.Address, [], hop: 0) with
        {
            Chain = Chain.Ethereum,
            Accounts = [new ChainAddress(Chain.Ethereum, "0x" + new string('1', 40))],
        };

        var holdings = await State.HoldingsAsync(_case, node, default);

        Assert.Equal([Asset.Ether, KnownTokens.EthereumUsdt], holdings.Held.Select(amount => amount.Asset));
        Assert.Equal(23_118_406, holdings.Tip!.Height);
    }

    [Fact]
    public async Task The_marks_are_read_from_the_case_without_asking()
    {
        var reading = await State.ReadMarksAsync(_case, default);

        Assert.Equal(["changes", "tip:CaseOnly"], _bitcoin.Calls);
        Assert.Equal(921_000, reading.Tip!.Height);
    }

    /// <summary>A Bitcoin source answering from what the test set, counting what it was asked.</summary>
    private sealed class FakeBitcoin : IBitcoinSource
    {
        public Dictionary<BitcoinAddress, (long Funded, long Spent)> Funded { get; } = [];

        public Dictionary<BitcoinAddress, long> MempoolIn { get; } = [];

        public Dictionary<BitcoinAddress, long> MempoolOut { get; } = [];

        public Dictionary<BitcoinAddress, DateTimeOffset> Received { get; } = [];

        public HashSet<BitcoinAddress> Failing { get; } = [];

        public List<string> Calls { get; } = [];

        public int Summaries { get; private set; }

        public BitcoinNetwork Network => BitcoinNetwork.Mainnet;

        public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
            ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default)
        {
            Summaries++;
            if (Failing.Contains(address))
            {
                return Task.FromResult(new BitcoinLookup<BitcoinAddressSummary>(
                    LookupStatus.Failed, null, [], "Källan bad Chainsaw vänta."));
            }

            var (funded, spent) = Funded.GetValueOrDefault(address);
            var confirmed = new BitcoinAddressStats(1, 1, new Satoshi(funded), 0, new Satoshi(spent));
            var mempool = new BitcoinAddressStats(0, 0, new Satoshi(MempoolIn.GetValueOrDefault(address)), 0,
                new Satoshi(MempoolOut.GetValueOrDefault(address)));
            var origin = new ResponseOrigin(Summaries, "esplora", new Uri("https://e.invalid/"), 200, Now,
                Received.GetValueOrDefault(address, Now), new OperatorProfile("T", "T", "T"), new string('0', 64));
            return Task.FromResult(new BitcoinLookup<BitcoinAddressSummary>(
                LookupStatus.Fetched, new BitcoinAddressSummary(address, confirmed, mempool), [origin], null));
        }

        public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
        {
            Calls.Add("tip");
            return Task.FromResult(new BitcoinLookup<ChainTip>(
                LookupStatus.Fetched, new ChainTip(921_344, new string('0', 64), Now), [], null));
        }

        public Task<BitcoinLookup<ChainTip>> GetTipAsync(
            ICase @case, LookupMode mode, CancellationToken cancellationToken = default)
        {
            Calls.Add("tip:" + mode);
            return Task.FromResult(new BitcoinLookup<ChainTip>(
                LookupStatus.FromCaseMayBeOutdated, new ChainTip(921_000, new string('0', 64), Now), [], null));
        }

        public Task<IReadOnlyList<BlockChange>> GetBlockChangesAsync(
            ICase @case, CancellationToken cancellationToken = default)
        {
            Calls.Add("changes");
            return Task.FromResult<IReadOnlyList<BlockChange>>([]);
        }

        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
            ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
            ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>An Ethereum source with ether, Tether, a made-up USDT and a spam token.</summary>
    private sealed class FakeEthereum : IEthereumSource
    {
        public Task<BitcoinLookup<EthereumBalance>> GetBalanceAsync(
            ICase @case, EthereumAddress address, CancellationToken cancellationToken = default)
        {
            var fake = Asset.Token(Chain.Ethereum, "0x" + new string('8', 40), "USDT", 6);
            var spam = Asset.Token(Chain.Ethereum, "0x" + new string('9', 40), "GRATIS", 18);
            var balance = new EthereumBalance(address, new AssetAmount(Asset.Ether, 1_000_000_000_000_000),
            [
                new AssetAmount(KnownTokens.EthereumUsdt, 12_450_000_000),
                new AssetAmount(fake, 99_000_000),
                new AssetAmount(spam, 1),
            ]);
            return Task.FromResult(new BitcoinLookup<EthereumBalance>(LookupStatus.Fetched, balance, [], null));
        }

        public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BitcoinLookup<ChainTip>(
                LookupStatus.Fetched, new ChainTip(23_118_406, "0x" + new string('0', 64), Now), [], null));

        public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
            ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
            LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
            ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
            ICase @case, long height, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Rates for Bitcoin only; the hour that has not ended yet has none.</summary>
    private sealed class FakePrices : IPriceService
    {
        public DateTimeOffset? NotYet { get; set; }

        public List<DateTimeOffset> Asked { get; } = [];

        public RateSummary Summarize(ICase @case) => throw new NotSupportedException();

        public Task<PriceLookup<Price>> GetPriceAsync(
            ICase @case, PriceRequest request, PriceLookupMode mode = PriceLookupMode.AsNeeded,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(request.At);
            if (request.Chain != Chain.Bitcoin)
            {
                return Task.FromResult(new PriceLookup<Price>(PriceLookupStatus.Unsupported, null, [], "nej"));
            }

            if (request.At == NotYet)
            {
                return Task.FromResult(new PriceLookup<Price>(PriceLookupStatus.NotAvailable, null, [], "inte än"));
            }

            var origin = new RateOrigin(1, "test", new Uri("https://rates.invalid/"), 200, request.At, request.At,
                new OperatorProfile("T", "T", "T"), new string('0', 64));
            var crypto = new CryptoRate(Chain.Bitcoin, FiatCurrency.Sek, PriceResolution.Hour, request.At,
                request.At.AddHours(1), "test", 1m, 1m, 1m, 1m, 1m, origin) { Asset = Asset.Bitcoin };
            return Task.FromResult(new PriceLookup<Price>(
                PriceLookupStatus.Fetched, new Price(request.At, FiatCurrency.Sek, crypto, null), [origin], null));
        }
    }
}
