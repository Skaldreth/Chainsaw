using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The chains' endpoints (sketch KL1a): the defaults, the modes Snabbast, Prioriterad and Av, probes that ask only for
/// the height and never go past the exit, keys from the vault, and the file in the program's data. Nothing goes on the
/// network.
/// </summary>
public sealed class ChainSourcesTests : IDisposable
{
    private const string Fast = "https://fast.example/api/";
    private const string Slow = "https://slow.example/api/";
    private const string Lagging = "https://lagging.example/api/";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-sources-" + Guid.NewGuid().ToString("N"));

    private readonly FakeClock _clock = new();

    private readonly FakeExitHttp _http;

    private readonly FakeKeys _keys = new();

    private readonly FakeVault _vault = new();

    public ChainSourcesTests() => _http = new FakeExitHttp(_clock);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Bitcoin_starts_with_mempool_space_and_Blockstream_in_Snabbast()
    {
        var sources = Create(new BitcoinSourceOptions());

        var bitcoin = sources.Get(Chain.Bitcoin);

        Assert.Equal([Chain.Bitcoin], sources.Chains);
        Assert.Equal(SourceMode.Fastest, bitcoin.Mode);
        Assert.Equal(["mempool.space", "Blockstream"], bitcoin.Endpoints.Select(e => e.Endpoint.Name));
        Assert.All(bitcoin.Endpoints, e => Assert.True(e.Endpoint.Enabled));
        Assert.Equal("mempool.space", bitcoin.InUse!.Endpoint.Name);
        Assert.Equal("esplora", Assert.Single(sources.Apis(Chain.Bitcoin)).Code);
        Assert.Empty(sources.Apis(Chain.Ethereum));
        Assert.Throws<ArgumentException>(() => sources.Get(Chain.Ethereum));
    }

    [Fact]
    public async Task Snabbast_asks_only_for_the_height_and_takes_the_fastest_that_is_not_behind()
    {
        var sources = Create(Endpoints(Slow, Lagging, Fast));
        Height(Slow, 921_404, 240);
        Height(Lagging, 921_402, 38);
        Height(Fast, 921_404, 180);

        var run = await sources.ProbeAsync(Chain.Bitcoin);

        Assert.Equal(ProbeRun.Probed, run);
        Assert.All(_http.Sent, request => Assert.EndsWith("/blocks/tip/height", request.Uri.AbsoluteUri));
        Assert.Equal(3, _http.Sent.Count);
        var view = sources.Get(Chain.Bitcoin);
        Assert.Equal(Fast, view.InUse!.Endpoint.Address.AbsoluteUri);
        var behind = view.Endpoints.Single(e => e.Endpoint.Address.AbsoluteUri == Lagging).Probe!;
        Assert.Equal(ProbeOutcome.Behind, behind.Outcome);
        Assert.Equal(921_402, behind.Height);
        Assert.Equal(TimeSpan.FromMilliseconds(38), behind.Duration);
        Assert.Contains("2 block efter", behind.Problem, StringComparison.Ordinal);
        Assert.Equal(2, behind.BlocksBehind);
        Assert.Equal(
            [Fast, Slow, Lagging], sources.Plan(Chain.Bitcoin).Endpoints.Select(p => p.Endpoint.Address.AbsoluteUri));
        Assert.Equal(_clock.UtcNow, view.ProbedAt);
    }

