using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// A real, encrypted case in a temporary folder with the Chains module's tables, and the Esplora source behind a fake
/// exit. Nothing goes on the network.
/// </summary>
internal sealed class SourceHarness : IDisposable
{
    public const string CaseNumber = "2026-0042";

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-chains-" + Guid.NewGuid().ToString("N"));

    public SourceHarness()
    {
        Http = new FakeExitHttp(Clock);
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IExitHttp>(Http);
        services.AddCasesModule(options =>
        {
            options.Directory = _directory;
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddChainsModule(new BitcoinSourceOptions
        {
            EsploraBaseUri = new Uri(Fixtures.Base),
        });
        _provider = services.BuildServiceProvider();
        Case = _provider.GetRequiredService<ICaseStore>().Create(CaseNumber);
        Source = _provider.GetRequiredService<IBitcoinSource>();
    }

    public FakeClock Clock { get; } = new();

    public FakeProfiles Profiles { get; } = new();

    public FakeExitHttp Http { get; }

    public ICase Case { get; }

    public IBitcoinSource Source { get; }

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    /// <summary>Answers the usual requests from the fixtures.</summary>
    public void AnswerAll()
    {
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Confirmed}"), "tx-confirmed.json");
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Unconfirmed}"), "tx-unconfirmed.json");
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Coinbase}"), "tx-coinbase.json");
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Older}"), "tx-older.json");
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Confirmed}/outspends"), "outspends-open.json");
        Http.Answer(Fixtures.Url($"block/{Fixtures.Block}"), "block.json");
        Http.Answer(Fixtures.Url("blocks/tip/height"), "tip-height.txt");
        var address = Fixtures.AddressText("wpkh");
        Http.Answer(Fixtures.Url($"address/{address}/txs"), "address-page-first.json");
        Http.Answer(Fixtures.Url($"address/{address}/txs/chain/{Fixtures.Confirmed}"), "address-page-after.json");
        Http.Answer(Fixtures.Url($"address/{address}/txs/chain/{Fixtures.Older}"), "address-page-empty.json");
    }

    /// <summary>Answers the transaction and block whose bytes are real, and the address summary.</summary>
    public void AnswerReal()
    {
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Real}"), "tx-real.json");
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Real}/hex"), "tx-real-hex.txt");
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Real}/outspends"), "outspends-real.json");
        Http.Answer(Fixtures.Url($"block/{Fixtures.RealBlock}"), "block-real.json");
        Http.Answer(Fixtures.Url($"block/{Fixtures.RealBlock}/header"), "block-real-header.txt");
        Http.Answer(Fixtures.Url($"address/{Fixtures.AddressText("wpkh")}"), "address-summary.json");
    }

    /// <summary>Reads the sealed exit and way out of every raw response, in order.</summary>
    public List<(string? ExitId, string Route)> ExitColumns() => Case.Read(reader =>
    {
        using var command = reader.CreateCommand("SELECT exit_id, exit_route FROM chains_raw_responses ORDER BY sequence");
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
            SELECT sequence, chain, source, url, kind, subject, status_code, media_type, requested_utc, received_utc,
                   received_local, time_zone, exit_id, fetched_by, sha256, body, hash
            FROM chains_raw_responses ORDER BY sequence
            """);
        using var row = command.ExecuteReader();
        var rows = new List<RawRow>();
        while (row.Read())
        {
            rows.Add(new RawRow(
                row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4),
                row.GetString(5), row.GetInt64(6), row.IsDBNull(7) ? null : row.GetString(7), row.GetString(8),
                row.GetString(9), row.GetString(10), row.GetString(11), row.GetString(12), row.GetString(13),
                row.GetFieldValue<byte[]>(14), row.GetFieldValue<byte[]>(15), row.GetFieldValue<byte[]>(16)));
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

/// <summary>One row of the sealed raw responses.</summary>
internal sealed record RawRow(
    long Sequence,
    string Chain,
    string Source,
    string Url,
    string Kind,
    string Subject,
    long Status,
    string? MediaType,
    string RequestedUtc,
    string ReceivedUtc,
    string ReceivedLocal,
    string TimeZone,
    string ExitId,
    string FetchedBy,
    byte[] Sha256,
    byte[] Body,
    byte[] Hash);
