using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// A real, encrypted case with the Chains module's tables, and the Solana source behind a fake exit answering from the
/// synthetic JSON-RPC answers in <c>Solana/</c> (made by <c>Solana/generate.py</c>). Nothing goes on the network.
/// </summary>
internal sealed class SolanaHarness : IDisposable
{
    public const string Base = "https://solana.test/";

    /// <summary>The first slot of the synthetic transactions.</summary>
    public const long Slot = 400_000_000;

    public static readonly EthereumAddress A = Key(0x11);

    public static readonly EthereumAddress B = Key(0x22);

    public static readonly EthereumAddress C = Key(0x33);

    public static readonly EthereumAddress Pool = Key(0x44);

    public static readonly EthereumAddress TokenAccountA = Key(0x55);

    public static readonly EthereumAddress TokenAccountB = Key(0x66);

    public static readonly EthereumAddress Foo = Key(0x77);

    public static readonly EthereumTxHash S1 = Signature(0xA1);

    public static readonly EthereumTxHash S2 = Signature(0xA2);

    public static readonly EthereumTxHash S3 = Signature(0xA3);

    public static readonly EthereumTxHash S4 = Signature(0xA4);

    public static readonly EthereumTxHash S5 = Signature(0xA5);

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-solana-" + Guid.NewGuid().ToString("N"));

    public SolanaHarness(int pageSize = 25)
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
            solana: new SolanaSourceOptions { DefaultEndpoints = [new Uri(Base)], PageSize = pageSize });
        _provider = services.BuildServiceProvider();
        Case = _provider.GetRequiredService<ICaseStore>().Create(SourceHarness.CaseNumber);
        Source = _provider.GetRequiredService<ISolanaSource>();
    }

    public FakeClock Clock { get; } = new();

    public FakeProfiles Profiles { get; } = new();

    public FakeExitHttp Http { get; }

    public int PageSize { get; }

    public ICase Case { get; }

    public ISolanaSource Source { get; }

    public IChainSources Sources => _provider.GetRequiredService<IChainSources>();

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    public static EthereumAddress Key(byte value) =>
        EthereumAddress.Parse(Chain.Solana, Base58.Encode(Enumerable.Repeat(value, 32).ToArray()));

    public static EthereumTxHash Signature(byte value) =>
        EthereumTxHash.Parse(Chain.Solana, Base58.Encode(Enumerable.Repeat(value, 64).ToArray()));

    /// <summary>Answers a JSON-RPC call to the endpoint from a fixture in <c>Solana/</c>.</summary>
    public void Answer(string call, string fixture, int status = 200) =>
        Http.Answer(FakeExitHttp.PostKey(Base, call), "../Solana/" + fixture, status);

    /// <summary>Answers a transaction, and the block of its slot when it is the first slot.</summary>
    public void AnswerTransaction(EthereumTxHash hash, string fixture)
    {
        Answer(SolanaRpcApi.TransactionCall(hash), fixture);
        Answer(SolanaRpcApi.BlockCall(Slot), "block.json");
    }

    /// <summary>Gives the bodies of the calls sent, in order.</summary>
    public List<string> Calls() =>
        [.. Http.Sent.Select(request => System.Text.Encoding.UTF8.GetString(request.Body.Span))];

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