    [Fact]
    public async Task A_wrong_answer_or_a_dead_endpoint_fails_its_probe_and_comes_last()
    {
        var sources = Create(Endpoints(Slow, Fast, Lagging));
        Height(Slow, 921_404, 240);
        _http.AnswerBytes(Fast + "blocks/tip/height", Encoding.ASCII.GetBytes("<html>"), 200);
        _http.FailingHosts.Add("lagging.example");

        await sources.ProbeAsync(Chain.Bitcoin);

        var view = sources.Get(Chain.Bitcoin);
        Assert.Equal(ProbeOutcome.Failed, view.Endpoints[1].Probe!.Outcome);
        Assert.Equal(ProbeOutcome.Failed, view.Endpoints[2].Probe!.Outcome);
        Assert.Equal(Slow, view.InUse!.Endpoint.Address.AbsoluteUri);
        Assert.False(sources.AnsweredProbe(Chain.Bitcoin, view.Endpoints[1].Endpoint.Id));
        Assert.True(sources.AnsweredProbe(Chain.Bitcoin, view.Endpoints[0].Endpoint.Id));
        Assert.Equal(
            [Slow, Fast, Lagging], sources.Plan(Chain.Bitcoin).Endpoints.Select(p => p.Endpoint.Address.AbsoluteUri));
    }

    [Fact]
    public async Task Nothing_is_probed_when_the_exit_does_not_allow_lookups_and_the_earlier_round_stands()
    {
        var sources = Create(Endpoints(Slow, Fast));
        Height(Slow, 921_404, 240);
        Height(Fast, 921_404, 180);
        await sources.ProbeAsync(Chain.Bitcoin);
        _http.Sent.Clear();
        _http.Connected = false;
        _clock.UtcNow = _clock.UtcNow.AddHours(1);

        var run = await sources.ProbeAsync(Chain.Bitcoin);

        Assert.Equal(ProbeRun.NotSent, run);
        Assert.Empty(_http.Sent);
        Assert.Equal(2, _http.Refused);
        var view = sources.Get(Chain.Bitcoin);
        Assert.Equal(_clock.UtcNow.AddHours(-1), view.ProbedAt);
        Assert.Equal(Fast, view.InUse!.Endpoint.Address.AbsoluteUri);
    }

    [Fact]
    public async Task Nothing_is_probed_or_tried_while_the_vault_is_locked()
    {
        var sources = Create(Endpoints(Fast));
        _vault.State = VaultState.Locked;

        var run = await sources.ProbeAsync(Chain.Bitcoin);
        var tried = await sources.TryAsync(Chain.Bitcoin, new Uri(Slow), "esplora", null);

        Assert.Equal(ProbeRun.NotSent, run);
        Assert.Equal(ProbeOutcome.NotSent, tried.Outcome);
        Assert.Empty(_http.Sent);
        Assert.Equal(0, _http.Refused);
    }

    [Fact]
    public async Task Av_sends_nothing_and_says_so()
    {
        var sources = Create(Endpoints(Fast));
        await sources.SetModeAsync(Chain.Bitcoin, SourceMode.Off);

        var run = await sources.ProbeAsync(Chain.Bitcoin);

        Assert.Equal(ProbeRun.Off, run);
        Assert.Empty(_http.Sent);
        Assert.True(sources.Plan(Chain.Bitcoin).Off);
        Assert.Null(sources.Get(Chain.Bitcoin).InUse);
    }

    [Fact]
    public async Task Prioriterad_follows_the_users_order_and_an_endpoint_that_failed_a_lookup_comes_last()
    {
        var sources = Create(Endpoints(Slow, Fast));
        Height(Slow, 921_404, 240);
        Height(Fast, 921_404, 180);
        await sources.ProbeAsync(Chain.Bitcoin);
        await sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        var slow = sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;

        Assert.Equal(slow, sources.Get(Chain.Bitcoin).InUse!.Endpoint.Id);
        sources.Failed(Chain.Bitcoin, slow);
        Assert.NotEqual(slow, sources.Get(Chain.Bitcoin).InUse!.Endpoint.Id);
        Assert.True(sources.Get(Chain.Bitcoin).Endpoints[0].FailedSince);
        sources.Answered(Chain.Bitcoin, slow);
        Assert.Equal(slow, sources.Get(Chain.Bitcoin).InUse!.Endpoint.Id);
    }

