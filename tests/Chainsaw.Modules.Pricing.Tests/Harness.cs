using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Pricing.Tests;

/// <summary>
/// A real, encrypted case in a temporary folder with the Pricing module's tables, and the price service behind a fake
/// exit. Nothing goes on the network.
/// </summary>
internal sealed class Harness : IDisposable
{
    public const string CaseNumber = "2026-0414";

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-pricing-" + Guid.NewGuid().ToString("N"));

    public Harness(PricingOptions? options = null, Action<IServiceCollection>? before = null)
    {
        Http = new FakeExitHttp(Clock);
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IVaultSecrets>(new TestVaultSecrets());
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IExitHttp>(Http);
        before?.Invoke(services);
        services.AddCasesModule(settings =>
        {
            settings.Directory = _directory;
            settings.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddPricingModule(options ?? new PricingOptions
        {
            BitstampBaseUri = new Uri(Fixtures.Bitstamp),
            RiksbankBaseUri = new Uri(Fixtures.Riksbank),
            RiksbankInterval = TimeSpan.Zero,
            KronaVia = FiatCurrency.Usd,
        });
        _provider = services.BuildServiceProvider();
        Case = _provider.GetRequiredService<ICaseStore>().Create(CaseNumber);
        Prices = _provider.GetRequiredService<IPriceService>();
        Defaults = _provider.GetRequiredService<IPriceDefaults>();
    }

    public FakeClock Clock { get; } = new();

    public FakeProfiles Profiles { get; } = new();

    public FakeExitHttp Http { get; }

    public ICase Case { get; }

    public IPriceService Prices { get; }

    public IPriceDefaults Defaults { get; }

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    /// <summary>Answers the usual requests from the fixtures.</summary>
    public void AnswerAll()
    {
        Http.Answer(Fixtures.BitstampUsdHour, "bitstamp-btcusd-hour.json");
        Http.Answer(Fixtures.BitstampEurHour, "bitstamp-btceur-hour.json");
        Http.Answer(Fixtures.RiksbankUsdNovember, "riksbank-sekusdpmi.json");
    }

    /// <summary>Answers Bitstamp's request for the hour that holds a time with one candle at one price.</summary>
    public void AnswerCandle(DateTimeOffset at, string price)
    {
        var start = PriceResolution.Hour.PeriodStart(at).ToUnixTimeSeconds();
        Http.AnswerText(
            $"{Fixtures.Bitstamp}ohlc/btcusd/?step=3600&limit=1&start={start}",
            $$$"""
            {"data": {"pair": "BTC/USD", "ohlc": [{"timestamp": "{{{start}}}", "open": "{{{price}}}",
            "high": "{{{price}}}", "low": "{{{price}}}", "close": "{{{price}}}", "volume": "1"}]}}
            """);
    }

    public Task<PriceLookup<Price>> PriceAsync(
        DateTimeOffset at, FiatCurrency? currency = null, PriceLookupMode mode = PriceLookupMode.AsNeeded,
        PriceResolution? resolution = null) =>
        Prices.GetPriceAsync(
            Case, new PriceRequest(Chain.Bitcoin, at) { Currency = currency, Resolution = resolution }, mode);

    /// <summary>Reads the sealed exit and way out of every raw response, in order.</summary>
    public List<(string? ExitId, string Route)> ExitColumns() => Case.Read(reader =>
    {
        using var command = reader.CreateCommand(
            "SELECT exit_id, exit_route FROM pricing_raw_responses ORDER BY sequence");
        using var row = command.ExecuteReader();
        var rows = new List<(string?, string)>();
        while (row.Read())
        {
            rows.Add((row.IsDBNull(0) ? null : row.GetString(0), row.GetString(1)));
        }

        return rows;
    });

    /// <summary>Reads the sealed raw responses, in order.</summary>
    public List<RawRow> RawRows() => Case.Read(reader =>
    {
        using var command = reader.CreateCommand(
            """
            SELECT sequence, source, url, kind, subject, status_code, received_local, time_zone, exit_id, fetched_by,
                   sha256, body
            FROM pricing_raw_responses ORDER BY sequence
            """);
        using var row = command.ExecuteReader();
        var rows = new List<RawRow>();
        while (row.Read())
        {
            rows.Add(new RawRow(
                row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4),
                row.GetInt64(5), row.GetString(6), row.GetString(7), row.GetString(8), row.GetString(9),
                row.GetFieldValue<byte[]>(10), row.GetFieldValue<byte[]>(11)));
        }

        return rows;
    });

    /// <summary>Runs SQL on the case's tables that are not sealed, as a change outside Chainsaw would.</summary>
    public void Execute(string sql) => Case.Write(transaction =>
    {
        using var command = transaction.CreateCommand(sql);
        return command.ExecuteNonQuery();
    });

    /// <summary>Counts the rows of an index.</summary>
    public long Count(string table) => Case.Read(reader =>
    {
        using var command = reader.CreateCommand($"SELECT COUNT(*) FROM {table}");
        return (long)command.ExecuteScalar()!;
    });

    public void Dispose()
    {
        Case.Dispose();
        _provider.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

/// <summary>One row of the sealed raw responses.</summary>
internal sealed record RawRow(
    long Sequence,
    string Source,
    string Url,
    string Kind,
    string Subject,
    long Status,
    string ReceivedLocal,
    string TimeZone,
    string ExitId,
    string FetchedBy,
    byte[] Sha256,
    byte[] Body);
