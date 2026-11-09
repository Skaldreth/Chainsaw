using System.Globalization;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// A real, encrypted case with the Chains module's tables, and the TRON source behind a fake exit answering from the
/// synthetic TronGrid responses in <c>Tron/</c> (made by <c>Tron/generate.py</c>). Nothing goes on the network.
/// </summary>
internal sealed class TronHarness : IDisposable
{
    public const string Base = "https://trongrid.test/";

    public static readonly EthereumAddress A = Address("11");

    public static readonly EthereumAddress B = Address("22");

    public static readonly EthereumAddress C = Address("33");

    public static readonly EthereumAddress X = Address("55");

    public static readonly EthereumAddress Foo = Address("44");

    public static readonly EthereumTxHash C1 = Hash("c1");

    public static readonly EthereumTxHash C2 = Hash("c2");

    public static readonly EthereumTxHash C3 = Hash("c3");

    public static readonly EthereumTxHash C4 = Hash("c4");

    /// <summary>The block time of the synthetic transactions, 2026-11-01 10:00 UTC, in milliseconds.</summary>
    public const long Time = 1793872800000;

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-tron-" + Guid.NewGuid().ToString("N"));

    public TronHarness(int pageSize = 50)
    {
        Http = new FakeExitHttp(Clock);
        PageSize = pageSize;
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IVaultSecrets>(new TestVaultSecrets());
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IExitHttp>(Http);
        services.AddCasesModule(options =>
        {
            options.Directory = _directory;
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddChainsModule(
            new BitcoinSourceOptions { EsploraBaseUri = new Uri(Fixtures.Base) },
            options => options.Delay = Clock.Delay,
            tron: new TronSourceOptions { DefaultEndpoints = [new Uri(Base)], PageSize = pageSize });
        _provider = services.BuildServiceProvider();
        Case = _provider.GetRequiredService<ICaseStore>().Create(SourceHarness.CaseNumber);
        Source = _provider.GetRequiredService<ITronSource>();
    }

    public FakeClock Clock { get; } = new();

    public FakeProfiles Profiles { get; } = new();

    public FakeExitHttp Http { get; }

    public int PageSize { get; }

    public ICase Case { get; }

    public ITronSource Source { get; }

    public IChainSources Sources => _provider.GetRequiredService<IChainSources>();

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    public static EthereumAddress Address(string twoDigits) =>
        EthereumAddress.Parse(Chain.Tron, "41" + Fixtures.Repeat(twoDigits, 20));

    public static EthereumTxHash Hash(string twoDigits) =>
        EthereumTxHash.Parse(Chain.Tron, Fixtures.Repeat(twoDigits, 32));

    public static string Url(string pathAndQuery) => Base + pathAndQuery;

    public static string TipUrl => Url("wallet/getblock?detail=false");

    public static string BlockUrl(long height) =>
        Url("wallet/getblock?id_or_num=" + height.ToString(CultureInfo.InvariantCulture) + "&detail=false");

    public static string TxUrl(EthereumTxHash hash) => Url("wallet/gettransactionbyid?value=" + hash.Value);

    public static string InfoUrl(EthereumTxHash hash) => Url("wallet/gettransactioninfobyid?value=" + hash.Value);

    public string HistoryUrl(string list, EthereumAddress address, long? atOrBelow = null) =>
        Url($"v1/accounts/{address.Value}/{list}?order_by=block_timestamp,desc&limit={PageSize}")
        + (atOrBelow is { } start ? "&max_timestamp=" + start.ToString(CultureInfo.InvariantCulture) : string.Empty);

    /// <summary>Answers a URL from a fixture in <c>Tron/</c>.</summary>
    public void Answer(string url, string fixture, int status = 200) =>
        Http.Answer(url, "../Tron/" + fixture, status);

    /// <summary>Answers the TRC-20 transaction with everything in it, its block and the newest block.</summary>
    public void AnswerTransaction()
    {
        Answer(TxUrl(C1), "tx-c1.json");
        Answer(InfoUrl(C1), "tx-c1-info.json");
        Answer(BlockUrl(900), "block-900.json");
        Answer(TipUrl, "tip.json");
    }

    /// <summary>Reads the sealed chain, sort of API and kind of every raw response, in order.</summary>
    public List<(string Chain, string Source, string Kind, long Status)> Rows() => Case.Read(reader =>
    {
        using var command = reader.CreateCommand(
            "SELECT chain, source, kind, status_code FROM chains_raw_responses ORDER BY sequence");
        using var row = command.ExecuteReader();
        var rows = new List<(string, string, string, long)>();
        while (row.Read())
        {
            rows.Add((row.GetString(0), row.GetString(1), row.GetString(2), row.GetInt64(3)));
        }

        return rows;
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