    [Fact]
    public async Task Endpoints_are_moved_switched_off_and_removed()
    {
        var sources = Create(Endpoints(Slow, Fast, Lagging));
        var ids = sources.Get(Chain.Bitcoin).Endpoints.Select(e => e.Endpoint.Id).ToList();
        var told = 0;
        sources.Changed += (_, chain) => told += chain == Chain.Bitcoin ? 1 : 0;

        await sources.MoveAsync(Chain.Bitcoin, ids[2], -1);
        await sources.MoveAsync(Chain.Bitcoin, ids[0], -5);
        await sources.SetEnabledAsync(Chain.Bitcoin, ids[0], false);
        await sources.RemoveAsync(Chain.Bitcoin, ids[1]);

        var view = sources.Get(Chain.Bitcoin);
        Assert.Equal([ids[0], ids[2]], view.Endpoints.Select(e => e.Endpoint.Id));
        Assert.False(view.Endpoints[0].Endpoint.Enabled);
        Assert.Equal(ids[2], view.InUse!.Endpoint.Id);
        Assert.Equal(4, told);
    }

    [Theory]
    [InlineData("http://esplora.example/api/")]
    [InlineData("https://user:pass@esplora.example/api/")]
    [InlineData("https://esplora.example/api/?case=2026-0042")]
    [InlineData("https://esplora.example/api/#x")]
    public void Only_plain_https_can_be_added(string address)
    {
        var sources = Create(Endpoints(Fast));

        Assert.False(SourceAddress.TryRead(address, out _, out var problem));
        Assert.NotNull(problem);
        Assert.Throws<ArgumentException>(() => sources.Add(Chain.Bitcoin, new Uri(address), "esplora", null));
        Assert.Single(sources.Get(Chain.Bitcoin).Endpoints);
    }

    [Fact]
    public void Http_is_refused_with_its_own_words()
    {
        Assert.False(SourceAddress.TryRead("http://esplora.example/api", out _, out var problem));
        Assert.Equal("Bara https.", problem);
        Assert.True(SourceAddress.TryRead(" https://esplora.example/api ", out var address, out _));
        Assert.Equal("https://esplora.example/api/", address!.AbsoluteUri);
    }

    [Fact]
    public async Task An_added_endpoint_comes_last_on_and_named_after_its_host()
    {
        var sources = Create(Endpoints(Fast));

        var (endpoint, saved) = sources.Add(Chain.Bitcoin, new Uri("https://esplora.example/api"), "esplora", " key ");
        await saved;

        Assert.Equal("esplora.example", endpoint.Name);
        Assert.Equal("https://esplora.example/api/", endpoint.Address.AbsoluteUri);
        Assert.Equal("key", endpoint.ApiKey);
        Assert.True(endpoint.Enabled);
        Assert.Equal(endpoint, sources.Get(Chain.Bitcoin).Endpoints[^1].Endpoint);
        Assert.Throws<ArgumentException>(() => sources.Add(Chain.Bitcoin, new Uri(Slow), "etherscan", null));
    }

    [Fact]
    public async Task Prova_shows_the_height_the_blocks_hash_and_the_time_without_keeping_anything()
    {
        var sources = Create(Endpoints(Fast));
        Height(Slow, 921_404, 38);
        var hash = new string('0', 8) + new string('a', 52) + "3f2a";
        _http.AnswerBytes(Slow + "block-height/921404", Encoding.ASCII.GetBytes(hash), 200);

        var tried = await sources.TryAsync(Chain.Bitcoin, new Uri(Slow), "esplora", null);

        Assert.Equal(ProbeOutcome.Answered, tried.Outcome);
        Assert.Equal(921_404, tried.Height);
        Assert.Equal(hash, tried.BlockHash);
        Assert.Equal(TimeSpan.FromMilliseconds(38), tried.Duration);
        Assert.Equal(Guid.Empty, tried.EndpointId);
        Assert.Single(sources.Get(Chain.Bitcoin).Endpoints);
        Assert.Null(sources.Get(Chain.Bitcoin).ProbedAt);
    }

