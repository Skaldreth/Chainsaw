using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit for round amounts in fiat (<c>round-fiat v1</c>): synthetic transactions valued with stored rates of
/// 50 000 EUR and 550 000 SEK for a bitcoin. What it finds, what it leaves alone, that it says which rates it used and
/// which are missing, and that it never asks for a rate the case does not hold.
/// </summary>
public sealed class RoundFiatTests
{
    /// <summary>A bitcoin in euro at every block, as the stored crypto rate says.</summary>
    private const decimal EuroPerBitcoin = 50_000m;

    /// <summary>A euro in kronor, as the stored day rate says.</summary>
    private const decimal KronorPerEuro = 11m;

    /// <summary>5 000,0005 SEK: round in kronor, 454,55 EUR is not round in euro.</summary>
    private const long FiveThousandKronor = 909_091;

    /// <summary>1 499,9985 SEK: round in kronor.</summary>
    private const long FifteenHundredKronor = 272_727;

    /// <summary>6 790,12 SEK and 617,28 EUR: round in neither.</summary>
    private const long OddChange = 1_234_567;

    /// <summary>100 EUR exactly (1 100 SEK, not round in kronor).</summary>
    private const long HundredEuro = 200_000;

    private static readonly OperatorProfile Fetcher = new("Testa Testsson", "Utredare", "Enheten");

    // ---- What is round ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(5_000.0005, true, 5_000)]
    [InlineData(9_980, true, 10_000)]
    [InlineData(1_500, true, 1_500)]
    [InlineData(25_100, true, 25_000)]
    [InlineData(5_030, false, 0)]
    [InlineData(1_520, false, 0)]
    [InlineData(3_500, false, 0)]
    [InlineData(99.9, false, 0)]
    public void A_round_amount_lies_within_half_a_percent_of_a_round_number(double value, bool round, double target)
    {
        var hit = RoundFiatCheck.Round((decimal)value, FiatCurrency.Sek);

        Assert.Equal(round, hit is not null);
        if (hit is not null)
        {
            Assert.Equal((decimal)target, hit.Target);
        }
    }

    [Fact]
    public void Small_amounts_are_never_round_payments()
    {
        Assert.Null(RoundFiatCheck.Round(50m, FiatCurrency.Sek));
        Assert.Null(RoundFiatCheck.Round(5m, FiatCurrency.Eur));
        Assert.NotNull(RoundFiatCheck.Round(10m, FiatCurrency.Usd));
    }

    // ---- What it finds ----------------------------------------------------------------------------------------

