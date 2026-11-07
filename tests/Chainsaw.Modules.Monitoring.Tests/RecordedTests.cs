using System.Globalization;
using System.Text;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// A check through the real Esplora source and the real endpoints, behind a fake exit that answers with recorded
/// (synthetic) Esplora responses: only an endpoint probed in step is asked, every response is sealed, and the hits
/// point at them. Never the network.
/// </summary>
public sealed class RecordedTests : IDisposable
{
    private const string Base = "https://esplora.test/api/";

    private const string Watched = "bc1qxvenxvenxvenxvenxvenxvenxvenxven2ymjt8";

    private const string Spent = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";

    private const string Coinbase = "a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-monitoring-rec-" + Guid.NewGuid().ToString("N"));

    private readonly ServiceProvider _provider;

    private readonly FakeClock _clock = new();

    private readonly FakeExitHttp _http;

    private readonly ICase _case;

    public RecordedTests()
    {
        _http = new FakeExitHttp(_clock);
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(new FakeProfiles());
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<IExitHttp>(_http);
        services.AddSingleton<IWatchGate>(new FakeGate());
        services.AddCasesModule(options =>
        {
            options.Directory = _directory;
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddChainsModule(
            new BitcoinSourceOptions { EsploraBaseUri = new Uri(Base) },
            options => options.Directory = _directory);
        services.AddClusteringModule();
        services.AddMonitoringModule();
        _provider = services.BuildServiceProvider();
        _case = _provider.GetRequiredService<ICaseStore>().Create("2026-0263");
        _http.Answer(Base + $"address/{Watched}/txs", Recorded("address-page-first.json"));
        _http.Answer(Base + $"address/{Watched}/txs/chain/{Spent}", Recorded("address-page-after.json"));
    }

    public void Dispose()
    {
        _case.Dispose();
        _provider.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Recorded_pages_give_the_hits_above_the_start_through_an_endpoint_in_step()
    {
        var service = _provider.GetRequiredService<IMonitoringService>();
        var sources = _provider.GetRequiredService<IChainSources>();
        Height(800_010);
        await sources.ProbeAsync(Chain.Bitcoin);
        service.Add(
            _case,
            new WatchSubject(WatchSubjectKind.Address, new ChainAddress(Chain.Bitcoin, Watched), "bc1q…jt8"),
            new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null));
        await service.CheckAsync(_case, Chain.Bitcoin, 799_999);
        _http.Sent.Clear();

        var height = await service.GetHeightAsync(_case, Chain.Bitcoin);
        var run = await service.CheckAsync(_case, Chain.Bitcoin, height!.Value);

        Assert.Equal(800_010, height);
        var hits = run.Hits.ToList();
        Assert.Equal([Spent, Coinbase], hits.Select(hit => hit.Match.Transaction));
        Assert.Equal(WatchDirection.Out, hits[0].Match.Direction);
        Assert.Equal(99_990_000, (long)hits[0].Match.Amount.Units);
        Assert.Equal(WatchDirection.In, hits[1].Match.Direction);
        Assert.Equal(626_234_567, (long)hits[1].Match.Amount.Units);
        Assert.Equal(
            ["blocks/tip/height", $"address/{Watched}/txs", $"address/{Watched}/txs/chain/{Spent}"],
            _http.Sent.Select(request => request.Uri.AbsoluteUri[Base.Length..]));
        var sealedResponses = _case.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT sequence FROM chains_raw_responses");
            using var rows = command.ExecuteReader();
            var all = new List<long>();
            while (rows.Read())
            {
                all.Add(rows.GetInt64(0));
            }

            return all;
        });
        Assert.All(hits, hit => Assert.Subset(sealedResponses.ToHashSet(), hit.Origins.ToHashSet()));
        Assert.Equal(2, hits[0].Origins.Count);
        Assert.True(_provider.GetRequiredService<ICaseVerifier>().Verify(_case).IsIntact);
    }

    [Fact]
    public async Task Without_an_endpoint_known_in_step_nothing_is_sent_and_the_check_says_so()
    {
        var service = _provider.GetRequiredService<IMonitoringService>();
        service.Add(
            _case,
            new WatchSubject(WatchSubjectKind.Address, new ChainAddress(Chain.Bitcoin, Watched), "bc1q…jt8"),
            new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null));
        await service.CheckAsync(_case, Chain.Bitcoin, 799_999);

        var run = await service.CheckAsync(_case, Chain.Bitcoin, 800_010);

        Assert.Empty(_http.Sent);
        var outcome = Assert.Single(run.Outcomes);
        Assert.NotEqual(WatchFeedStatus.Read, outcome.Status);
        Assert.Empty(outcome.Hits);
        Assert.Contains("i fas", string.Join(' ', outcome.Problems), StringComparison.Ordinal);
    }

    private void Height(long height) => _http.Answer(
        Base + "blocks/tip/height",
        Encoding.ASCII.GetBytes(height.ToString(CultureInfo.InvariantCulture)),
        "text/plain");

    private static byte[] Recorded(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Recorded", name));
}