    [Fact]
    public async Task Prova_without_an_exit_sends_nothing_and_says_why()
    {
        var sources = Create(Endpoints(Fast));
        _http.Connected = false;

        var tried = await sources.TryAsync(Chain.Bitcoin, new Uri(Slow), "esplora", null);

        Assert.Equal(ProbeOutcome.NotSent, tried.Outcome);
        Assert.Empty(_http.Sent);
        Assert.NotNull(tried.Problem);
    }

    [Fact]
    public async Task A_key_from_the_vault_goes_as_a_header_and_never_in_the_address()
    {
        _keys.Save(new ApiKey("esplora-lan", string.Empty, "s3cret"));
        var sources = Create(Endpoints(Fast));
        var (endpoint, _) = sources.Add(Chain.Bitcoin, new Uri(Slow), "esplora", "esplora-lan");
        await sources.RemoveAsync(Chain.Bitcoin, sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id);
        Height(Slow, 921_404, 50);

        await sources.ProbeAsync(Chain.Bitcoin);

        var request = Assert.Single(_http.Sent);
        Assert.Equal(new ExitCredential("Authorization", "Bearer s3cret"), request.Credential);
        Assert.DoesNotContain("s3cret", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", request.Credential!.ToString(), StringComparison.Ordinal);
        Assert.Equal(ProbeOutcome.Answered, sources.Get(Chain.Bitcoin).Endpoints[0].Probe!.Outcome);
        Assert.Equal(endpoint.Id, sources.Get(Chain.Bitcoin).InUse!.Endpoint.Id);
    }

    [Fact]
    public async Task A_key_that_is_missing_sends_nothing()
    {
        var sources = Create(Endpoints(Fast));
        sources.Add(Chain.Bitcoin, new Uri(Slow), "esplora", "missing");
        await sources.SetEnabledAsync(Chain.Bitcoin, sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id, false);

        await sources.ProbeAsync(Chain.Bitcoin);

        Assert.Empty(_http.Sent);
        var probe = sources.Get(Chain.Bitcoin).Endpoints[1].Probe!;
        Assert.Equal(ProbeOutcome.NotSent, probe.Outcome);
        Assert.Contains("missing", probe.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_endpoints_are_kept_in_the_programs_data_without_keys()
    {
        _keys.Save(new ApiKey("esplora-lan", string.Empty, "s3cret"));
        var sources = Create(new BitcoinSourceOptions(), _directory);
        var (added, saved) = sources.Add(Chain.Bitcoin, new Uri(Slow), "esplora", "esplora-lan");
        await saved;
        await sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        await sources.MoveAsync(Chain.Bitcoin, added.Id, -2);

        var text = await File.ReadAllTextAsync(Path.Combine(_directory, "sources.json"));
        var again = Create(new BitcoinSourceOptions(), _directory).Get(Chain.Bitcoin);

        Assert.DoesNotContain("s3cret", text, StringComparison.Ordinal);
        Assert.Contains("\"Priority\"", text, StringComparison.Ordinal);
        Assert.Equal(SourceMode.Priority, again.Mode);
        Assert.Equal(["slow.example", "mempool.space", "Blockstream"], again.Endpoints.Select(e => e.Endpoint.Name));
        Assert.Equal("esplora-lan", again.Endpoints[0].Endpoint.ApiKey);
    }

    [Fact]
    public async Task A_file_changed_by_hand_keeps_only_what_can_be_used()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "sources.json"), """
            {"Chains":[{"Chain":"Bitcoin","Mode":"Priority","Endpoints":[
              {"Id":"11111111-1111-1111-1111-111111111111","Name":"Plain","Address":"http://plain.example/api/",
               "Api":"esplora","ApiKey":null,"Enabled":true},
              {"Id":"22222222-2222-2222-2222-222222222222","Name":"Other","Address":"https://other.example/api/",
               "Api":"etherscan","ApiKey":null,"Enabled":true},
              {"Id":"33333333-3333-3333-3333-333333333333","Name":"","Address":"https://good.example/api",
               "Api":"esplora","ApiKey":" ","Enabled":false}]}]}
            """);

        var bitcoin = Create(new BitcoinSourceOptions(), _directory).Get(Chain.Bitcoin);

        var only = Assert.Single(bitcoin.Endpoints).Endpoint;
        Assert.Equal("good.example", only.Name);
        Assert.Equal("https://good.example/api/", only.Address.AbsoluteUri);
        Assert.Null(only.ApiKey);
        Assert.False(only.Enabled);
        Assert.Null(bitcoin.InUse);
    }