    [Fact]
    public void A_payment_round_in_kronor_points_at_the_odd_output_as_change()
    {
        var tx = Payment("rf-sek", FiveThousandKronor, OddChange);

        var finding = Assert.Single(new RoundFiatCheck().Run(InputWith(tx, Sek(tx, 1), Eur(tx, 1))));

        Assert.Equal(tx.Txid.Value, finding.Key);
        Assert.Equal(CheckKind.Check, finding.Kind);
        Assert.Equal(AreaSort.Wallet, finding.Area);
        Assert.Equal("Jämnt belopp i SEK", finding.Title);
        Assert.Contains("Output 0 ≈ 5 000 SEK", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("output 1 trolig växel", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("round-fiat v1", finding.Why, StringComparison.Ordinal);
        Assert.Contains("inte ett faktum", finding.Why, StringComparison.Ordinal);
        Assert.Contains("Ärendet har ingen kurs i USD", finding.Why, StringComparison.Ordinal);
        Assert.Contains(tx.Outputs[1].Address!, finding.Addresses);
        Assert.Contains(tx.Inputs[0].Address!, finding.Addresses);
        Assert.DoesNotContain(tx.Outputs[0].Address!, finding.Addresses);
        Assert.Equal([tx.Txid], finding.Basis.Select(basis => basis.Txid));
        Assert.Equal(tx.Block, finding.Basis[0].Block);
    }

    [Fact]
    public void Every_rate_used_is_named_with_its_source_and_hash_once()
    {
        var tx = Payment("rf-rates", FiveThousandKronor, OddChange);

        var finding = Assert.Single(new RoundFiatCheck().Run(InputWith(tx, Sek(tx, 1), Eur(tx, 1))));

        // SEK rests on the euro candle and the day rate; EUR on the same candle, named once.
        Assert.Equal(["bitstamp", "riksbank"], finding.Rates.Select(rate => rate.Source));
        Assert.Equal([1L, 2L], finding.Rates.Select(rate => rate.Sequence));
        Assert.All(
            finding.Rates,
            rate => Assert.Contains($"SHA-256 {rate.Sha256}", finding.Why, StringComparison.Ordinal));
        Assert.Contains("Bitstamp", finding.Rates[0].Reading, StringComparison.Ordinal);
        Assert.Contains("SEKEURPMI", finding.Rates[1].Reading, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_round_payments_and_one_odd_output_are_a_batch_with_change()
    {
        var tx = Tx("rf-batch")
            .In(Wpkh("rf-batch-payer"), 3_000_000)
            .Out(Pkh("rf-batch-a"), FiveThousandKronor)
            .Out(Pkh("rf-batch-b"), FifteenHundredKronor)
            .Out(Wpkh("rf-batch-change"), OddChange)
            .Build();

        var finding = Assert.Single(new RoundFiatCheck().Run(InputWith(tx, Sek(tx, 1), Eur(tx, 1))));

        Assert.Equal("Jämna belopp i fiat", finding.Title);
        Assert.Contains("2 outputs jämna, output 2 trolig växel", finding.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_payment_round_only_in_euro_counts_with_the_euro_rate_alone()
    {
        var tx = Payment("rf-eur", 1_000_000, OddChange);

        var finding = Assert.Single(new RoundFiatCheck().Run(InputWith(tx, Eur(tx, 1))));

        Assert.Equal("Jämnt belopp i EUR", finding.Title);
        Assert.Contains("Ärendet har ingen kurs i SEK eller USD", finding.Why, StringComparison.Ordinal);
        Assert.Equal(["bitstamp"], finding.Rates.Select(rate => rate.Source));
    }

    // ---- What it leaves alone -----------------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_found_when_no_output_or_every_output_is_round()
    {
        var odd = Payment("rf-odd", 1_111_111, OddChange);
        var both = Payment("rf-both", FiveThousandKronor, HundredEuro);
        var analysis = new RoundFiatCheck();

        Assert.Empty(analysis.Run(InputWith(odd, Sek(odd, 1), Eur(odd, 1))));
        Assert.Empty(analysis.Run(InputWith(both, Sek(both, 1), Eur(both, 1))));
    }

    [Fact]
    public void Random_amounts_give_findings_by_chance_at_about_the_rate_the_guide_says()
    {
        // A fixed sequence of amounts between 0.001 and 0.1 BTC, spread evenly on a log scale, two outputs a tx.
        var random = new Random(20261109);
        var analysis = new RoundFiatCheck();
        var found = 0;
        const int Count = 2_000;
        for (var i = 0; i < Count; i++)
        {
            var paid = (long)Math.Pow(10, 5 + (2 * random.NextDouble()));
            var change = (long)Math.Pow(10, 5 + (2 * random.NextDouble()));
            var tx = Payment($"rf-random-{i}", paid, change);
            found += analysis.Run(InputWith(tx, Sek(tx, 1), Eur(tx, 1))).Count;
        }

        // About one amount in twenty is round in each currency, so about one tx in six has exactly one round output:
        // a weak sign, which is why it never merges and the Guide says so.
        Assert.InRange(found / (double)Count, 0.10, 0.22);
    }

    [Fact]
    public void A_transaction_without_a_stored_rate_is_not_weighed_and_no_rate_is_guessed()
    {
        var tx = Payment("rf-none", FiveThousandKronor, OddChange);

        Assert.Empty(new RoundFiatCheck().Run(InputWith(tx)));
    }

    [Fact]
    public void An_unconfirmed_transaction_or_a_single_output_is_never_priced()
    {
        var unconfirmed = Tx("rf-mempool", height: null)
            .In(Wpkh("rf-mempool-payer"), 3_000_000)
            .Out(Pkh("rf-mempool-shop"), FiveThousandKronor)
            .Out(Wpkh("rf-mempool-change"), OddChange)
            .Build();
        var single = Tx("rf-single")
            .In(Wpkh("rf-single-payer"), 1_000_000)
            .Out(Pkh("rf-single-shop"), FiveThousandKronor)
            .Build();
        var rates = new FakeCaseRates();

        var prices = CasePrices.Read(rates, null!, [unconfirmed, single]);

        Assert.Empty(prices);
        Assert.Empty(rates.Asked);
    }

    // ---- Reading the case's rates -------------------------------------------------------------------------------

    [Fact]
    public void The_hour_is_taken_before_the_minute_and_the_day_and_only_missing_rates_are_asked_again()
    {
        var tx = Payment("rf-hour", FiveThousandKronor, OddChange);
        var rates = new FakeCaseRates();
        rates.Add(tx, FiatCurrency.Eur, PriceResolution.Minute, Eur(tx, 7));
        rates.Add(tx, FiatCurrency.Eur, PriceResolution.Hour, Eur(tx, 1));
        rates.Add(tx, FiatCurrency.Sek, PriceResolution.Day, Sek(tx, 3));

        var prices = CasePrices.Read(rates, null!, [tx])[tx.Txid];

        Assert.Equal([FiatCurrency.Sek, FiatCurrency.Eur], prices.Select(price => price.Currency));
        Assert.Equal(1L, prices[1].CryptoRate.Origin.Sequence);
        Assert.Equal(3L, prices[0].CryptoRate.Origin.Sequence);
        Assert.Equal(3, rates.Reads);
        Assert.Equal(3 + 2 + 2, rates.Asked.Count);
    }

    [Fact]
    public void The_service_reads_the_case_rates_seals_the_rates_with_the_finding_and_agrees_with_the_change()
    {
        var tx = Payment("rf-service", FiveThousandKronor, OddChange);
        var rates = new FakeCaseRates();
        rates.Add(tx, FiatCurrency.Sek, PriceResolution.Hour, Sek(tx, 1));
        rates.Add(tx, FiatCurrency.Eur, PriceResolution.Hour, Eur(tx, 1));
        using var harness = new Harness(services => services.AddSingleton<ICaseRates>(rates));

        var first = harness.Run([tx]);
        var second = harness.Run([tx]);

        var check = Assert.Single(first.Checks, item => item.Analysis.Name == RoundFiatCheck.Name);
        Assert.Contains("stöder klustringens change-address v2", check.Finding.Why, StringComparison.Ordinal);
        Assert.Equal(2, check.Finding.Rates.Count);
        Assert.Empty(first.Problems);
        Assert.Equal(0, second.Sealed);
        var again = Assert.Single(second.Checks, item => item.Analysis.Name == RoundFiatCheck.Name);
        Assert.Equal(check.Finding.Rates, again.Finding.Rates);
        Assert.Equal(check.Finding.Why, again.Finding.Why);
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public void A_reader_that_fails_is_said_and_the_other_analyses_still_run()
    {
        var tx = Payment("rf-fails", FiveThousandKronor, OddChange);
        var failing = new FakeCaseRates { Fails = true };
        using var harness = new Harness(services => services.AddSingleton<ICaseRates>(failing));

        var run = harness.Run([tx]);

        Assert.Contains(run.Problems, problem => problem.Contains("round-fiat v1", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Checks, item => item.Analysis.Name == RoundFiatCheck.Name);
    }

    [Fact]
    public void A_finding_with_rates_is_sealed_and_read_back_with_them_and_others_are_written_as_before()
    {
        var tx = Payment("rf-codec", FiveThousandKronor, OddChange);
        var finding = Assert.Single(new RoundFiatCheck().Run(InputWith(tx, Sek(tx, 1), Eur(tx, 1))));

        var json = FindingCodec.Encode(finding);
        var back = FindingCodec.Decode(json);

        Assert.Equal(finding.Rates, back.Rates);
        Assert.Equal(json, FindingCodec.Encode(back));
        Assert.DoesNotContain("Rates", FindingCodec.Encode(finding with { Rates = [] }), StringComparison.Ordinal);
    }

    // ---- The synthetic transactions and rates ---------------------------------------------------------------

    /// <summary>A payment from a P2WPKH wallet to a P2PKH shop, with the change to a fresh P2WPKH address.</summary>
    private static BitcoinTransaction Payment(string label, long paid, long change) =>
        Tx(label)
            .In(Wpkh(label + "-payer"), paid + change + 6_342)
            .Out(Pkh(label + "-shop"), paid)
            .Out(Wpkh(label + "-change"), change)
            .Build();

    /// <summary>The input of one transaction with the prices the case holds at its block.</summary>
    private static CheckInput InputWith(BitcoinTransaction transaction, params Price[] prices) =>
        new CheckInput([transaction], [], new Dictionary<Txid, TransactionConclusions>())
        {
            Prices = prices.Length == 0
                ? new Dictionary<Txid, IReadOnlyList<Price>>()
                : new Dictionary<Txid, IReadOnlyList<Price>> { [transaction.Txid] = prices },
        };

    /// <summary>The euro price at a transaction's block, from a sealed Bitstamp response.</summary>
    private static Price Eur(BitcoinTransaction transaction, long sequence) =>
        new(transaction.Block!.Time, FiatCurrency.Eur, Candle(transaction, sequence), null);

    /// <summary>The kronor price at a transaction's block: the euro candle times the Riksbank's day rate.</summary>
    private static Price Sek(BitcoinTransaction transaction, long sequence)
    {
        var day = DateOnly.FromDateTime(transaction.Block!.Time.UtcDateTime);
        var fiat = new FiatRate(
            FiatCurrency.Eur, FiatCurrency.Sek, KronorPerEuro, day, day, "SEKEURPMI",
            Origin(sequence + 1, "riksbank"))
        {
            SourceName = "Riksbanken",
        };
        return new Price(transaction.Block.Time, FiatCurrency.Sek, Candle(transaction, sequence), fiat);
    }

    /// <summary>The hour's candle at a transaction's block.</summary>
    private static CryptoRate Candle(BitcoinTransaction transaction, long sequence)
    {
        var start = PriceResolution.Hour.PeriodStart(transaction.Block!.Time);
        return new CryptoRate(
            Chain.Bitcoin, FiatCurrency.Eur, PriceResolution.Hour, start, start.AddHours(1), "BTC/EUR",
            EuroPerBitcoin, EuroPerBitcoin, EuroPerBitcoin, EuroPerBitcoin, 1m, Origin(sequence, "bitstamp"))
        {
            SourceName = "Bitstamp",
        };
    }

    /// <summary>A sealed response's origin with a hash made from its number.</summary>
    private static RateOrigin Origin(long sequence, string source) => new(
        sequence,
        source,
        new Uri($"https://{source}.example/{sequence}"),
        200,
        new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.FromHours(1)),
        Fetcher,
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(sequence))));

    /// <summary>
    /// Holds prices by block time, currency and resolution, as a case would, and counts what it is asked; it never
    /// sends anything.
    /// </summary>
    private sealed class FakeCaseRates : ICaseRates
    {
        private readonly Dictionary<(DateTimeOffset At, FiatCurrency Currency, PriceResolution Resolution), Price>
            _prices = [];

        public List<PriceRequest> Asked { get; } = [];

        public int Reads { get; private set; }

        public bool Fails { get; init; }

        public void Add(BitcoinTransaction transaction, FiatCurrency currency, PriceResolution resolution, Price price) =>
            _prices[(transaction.Block!.Time, currency, resolution)] = price;

        public IReadOnlyList<PriceLookup<Price>> ReadStored(ICase @case, IReadOnlyList<PriceRequest> requests)
        {
            if (Fails)
            {
                throw new InvalidOperationException("The case cannot be read.");
            }

            Reads++;
            Asked.AddRange(requests);
            return
            [
                .. requests.Select(request =>
                    _prices.TryGetValue((request.At, request.Currency!.Value, request.Resolution!.Value), out var price)
                        ? new PriceLookup<Price>(PriceLookupStatus.FromCase, price, price.Origins, null)
                        : new PriceLookup<Price>(PriceLookupStatus.NotInCase, null, [], "Inte i ärendet.")),
            ];
        }
    }
}
