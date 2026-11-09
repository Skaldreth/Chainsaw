using System.Security.Cryptography;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Tests;

/// <summary>Pricing a known token (USDT and USDC on Ethereum) by its asset, through the price service.</summary>
public sealed class TokenServiceTests : IDisposable
{
    private const string UsdtSubject = "Ethereum:0xdac17f958d2ee523a2206206994597c13d831ec7-USD/3600/1699999200";

    private const string UsdcSubject = "Ethereum:0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48-USD/3600/1699999200";

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Prices_usdt_in_dollars_from_its_own_market()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdtHour, "bitstamp-usdtusd-hour.json");

        var lookup = await PriceAsync(KnownTokens.EthereumUsdt, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        var price = lookup.Value!;
        Assert.Equal(KnownTokens.EthereumUsdt, price.Asset);
        Assert.Equal(KnownTokens.EthereumUsdt, price.CryptoRate.Asset);
        Assert.Equal(Chain.Ethereum, price.Chain);
        Assert.Equal(0.9998m, price.UnitPrice);
        Assert.Equal("USDT/USD", price.CryptoRate.Market);
        Assert.Equal(182345.5m, price.CryptoRate.Volume);
        Assert.Null(price.FiatRate);
        Assert.Equal(
            ["1 USDT = 0,9998 USD: Bitstamp, USDT/USD, sista priset för 2023-11-14 22:00–23:00 (UTC)."],
            price.Steps);
        Assert.Equal([Fixtures.BitstampUsdtHour], _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Prices_usdc_in_dollars()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdcHour, "bitstamp-usdcusd-hour.json");

        var lookup = await PriceAsync(KnownTokens.EthereumUsdc, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        Assert.Equal(1.0001m, lookup.Value!.UnitPrice);
        Assert.Equal(KnownTokens.EthereumUsdc, lookup.Value.Asset);
        Assert.StartsWith("1 USDC = 1,0001 USD: Bitstamp, USDC/USD", lookup.Value.Steps[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prices_usdt_in_kronor_through_dollars_and_the_riksbank()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdtHour, "bitstamp-usdtusd-hour.json");
        _harness.Http.Answer(Fixtures.RiksbankUsdNovember, "riksbank-sekusdpmi.json");

        var lookup = await PriceAsync(KnownTokens.EthereumUsdt, currency: null);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        var price = lookup.Value!;
        Assert.Equal(FiatCurrency.Sek, price.Currency);
        Assert.Equal(10.5108974m, price.UnitPrice);
        Assert.Equal(10.513m, price.FiatRate!.PerUnit);
        Assert.Equal("1 USDT = 0,9998 × 10,513 = 10,5108974 SEK.", price.Steps[^1]);
        Assert.Equal(
            [Fixtures.BitstampUsdtHour, Fixtures.RiksbankUsdNovember],
            _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Prices_usdc_in_kronor_through_dollars_and_the_riksbank()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdcHour, "bitstamp-usdcusd-hour.json");
        _harness.Http.Answer(Fixtures.RiksbankUsdNovember, "riksbank-sekusdpmi.json");

        var lookup = await PriceAsync(KnownTokens.EthereumUsdc, FiatCurrency.Sek);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        Assert.Equal(10.5140513m, lookup.Value!.UnitPrice);
        Assert.Equal("1 USDC = 1,0001 × 10,513 = 10,5140513 SEK.", lookup.Value.Steps[^1]);
    }

    [Fact]
    public async Task Seals_the_token_rate_with_source_hash_and_a_subject_named_by_contract()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdtHour, "bitstamp-usdtusd-hour.json");

        var lookup = await PriceAsync(KnownTokens.EthereumUsdt, FiatCurrency.Usd);

        var row = Assert.Single(_harness.RawRows());
        Assert.Equal("bitstamp", row.Source);
        Assert.Equal("crypto.candle", row.Kind);
        Assert.Equal(UsdtSubject, row.Subject);
        Assert.Equal(Fixtures.BitstampUsdtHour, row.Url);
        Assert.Equal(Fixtures.Bytes("bitstamp-usdtusd-hour.json"), row.Body);
        Assert.Equal(SHA256.HashData(row.Body), row.Sha256);
        Assert.Equal(Convert.ToHexStringLower(row.Sha256), lookup.Origins[0].Sha256);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Keeps_the_token_rate_apart_from_the_chains_coin()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdtHour, "bitstamp-usdtusd-hour.json");
        _harness.Http.Answer(Fixtures.BitstampUsdcHour, "bitstamp-usdcusd-hour.json");
        _harness.Http.AnswerText(
            Fixtures.BitstampEthHour,
            $$$"""
            {"data": {"pair": "ETH/USD", "ohlc": [{"timestamp": "{{{Fixtures.HourStart}}}", "open": "2000",
            "high": "2100", "low": "1900", "close": "2050.5", "volume": "3"}]}}
            """);

        var ether = await _harness.Prices.GetPriceAsync(
            _harness.Case, new PriceRequest(Chain.Ethereum, Fixtures.At) { Currency = FiatCurrency.Usd });
        var usdt = await PriceAsync(KnownTokens.EthereumUsdt, FiatCurrency.Usd);
        var usdc = await PriceAsync(KnownTokens.EthereumUsdc, FiatCurrency.Usd);

        Assert.Equal(2050.5m, ether.Value!.UnitPrice);
        Assert.Equal(Asset.Ether, ether.Value.Asset);
        Assert.Equal(0.9998m, usdt.Value!.UnitPrice);
        Assert.Equal(1.0001m, usdc.Value!.UnitPrice);
        Assert.Equal(
            ["ETH-USD/3600/1699999200", UsdtSubject, UsdcSubject],
            _harness.RawRows().Select(row => row.Subject));
        Assert.Equal(3, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Answers_the_token_from_the_case_offline()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdtHour, "bitstamp-usdtusd-hour.json");
        _harness.Http.Answer(Fixtures.RiksbankUsdNovember, "riksbank-sekusdpmi.json");
        var first = await PriceAsync(KnownTokens.EthereumUsdt, FiatCurrency.Sek);
        _harness.Http.Connected = false;

        var again = await PriceAsync(KnownTokens.EthereumUsdt, FiatCurrency.Sek);
        var request = new PriceRequest(Chain.Ethereum, Fixtures.At)
        {
            Asset = KnownTokens.EthereumUsdt,
            Currency = FiatCurrency.Sek,
        };
        var caseOnly = await _harness.Prices.GetPriceAsync(_harness.Case, request, PriceLookupMode.CaseOnly);
        var ether = await _harness.Prices.GetPriceAsync(
            _harness.Case, new PriceRequest(Chain.Ethereum, Fixtures.At) { Currency = FiatCurrency.Usd });

        Assert.Equal(PriceLookupStatus.FromCase, again.Status);
        Assert.Equal(first.Value!.UnitPrice, again.Value!.UnitPrice);
        Assert.Equal(first.Origins, again.Origins);
        Assert.Equal(KnownTokens.EthereumUsdt, again.Value.Asset);
        Assert.Equal(PriceLookupStatus.FromCase, caseOnly.Status);
        Assert.Equal(PriceLookupStatus.Offline, ether.Status);
        Assert.Equal(2, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Shows_a_known_token_with_chainsaws_own_symbol_whatever_the_caller_spelled()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdtHour, "bitstamp-usdtusd-hour.json");
        var spelled = Asset.Token(Chain.Ethereum, "0xDAC17F958D2EE523A2206206994597C13D831EC7", "Tether USD", 6);

        var lookup = await PriceAsync(spelled, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        Assert.Equal("USDT", lookup.Value!.Asset.Symbol);
        Assert.StartsWith("1 USDT = ", lookup.Value.Steps[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_without_a_source_has_no_price_and_nothing_is_sent()
    {
        var impostor = Asset.Token(Chain.Tron, "T" + new string('1', 33), "USDT", 6);
        var lookup = await _harness.Prices.GetPriceAsync(
            _harness.Case, new PriceRequest(Chain.Tron, Fixtures.At) { Asset = impostor });

        Assert.Equal(PriceLookupStatus.Unsupported, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.StartsWith(
            "Ingen kurs: ingen källa anger kursen för USDT", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(lookup.Origins);
        Assert.Empty(_harness.Http.Sent);
        Assert.Empty(_harness.RawRows());
    }

    [Fact]
    public async Task A_token_called_usdt_with_another_contract_is_not_taken_for_usdt()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdtHour, "bitstamp-usdtusd-hour.json");
        var impostor = Asset.Token(Chain.Ethereum, "0x1111111111111111111111111111111111111111", "USDT", 6);

        var lookup = await PriceAsync(impostor, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Unsupported, lookup.Status);
        Assert.Equal(
            "Ingen kurs: ingen källa anger kursen för USDT (0x1111111111111111111111111111111111111111). " +
            "Inget skickades.",
            lookup.Problem);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task A_token_in_a_currency_its_source_does_not_quote_is_refused()
    {
        using var harness = new Harness(new PricingOptions
        {
            BitstampBaseUri = new Uri(Fixtures.Bitstamp),
            RiksbankBaseUri = new Uri(Fixtures.Riksbank),
            KronaVia = FiatCurrency.Sek,
        });

        var request = new PriceRequest(Chain.Ethereum, Fixtures.At)
        {
            Asset = KnownTokens.EthereumUsdt,
            Currency = FiatCurrency.Sek,
        };

        var lookup = await harness.Prices.GetPriceAsync(harness.Case, request);

        Assert.Equal(PriceLookupStatus.Unsupported, lookup.Status);
        Assert.Equal("Källan har ingen kurs för USDT i SEK. Inget skickades.", lookup.Problem);
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public async Task An_asset_on_another_chain_than_the_request_is_refused()
    {
        var lookup = await _harness.Prices.GetPriceAsync(
            _harness.Case, new PriceRequest(Chain.Bitcoin, Fixtures.At) { Asset = KnownTokens.EthereumUsdt });

        Assert.Equal(PriceLookupStatus.Unsupported, lookup.Status);
        Assert.Equal("USDT finns inte på kedjan Bitcoin. Inget skickades.", lookup.Problem);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Bitcoin_named_as_its_asset_is_asked_and_stored_exactly_as_before()
    {
        _harness.AnswerAll();

        var named = await _harness.Prices.GetPriceAsync(
            _harness.Case,
            new PriceRequest(Chain.Bitcoin, Fixtures.At) { Asset = Asset.Bitcoin, Currency = FiatCurrency.Usd });
        var plain = await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Fetched, named.Status);
        Assert.Equal(PriceLookupStatus.FromCase, plain.Status);
        Assert.Equal(named.Value!.CryptoRate, plain.Value!.CryptoRate);
        Assert.Equal(Asset.Bitcoin, plain.Value.Asset);
        Assert.Equal(["BTC-USD/3600/1699999200"], _harness.RawRows().Select(row => row.Subject));
        Assert.Equal([Fixtures.BitstampUsdHour], _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri));
    }

    private Task<PriceLookup<Price>> PriceAsync(Asset asset, FiatCurrency? currency) =>
        _harness.Prices.GetPriceAsync(
            _harness.Case, new PriceRequest(asset.Chain, Fixtures.At) { Asset = asset, Currency = currency });
}

/// <summary>A price of a token, and amounts valued at it.</summary>
public sealed class TokenPriceTests
{
    private static readonly RateOrigin Origin =
        new(1, "bitstamp", new Uri("https://bitstamp.test/"), 200, default, default,
            new OperatorProfile("Testa Testsson", "Utredare", "Enheten"), "00");

    private static readonly CryptoRate Usdt = new(
        Chain.Ethereum, FiatCurrency.Usd, PriceResolution.Hour,
        DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart),
        DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart + 3_600),
        "USDT/USD", 1.0002m, 1.0004m, 0.9995m, 0.9998m, 182345.5m, Origin)
    {
        Asset = KnownTokens.EthereumUsdt,
        SourceName = "Bitstamp",
    };

    private static readonly FiatRate Krona = new(
        FiatCurrency.Usd, FiatCurrency.Sek, 10.513m, new DateOnly(2023, 11, 14), new DateOnly(2023, 11, 14),
        "SEKUSDPMI", Origin with { Sequence = 2, Source = "riksbank" }) { SourceName = "Riksbanken" };

    [Fact]
    public void A_rate_for_a_chains_coin_is_for_its_native_asset_by_default()
    {
        var bitcoin = Usdt with { Asset = Asset.Bitcoin, Chain = Chain.Bitcoin };
        var plain = new CryptoRate(
            Chain.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour, default, default, "BTC/USD", 1, 1, 1, 1, 1, Origin);

        Assert.Equal(Asset.Bitcoin, plain.Asset);
        Assert.Equal(Asset.Bitcoin, bitcoin.Asset);
        Assert.Equal(Asset.Ether, (plain with { Chain = Chain.Ethereum }).Asset);
    }

    [Fact]
    public void Names_the_token_by_its_symbol_in_the_steps()
    {
        var steps = new Price(Fixtures.At, FiatCurrency.Sek, Usdt, Krona).Steps;

        Assert.Equal(
            [
                "1 USDT = 0,9998 USD: Bitstamp, USDT/USD, sista priset för 2023-11-14 22:00–23:00 (UTC).",
                "1 USD = 10,513 SEK: Riksbanken, serien SEKUSDPMI, kursen för 2023-11-14.",
                "1 USDT = 0,9998 × 10,513 = 10,5108974 SEK.",
            ],
            steps);
    }

    [Fact]
    public void Values_usdt_from_its_six_decimals_exactly()
    {
        var price = new Price(Fixtures.At, FiatCurrency.Sek, Usdt, Krona);

        var valuation = price.ValueOf(new AssetAmount(KnownTokens.EthereumUsdt, 1_250_500_000));

        Assert.Equal(1250.5m, valuation.Amount);
        Assert.Equal(13143.8771987m, valuation.Value);
        Assert.Equal(13143.88m, valuation.Rounded);
        Assert.Equal(
            "1 250,5 USDT × 10,5108974 = 13 143,8771987 SEK, avrundat 13 143,88 SEK.",
            valuation.Steps[^1]);
    }

    [Fact]
    public void Values_the_smallest_unit_of_usdc()
    {
        var usdc = Usdt with { Asset = KnownTokens.EthereumUsdc, Market = "USDC/USD", Close = 1.0001m };
        var price = new Price(Fixtures.At, FiatCurrency.Usd, usdc, null);

        var valuation = price.ValueOf(new AssetAmount(KnownTokens.EthereumUsdc, 1));

        Assert.Equal(0.000001m, valuation.Amount);
        Assert.Equal(0.0000010001m, valuation.Value);
        Assert.Equal(0m, valuation.Rounded);
    }

    [Fact]
    public void Refuses_an_amount_of_another_asset()
    {
        var price = new Price(Fixtures.At, FiatCurrency.Usd, Usdt, null);

        Assert.Throws<InvalidOperationException>(() => price.ValueOf(new AssetAmount(KnownTokens.EthereumUsdc, 1)));
        Assert.Throws<InvalidOperationException>(() => price.ValueOf(new AssetAmount(KnownTokens.TronUsdt, 1)));
        Assert.Throws<InvalidOperationException>(() => price.ValueOf(new AssetAmount(Asset.Ether, 1)));
        Assert.Throws<InvalidOperationException>(() => price.ValueOf(new Satoshi(1)));
        Assert.Throws<InvalidOperationException>(() => price.ValueOf(new AssetAmount(
            Asset.Token(Chain.Ethereum, "0x1111111111111111111111111111111111111111", "USDT", 6), 1)));
    }

    [Fact]
    public void Refuses_a_rate_whose_asset_is_not_on_its_chain()
    {
        Assert.Throws<ArgumentException>(
            () => new Price(Fixtures.At, FiatCurrency.Usd, Usdt with { Chain = Chain.Bitcoin }, null));
    }

    [Fact]
    public void Refuses_a_null_asset()
    {
        Assert.Throws<ArgumentNullException>(() => Usdt with { Asset = null! });
    }
}

/// <summary>Bitstamp's markets for the tokens it lists.</summary>
public sealed class BitstampTokenSourceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart);

    private static readonly Asset Impostor =
        Asset.Token(Chain.Ethereum, "0x1111111111111111111111111111111111111111", "USDT", 6);

    /// <summary>A token on TRON called USDT whose contract is not Tether's.</summary>
    private static readonly Asset TronImpostor = Asset.Token(Chain.Tron, "T" + new string('1', 33), "USDT", 6);

    private readonly ICryptoRateSource _source = new BitstampRateSource(new Uri(Fixtures.Bitstamp));

    public static TheoryData<Asset, FiatCurrency, bool> Supported => new()
    {
        { KnownTokens.EthereumUsdt, FiatCurrency.Usd, true },
        { KnownTokens.EthereumUsdt, FiatCurrency.Eur, true },
        { KnownTokens.EthereumUsdt, FiatCurrency.Sek, false },
        { KnownTokens.EthereumUsdc, FiatCurrency.Usd, true },
        { KnownTokens.EthereumUsdc, FiatCurrency.Eur, true },
        { KnownTokens.TronUsdt, FiatCurrency.Usd, true },
        { KnownTokens.TronUsdt, FiatCurrency.Sek, false },
        { TronImpostor, FiatCurrency.Usd, false },
        { Impostor, FiatCurrency.Usd, false },
        { Asset.Bitcoin, FiatCurrency.Usd, true },
        { Asset.Ether, FiatCurrency.Eur, true },
        { Asset.Tronix, FiatCurrency.Usd, true },
        { Asset.Tronix, FiatCurrency.Sek, false },
        { KnownTokens.BscUsdt, FiatCurrency.Eur, true },
        { KnownTokens.BscUsdc, FiatCurrency.Usd, true },
        { Asset.Token(Chain.Bsc, KnownTokens.EthereumUsdt.Contract!, "USDT", 6), FiatCurrency.Usd, false },
        { Asset.Litecoin, FiatCurrency.Eur, true },
        { Asset.Bnb, FiatCurrency.Eur, true },
    };

    [Theory]
    [MemberData(nameof(Supported))]
    public void Has_usdt_and_usdc_on_ethereum_by_contract(Asset asset, FiatCurrency quote, bool supported)
    {
        Assert.Equal(supported, _source.Supports(asset, quote));
    }

    [Theory]
    [InlineData("USDT", FiatCurrency.Usd, "usdtusd")]
    [InlineData("USDT", FiatCurrency.Eur, "usdteur")]
    [InlineData("USDC", FiatCurrency.Usd, "usdcusd")]
    [InlineData("USDC", FiatCurrency.Eur, "usdceur")]
    [InlineData("TRON-USDT", FiatCurrency.Eur, "usdteur")]
    public void Asks_the_tokens_own_market(string token, FiatCurrency quote, string market)
    {
        var asset = token switch
        {
            "USDT" => KnownTokens.EthereumUsdt,
            "TRON-USDT" => KnownTokens.TronUsdt,
            _ => KnownTokens.EthereumUsdc,
        };

        var uri = _source.RequestUri(asset, quote, PriceResolution.Hour, Start);

        Assert.Equal(
            $"{Fixtures.Bitstamp}ohlc/{market}/?step=3600&limit=1&start={Fixtures.HourStart}", uri.AbsoluteUri);
    }

    [Fact]
    public void Asks_for_a_chains_coin_by_asset_as_by_chain()
    {
        Assert.Equal(
            _source.RequestUri(Chain.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour, Start),
            _source.RequestUri(Asset.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour, Start));
        Assert.Equal(Fixtures.BitstampUsdHour,
            _source.RequestUri(Asset.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour, Start).AbsoluteUri);
    }

    [Fact]
    public void Reads_a_tokens_candle()
    {
        var candle = _source.ReadCandle(
            Fixtures.Bytes("bitstamp-usdtusd-hour.json"), KnownTokens.EthereumUsdt, FiatCurrency.Usd,
            PriceResolution.Hour, Start);

        Assert.NotNull(candle);
        Assert.Equal("USDT/USD", candle.Market);
        Assert.Equal(0.9998m, candle.Close);
        Assert.Equal(182345.5m, candle.Volume);
    }

    [Fact]
    public void Refuses_a_response_for_another_market()
    {
        var body = Fixtures.Bytes("bitstamp-usdtusd-hour.json");

        Assert.Throws<RateFormatException>(() => _source.ReadCandle(
            body, KnownTokens.EthereumUsdc, FiatCurrency.Usd, PriceResolution.Hour, Start));
        Assert.Throws<RateFormatException>(() => _source.ReadCandle(
            body, Asset.Ether, FiatCurrency.Usd, PriceResolution.Hour, Start));
    }

    [Fact]
    public void Has_no_market_for_an_unknown_token()
    {
        var unknown = TronImpostor;


        Assert.Throws<NotSupportedException>(
            () => _source.RequestUri(unknown, FiatCurrency.Usd, PriceResolution.Hour, Start));
        Assert.Throws<NotSupportedException>(() => _source.ReadCandle(
            Fixtures.Bytes("bitstamp-usdtusd-hour.json"), unknown, FiatCurrency.Usd, PriceResolution.Hour, Start));
    }

    [Fact]
    public void A_source_without_tokens_prices_only_the_chains_coin_by_default()
    {
        ICryptoRateSource coinsOnly = new CoinsOnly();

        Assert.True(coinsOnly.Supports(Asset.Bitcoin, FiatCurrency.Usd));
        Assert.False(coinsOnly.Supports(KnownTokens.EthereumUsdt, FiatCurrency.Usd));
        Assert.Equal("https://coins.test/BTC",
            coinsOnly.RequestUri(Asset.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour, Start).AbsoluteUri);
        Assert.Throws<NotSupportedException>(
            () => coinsOnly.RequestUri(KnownTokens.EthereumUsdt, FiatCurrency.Usd, PriceResolution.Hour, Start));
        Assert.Throws<NotSupportedException>(() => coinsOnly.ReadCandle(
            [], KnownTokens.EthereumUsdt, FiatCurrency.Usd, PriceResolution.Hour, Start));
    }

    /// <summary>A source written before tokens could be priced: it knows only chains.</summary>
    private sealed class CoinsOnly : ICryptoRateSource
    {
        public string Name => "coins";

        public string DisplayName => "Coins";

        public bool Supports(Chain chain, FiatCurrency quote) => true;

        public Uri RequestUri(
            Chain chain, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart) =>
            new($"https://coins.test/{chain.Code()}");

        public RateCandle? ReadCandle(
            ReadOnlySpan<byte> body, Chain chain, FiatCurrency quote, PriceResolution resolution,
            DateTimeOffset periodStart) =>
            null;
    }
}