    [Fact]
    public void A_file_that_cannot_be_read_gives_the_defaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "sources.json"), "{ not json");

        var bitcoin = Create(new BitcoinSourceOptions(), _directory).Get(Chain.Bitcoin);

        Assert.Equal(["mempool.space", "Blockstream"], bitcoin.Endpoints.Select(e => e.Endpoint.Name));
    }

    [Fact]
    public async Task A_cancelled_round_is_not_kept()
    {
        var sources = Create(Endpoints(Fast));
        Height(Fast, 921_404, 180);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sources.ProbeAsync(Chain.Bitcoin, cancel.Token));
        Assert.Null(sources.Get(Chain.Bitcoin).ProbedAt);
    }

    [Fact]
    public async Task One_behind_is_still_asked_when_the_one_that_was_highest_is_switched_off()
    {
        var sources = Create(Endpoints(Fast, Lagging));
        Height(Fast, 921_404, 180);
        Height(Lagging, 921_403, 38);
        await sources.ProbeAsync(Chain.Bitcoin);

        await sources.SetEnabledAsync(Chain.Bitcoin, sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id, false);

        var planned = Assert.Single(sources.Plan(Chain.Bitcoin).Endpoints);
        Assert.Equal(Lagging, planned.Endpoint.Address.AbsoluteUri);
        Assert.Equal(Lagging, sources.Get(Chain.Bitcoin).InUse!.Endpoint.Address.AbsoluteUri);
    }

    [Fact]
    public async Task No_star_when_every_endpoint_failed_its_probe()
    {
        var sources = Create(Endpoints(Fast, Slow));
        _http.FailingHosts.Add("fast.example");
        _http.FailingHosts.Add("slow.example");

        await sources.ProbeAsync(Chain.Bitcoin);

        Assert.Null(sources.Get(Chain.Bitcoin).InUse);
        Assert.Equal(2, sources.Plan(Chain.Bitcoin).Endpoints.Count);
    }

    [Fact]
    public async Task A_round_that_sends_nothing_does_not_drop_a_round_under_way()
    {
        var sources = Create(Endpoints(Fast));
        Height(Fast, 921_404, 180);
        var gate = new TaskCompletionSource();
        _http.Gate = gate.Task;
        var running = sources.ProbeAsync(Chain.Bitcoin);
        _vault.State = VaultState.Locked;

        Assert.Equal(ProbeRun.NotSent, await sources.ProbeAsync(Chain.Bitcoin));
        gate.SetResult();
        Assert.Equal(ProbeRun.Probed, await running);

        Assert.NotNull(sources.Get(Chain.Bitcoin).ProbedAt);
    }

    private ChainSources Create(BitcoinSourceOptions options, string? directory = null) =>
        new([new EsploraApi(options)], _http, _keys, _vault, _clock, new ChainSourcesOptions { Directory = directory });

    private static BitcoinSourceOptions Endpoints(params string[] addresses) =>
        new() { DefaultEndpoints = [.. addresses.Select(address => new Uri(address))] };

    private void Height(string address, long height, int milliseconds)
    {
        _http.AnswerBytes(address + "blocks/tip/height", Encoding.ASCII.GetBytes(height.ToString(
            System.Globalization.CultureInfo.InvariantCulture)), 200);
        _http.Milliseconds[new Uri(address).Host] = milliseconds;
    }
}
