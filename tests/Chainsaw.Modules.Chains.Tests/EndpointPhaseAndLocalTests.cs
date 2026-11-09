using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The endpoints, second part (etapp 25): one block's margin for ordinary lookups, the monitoring's demand for an
/// endpoint in step (<see cref="LookupMode.InStep"/>), and the user's own node on the local network, which is reached
/// only when the endpoint is marked local and is never reached through the exit. Never the network.
/// </summary>
public sealed class EndpointPhaseAndLocalTests : IDisposable
{
    private const string Lagging = "https://lagging.example/api/";

    private const string Own = "https://esplora.lan/api/";

    private const string OwnIp = "https://192.168.1.20/api/";

    private readonly SourceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void One_block_behind_is_not_behind_but_two_are()
    {
        var at = DateTimeOffset.UnixEpoch;
        EndpointProbe Answer(long height) =>
            new(Guid.NewGuid(), ProbeOutcome.Answered, height, null, TimeSpan.FromMilliseconds(50), at, null);

        var ranked = ChainSources.Ranked([Answer(100), Answer(99), Answer(98)]);

        Assert.Equal(ProbeOutcome.Answered, ranked[0].Outcome);
        Assert.Null(ranked[0].BlocksBehind);
        Assert.Equal(ProbeOutcome.Answered, ranked[1].Outcome);
        Assert.Equal(1, ranked[1].BlocksBehind);
        Assert.Null(ranked[1].Problem);
        Assert.Equal(ProbeOutcome.Behind, ranked[2].Outcome);
        Assert.Equal(2, ranked[2].BlocksBehind);
    }

    [Fact]
    public async Task An_ordinary_lookup_takes_an_endpoint_one_block_behind_but_the_monitoring_does_not()
    {
        _harness.AnswerAll();
        var lagging = await AddAsync(Lagging);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, lagging.Id, -1);
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        Height(Fixtures.Base, 800_010);
        Height(Lagging, 800_009);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        var address = Fixtures.Address("wpkh");
        _harness.Http.Answer(Lagging + $"address/{address.Value}/txs", "address-page-first.json");
        _harness.Http.Sent.Clear();

        var ordinary = await _harness.Source.GetAddressTransactionsAsync(_harness.Case, address);
        var monitoring = await _harness.Source.GetAddressTransactionsAsync(
            _harness.Case, address, null, LookupMode.InStep);

