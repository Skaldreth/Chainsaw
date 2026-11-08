using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// A real, encrypted case with the Chains module's tables, and the Ethereum source behind a fake exit answering from
/// the synthetic Blockscout responses in <c>Ethereum/</c> (made by <c>Ethereum/generate.py</c>). Nothing goes on the
/// network.
/// </summary>
internal sealed class EthereumHarness : IDisposable
{
    public const string Base = "https://blockscout.test/api/";

    public static readonly EthereumAddress A = EthereumAddress.Parse("0x" + Fixtures.Repeat("11", 20));

    public static readonly EthereumAddress B = EthereumAddress.Parse("0x" + Fixtures.Repeat("22", 20));

    public static readonly EthereumAddress C = EthereumAddress.Parse("0x" + Fixtures.Repeat("33", 20));

    public static readonly EthereumAddress Router = EthereumAddress.Parse("0x" + Fixtures.Repeat("55", 20));

    public static readonly EthereumAddress Foo = EthereumAddress.Parse("0x" + Fixtures.Repeat("44", 20));

    public static readonly EthereumTxHash H1 = EthereumTxHash.Parse("0x" + Fixtures.Repeat("b1", 32));

    public static readonly EthereumTxHash H2 = EthereumTxHash.Parse("0x" + Fixtures.Repeat("b2", 32));

    public static readonly EthereumTxHash H3 = EthereumTxHash.Parse("0x" + Fixtures.Repeat("b3", 32));

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-ethereum-" + Guid.NewGuid().ToString("N"));

    public EthereumHarness(int pageSize = 100)
    {
        Http = new FakeExitHttp(Clock);
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
            new EthereumSourceOptions { DefaultEndpoints = [new Uri(Base)], PageSize = pageSize });
        _provider = services.BuildServiceProvider();
        Case = _provider.GetRequiredService<ICaseStore>().Create(SourceHarness.CaseNumber);
        Source = _provider.GetRequiredService<IEthereumSource>();
    }

    public FakeClock Clock { get; } = new();

    public FakeProfiles Profiles { get; } = new();

    public FakeExitHttp Http { get; }

    public ICase Case { get; }

    public IEthereumSource Source { get; }

    public IChainSources Sources => _provider.GetRequiredService<IChainSources>();

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    public CaseVerification Verify(ICase other) => _provider.GetRequiredService<ICaseVerifier>().Verify(other);

    public IResponseTransfer Transfer => _provider.GetRequiredService<IResponseTransfer>();

    public ICase CreateCase(string number) => _provider.GetRequiredService<ICaseStore>().Create(number);

    public static string Url(string pathAndQuery) => Base + pathAndQuery;

    public static string TxUrl(EthereumTxHash hash) =>
        Url("?module=transaction&action=gettxinfo&txhash=" + hash.Value);

    public static string InternalUrl(EthereumTxHash hash) =>
        Url("?module=account&action=txlistinternal&txhash=" + hash.Value);

    public static string HistoryUrl(string action, EthereumAddress address, int pageSize = 100, long? below = null) =>
        Url($"?module=account&action={action}&address={address.Value}&sort=desc&page=1&offset={pageSize}")
        + (below is null ? string.Empty : "&endblock=" + below);

    /// <summary>Answers a URL from a fixture in <c>Ethereum/</c>.</summary>
    public void Answer(string url, string fixture, int status = 200) =>
        Http.Answer(url, "../Ethereum/" + fixture, status);

    /// <summary>Answers the transaction with everything in it, its block and the chain's height.</summary>
    public void AnswerTransaction()
    {
        Answer(TxUrl(H1), "tx-b1.json");
        Answer(InternalUrl(H1), "tx-b1-internal.json");
        Answer(Url("v2/blocks/900"), "block-900.json");
        Answer(Url("?module=token&action=getToken&contractaddress=" + Foo.Value), "token-foo.json");
        Answer(Url("?module=block&action=eth_block_number"), "tip.json");
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