        Assert.Equal(LookupStatus.Fetched, ordinary.Status);
        Assert.Equal(LookupStatus.Fetched, monitoring.Status);
        Assert.Equal(["lagging.example", "esplora.test"], _harness.Http.Sent.Select(r => r.Uri.Host));
        Assert.Equal("esplora.test", Assert.Single(monitoring.Origins).Endpoint);
    }

    [Fact]
    public async Task The_monitoring_takes_the_chain_height_only_from_an_endpoint_in_step()
    {
        _harness.AnswerAll();
        var lagging = await AddAsync(Lagging);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, lagging.Id, -1);
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        Height(Fixtures.Base, 800_010);
        Height(Lagging, 800_009);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        _harness.Http.Sent.Clear();

        var ordinary = await _harness.Source.GetTipHeightAsync(_harness.Case);
        var monitoring = await _harness.Source.GetTipHeightAsync(_harness.Case, LookupMode.InStep);

        Assert.Equal(800_009, ordinary.Value!.Height);
        Assert.Equal(800_010, monitoring.Value!.Height);
        Assert.Equal(["lagging.example", "esplora.test"], _harness.Http.Sent.Select(r => r.Uri.Host));
    }

    [Fact]
    public async Task Not_found_only_from_endpoints_that_cannot_be_believed_is_a_failure_not_an_answer()
    {
        // The only endpoint failed its probe (its path changed, say): its 404 says nothing about the chain.
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        Assert.Equal(ProbeOutcome.Failed, _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Probe!.Outcome);

        var lookup = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Equal(SourceLookups.UntrustedNotFoundText, lookup.Problem);
    }

    [Fact]
    public async Task The_monitoring_sends_nothing_when_no_endpoint_is_known_to_be_in_step()
    {
        _harness.AnswerAll();

        var lookup = await _harness.Source.GetTransactionAsync(
            _harness.Case, Fixtures.Unconfirmed, LookupMode.InStep);

        Assert.Empty(_harness.Http.Sent);
        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Contains("i fas", lookup.Problem, StringComparison.Ordinal);
        Assert.True(_harness.Sources.Plan(Chain.Bitcoin, inStep: true).NoneInStep);
    }

    [Fact]
    public async Task The_monitoring_passes_over_an_endpoint_that_failed_since_its_probe()
    {
        _harness.AnswerAll();
        Height(Fixtures.Base, 800_010);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        var id = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;

        Assert.Single(_harness.Sources.Plan(Chain.Bitcoin, inStep: true).Endpoints);
        _harness.Sources.Failed(Chain.Bitcoin, id);
        Assert.Empty(_harness.Sources.Plan(Chain.Bitcoin, inStep: true).Endpoints);
    }

    [Fact]
    public async Task Without_an_exit_the_own_node_is_probed_and_kept_but_never_in_step()
    {
        var own = await AddAsync(Own, local: true);
        Height(Own, 800_010, local: true);
        _harness.Http.Connected = false;

        var run = await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        Assert.Equal(ProbeRun.OwnNodesOnly, run);
        Assert.Empty(_harness.Http.Sent);
        var view = _harness.Sources.Get(Chain.Bitcoin);
        var probe = view.Endpoints.Single(e => e.Endpoint.Id == own.Id).Probe!;
        Assert.Equal(ProbeOutcome.Answered, probe.Outcome);
        Assert.Equal(800_010, probe.Height);
        Assert.True(probe.Unreferenced);
        Assert.False(probe.IsInStep);

        // The endpoint through the exit was not tried: the round says nothing about it.
        Assert.Null(view.Endpoints.Single(e => !e.Endpoint.Local).Probe);

        // It answers ordinary lookups, but the monitoring never counts from its word alone.
        Assert.Contains(_harness.Sources.Plan(Chain.Bitcoin).Endpoints, e => e.Endpoint.Id == own.Id);
        Assert.Empty(_harness.Sources.Plan(Chain.Bitcoin, inStep: true).Endpoints);
    }

    [Fact]
    public async Task Without_an_exit_the_own_node_is_measured_against_the_height_the_exit_gave_before()
    {
        var own = await AddAsync(Own, local: true);
        Height(Fixtures.Base, 800_020);
        Height(Own, 800_020, local: true);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        Assert.True(_harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(e => e.Endpoint.Id == own.Id).Probe!
            .IsInStep);

        // The own node falls behind while the exit is closed: it is behind, and the other's probe stands.
        Height(Own, 800_010, local: true);
        _harness.Http.Connected = false;
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        var view = _harness.Sources.Get(Chain.Bitcoin);
        var probe = view.Endpoints.Single(e => e.Endpoint.Id == own.Id).Probe!;
        Assert.Equal(ProbeOutcome.Behind, probe.Outcome);
        Assert.Equal(10, probe.BlocksBehind);
        Assert.Equal(800_020, view.Endpoints.Single(e => !e.Endpoint.Local).Probe!.Height);

        // A round with the exit measures it again, and it is in step once it has caught up.
        Height(Own, 800_020, local: true);
        _harness.Http.Connected = true;
        Assert.Equal(ProbeRun.Probed, await _harness.Sources.ProbeAsync(Chain.Bitcoin));
        Assert.True(_harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(e => e.Endpoint.Id == own.Id).Probe!
            .IsInStep);
    }

    [Fact]
    public async Task Without_an_exit_and_no_own_node_nothing_is_kept_as_before()
    {
        Height(Fixtures.Base, 800_010);
        _harness.Http.Connected = false;

        Assert.Equal(ProbeRun.NotSent, await _harness.Sources.ProbeAsync(Chain.Bitcoin));
        Assert.Null(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Probe);
    }

    [Fact]
    public void A_source_without_the_choice_refuses_in_step_instead_of_ignoring_it()
    {
        IBitcoinSource source = new NoChoiceSource();

        // The refusal comes before any task is made: nothing could ever be sent in the wrong mode.
        Assert.Throws<NotSupportedException>(() =>
        {
            _ = source.GetAddressTransactionsAsync(null!, Fixtures.Address("wpkh"), null, LookupMode.InStep);
        });
    }

    [Theory]
    [InlineData("https://192.168.1.20/api/")]
    [InlineData("https://10.0.0.5/")]
    [InlineData("https://127.0.0.1:3002/")]
    [InlineData("https://localhost/")]
    [InlineData("https://[fd00::1]/")]
    public void An_address_on_the_computer_or_a_private_network_needs_the_mark_local(string text)
    {
        Assert.False(SourceAddress.TryRead(text, out _, out var problem));
        Assert.Equal(SourceAddress.LocalNeedsMarkText, problem);
        Assert.True(SourceAddress.TryRead(text, local: true, out _, out _));
        Assert.Throws<ArgumentException>(() => _harness.Sources.Add(Chain.Bitcoin, new Uri(text), "esplora", null));
    }

    [Theory]
    [InlineData("https://8.8.8.8/")]
    [InlineData("https://[2606:4700::1]/")]
    [InlineData("https://100.64.0.1/")]
    [InlineData("https://[2001::1]/")]
    [InlineData("https://[2002:c0a8:114::1]/")]
    [InlineData("https://[64:ff9b::c0a8:114]/")]
    public void A_local_endpoint_must_not_have_a_global_address(string text)
    {
        Assert.False(SourceAddress.TryRead(text, local: true, out _, out var problem));
        Assert.Equal(SourceAddress.LocalMustBeLocalText, problem);
        Assert.Throws<ArgumentException>(
            () => _harness.Sources.Add(Chain.Bitcoin, new Uri(text), "esplora", null, local: true));
    }

    [Fact]
    public void A_local_endpoint_is_still_only_https()
    {
        Assert.False(SourceAddress.TryRead("http://esplora.lan/", local: true, out _, out var problem));
        Assert.Equal("Bara https.", problem);
    }

    [Fact]
    public async Task A_local_endpoint_is_asked_straight_and_never_through_the_exit_and_its_response_says_local()
    {
        _harness.AnswerAll();
        var own = await AddAsync(OwnIp, local: true);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, own.Id, -1);
        _harness.Local.Answer(OwnIp + "blocks/tip/height", "tip-height.txt");

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Empty(_harness.Http.Sent);
        Assert.Equal("192.168.1.20", Assert.Single(_harness.Local.Sent).Uri.Host);
        Assert.Equal((null, ExitResponse.LocalRouteCode), _harness.ExitColumns()[^1]);
        Assert.True(own.Local);
        Assert.Equal(SourcePace.Local, own.Interval);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Every_other_endpoint_goes_through_the_exit_and_never_straight()
    {
        _harness.AnswerAll();
        await AddAsync(Own, local: true);

        await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Empty(_harness.Local.Sent);
        Assert.Equal("esplora.test", Assert.Single(_harness.Http.Sent).Uri.Host);
        Assert.Equal("vpn", _harness.ExitColumns()[^1].Route);
    }

    [Fact]
    public async Task Without_a_connected_exit_only_the_own_node_is_asked()
    {
        _harness.AnswerAll();
        await AddAsync(Own, local: true);
        _harness.Local.Answer(Own + "blocks/tip/height", "tip-height.txt");
        _harness.Http.Connected = false;

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal("esplora.lan", Assert.Single(lookup.Origins).Endpoint);
        Assert.Empty(_harness.Http.Sent);
        Assert.Equal(1, _harness.Http.Refused);
    }

    [Fact]
    public async Task Without_a_connected_exit_and_no_own_node_the_lookup_is_offline_as_before()
    {
        _harness.AnswerAll();
        _harness.Http.Connected = false;

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Offline, lookup.Status);
    }

    [Fact]
    public async Task An_untrusted_not_found_from_the_own_node_with_the_exit_closed_is_offline_not_not_found()
    {
        _harness.AnswerAll();
        Height(Fixtures.Base, 800_010);
        var own = await AddAsync(Own, local: true);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, own.Id, -1);

        // The own node answers no height (still syncing), so its probe fails and its not found is not believed.
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        _harness.Http.Connected = false;

        var lookup = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Equal(LookupStatus.Offline, lookup.Status);
        Assert.Single(_harness.Local.Sent, r => r.Uri.AbsolutePath.Contains("/tx/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_local_endpoint_without_a_way_to_the_own_node_fails_and_never_goes_through_the_exit()
    {
        var http = new FakeExitHttp(_harness.Clock);
        var sources = new ChainSources(
            [new EsploraApi(new BitcoinSourceOptions { DefaultEndpoints = [] })], http, null, null, _harness.Clock,
            new ChainSourcesOptions(), local: null, _harness.Clock.Delay);
        var (endpoint, _) = sources.Add(Chain.Bitcoin, new Uri(OwnIp), "esplora", null, local: true);

        await Assert.ThrowsAsync<ExitException>(
            () => sources.SendAsync(endpoint, ExitRequest.Get(new Uri(OwnIp + "blocks/tip/height")), default));
        var probe = await sources.TryAsync(Chain.Bitcoin, new Uri(OwnIp), "esplora", null, local: true);

        Assert.Empty(http.Sent);
        Assert.Equal(ProbeOutcome.Failed, probe.Outcome);
    }

    [Fact]
    public async Task Trying_a_local_address_asks_the_own_node()
    {
        Height(Own, 800_010, local: true);
        _harness.Local.Answer(Own + "block-height/800010", "block-real-header.txt");

        await _harness.Sources.TryAsync(Chain.Bitcoin, new Uri(Own), "esplora", null, local: true);

        Assert.Empty(_harness.Http.Sent);
        Assert.NotEmpty(_harness.Local.Sent);
    }

    [Fact]
    public async Task The_mark_and_the_pace_are_kept_in_the_file_and_a_file_cannot_make_a_private_address_external()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chainsaw-local-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new ChainSourcesOptions { Directory = directory };
            var api = new EsploraApi(new BitcoinSourceOptions());
            var first = new ChainSources([api], _harness.Http, null, null, _harness.Clock, options);
            var (own, saved) = first.Add(Chain.Bitcoin, new Uri(OwnIp), "esplora", null, local: true);
            await saved;

            var again = new ChainSources([api], _harness.Http, null, null, _harness.Clock, options)
                .Get(Chain.Bitcoin).Endpoints.Single(e => e.Endpoint.Id == own.Id).Endpoint;
            Assert.True(again.Local);
            Assert.Equal(SourcePace.Local, again.Interval);
            Assert.DoesNotContain("IntervalMilliseconds", File.ReadAllText(
                Path.Combine(directory, ChainSourcesOptions.FileName)), StringComparison.Ordinal);
            Assert.Equal(SourcePace.Public, first.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Interval);

            // A hand-edited file that unmarks a private address, or marks a global one, loses that endpoint.
            var path = Path.Combine(directory, ChainSourcesOptions.FileName);
            File.WriteAllText(path, $$"""
                {"Chains": [{"Chain": "Bitcoin", "Mode": "Fastest", "Endpoints": [
                  {"Id": "{{Guid.NewGuid()}}", "Address": "https://192.168.1.20/api/", "Api": "esplora",
                   "Enabled": true, "Local": false},
                  {"Id": "{{Guid.NewGuid()}}", "Address": "https://8.8.8.8/api/", "Api": "esplora",
                   "Enabled": true, "Local": true},
                  {"Id": "{{Guid.NewGuid()}}", "Address": "https://esplora.lan/api/", "Api": "esplora",
                   "Enabled": true, "Local": true, "IntervalMilliseconds": 999999999},
                  {"Id": "{{Guid.NewGuid()}}", "Address": "https://fast.example/api/", "Api": "esplora",
                   "Enabled": true, "IntervalMilliseconds": 0}]}]}
                """);
            var edited = new ChainSources([api], _harness.Http, null, null, _harness.Clock, options).Get(Chain.Bitcoin);
            Assert.Equal(2, edited.Endpoints.Count);
            var kept = edited.Endpoints[0].Endpoint;
            Assert.Equal("esplora.lan", kept.Name);
            Assert.True(kept.Local);
            Assert.Equal(SourcePace.Maximum, kept.Interval);

            // An endpoint on the internet is never asked faster than Chainsaw's own pace, whatever the file says.
            Assert.Equal(SourcePace.Public, edited.Endpoints[1].Endpoint.Interval);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Adds an endpoint and waits until it is written.</summary>
    private async Task<SourceEndpoint> AddAsync(string address, bool local = false)
    {
        var (endpoint, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(address), "esplora", null, local);
        await saved;
        return endpoint;
    }

    /// <summary>Answers an endpoint's height.</summary>
    private void Height(string address, long height, bool local = false)
    {
        var url = address + "blocks/tip/height";
        var body = Encoding.ASCII.GetBytes(height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (local)
        {
            _harness.Local.AnswerBytes(url, body, 200);
        }
        else
        {
            _harness.Http.AnswerBytes(url, body, 200);
        }
    }

    /// <summary>A source that has no choice of how to ask, as the tests' fakes in other modules.</summary>
    private sealed class NoChoiceSource : IBitcoinSource
    {
        public BitcoinNetwork Network => BitcoinNetwork.Mainnet;

        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            Cases.Contracts.ICase @case, BitcoinAddress address, Txid? after = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
            Cases.Contracts.ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
            Cases.Contracts.ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
            Cases.Contracts.ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
            Cases.Contracts.ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
            Cases.Contracts.ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
            Cases.Contracts.ICase @case, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }
}
