using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The own node's pinned certificate in the sources (sketch KL3b): only an endpoint marked local is pinned, every
/// request to it carries the pin, a changed certificate refuses the node and is told once while the lookup goes on to
/// the other endpoints, the old pin is kept with when and by whom, and the pin lives in the program's data. Never the
/// network.
/// </summary>
public sealed class EndpointPinTests : IDisposable
{
    private const string Own = "https://esplora.lan/api/";

    private static readonly string Pinned = new('A', CertificatePin.Length);

    private static readonly string Other = new('B', CertificatePin.Length);

    private readonly SourceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_pinned_node_is_asked_with_its_pin_and_answers()
    {
        _harness.AnswerAll();
        var own = await OwnFirstAsync();
        await _harness.Sources.PinAsync(Chain.Bitcoin, own.Id, Pinned, "Anna Lind");

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(Pinned, Assert.Single(_harness.Local.Sent).CertificatePin);
        Assert.Empty(_harness.Http.Sent);
        var pin = Endpoint(own.Id).Pin!;
        Assert.Equal(Pinned, pin.Fingerprint);
        Assert.Equal("Anna Lind", pin.PinnedBy);
        Assert.Equal(_harness.Clock.UtcNow, pin.PinnedAt);
    }

    [Fact]
    public async Task A_changed_certificate_refuses_the_node_tells_it_once_and_the_other_endpoints_answer()
    {
        _harness.AnswerAll();
        var own = await OwnFirstAsync();
        await _harness.Sources.PinAsync(Chain.Bitcoin, own.Id, Pinned, "Anna Lind");
        _harness.Local.Shows = Other;
        var told = new List<SourceEndpoint>();
        _harness.Sources.CertificateRefused += (_, endpoint) => told.Add(endpoint);

        var first = await _harness.Source.GetTipHeightAsync(_harness.Case);
        var second = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, first.Status);
        Assert.Equal(LookupStatus.Fetched, second.Status);
        Assert.NotEmpty(_harness.Http.Sent);
        Assert.Equal(own.Id, Assert.Single(told).Id);
        Assert.True(_harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(view => view.Endpoint.Id == own.Id)
            .CertificateRefused);
    }

    [Fact]
    public async Task Trusting_the_new_certificate_keeps_the_old_with_when_and_by_whom_and_tells_a_new_change_again()
    {
        _harness.AnswerAll();
        var own = await OwnFirstAsync();
        await _harness.Sources.PinAsync(Chain.Bitcoin, own.Id, Pinned, "Anna Lind");
        _harness.Local.Shows = Other;
        var told = 0;
        _harness.Sources.CertificateRefused += (_, _) => told++;
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        _harness.Clock.UtcNow += TimeSpan.FromHours(2);

        await _harness.Sources.PinAsync(Chain.Bitcoin, own.Id, Other, "Bo Ek");
        _harness.Local.Shows = Pinned;
        await _harness.Source.GetTipHeightAsync(_harness.Case);

        var endpoint = Endpoint(own.Id);
        Assert.Equal(Other, endpoint.Pin!.Fingerprint);
        var former = Assert.Single(endpoint.FormerPins);
        Assert.Equal(Pinned, former.Fingerprint);
        Assert.Equal("Anna Lind", former.PinnedBy);
        Assert.Equal("Bo Ek", former.EndedBy);
        Assert.Equal(_harness.Clock.UtcNow, former.EndedAt);
        Assert.Equal(2, told);
        Assert.True(_harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(view => view.Endpoint.Id == own.Id)
            .CertificateRefused);
    }

    [Fact]
    public async Task Removing_the_pin_keeps_it_among_the_former_and_asks_without_one()
    {
        _harness.AnswerAll();
        var own = await OwnFirstAsync();
        await _harness.Sources.PinAsync(Chain.Bitcoin, own.Id, Pinned, "Anna Lind");

        await _harness.Sources.UnpinAsync(Chain.Bitcoin, own.Id, "Anna Lind");
        await _harness.Source.GetTipHeightAsync(_harness.Case);

        var endpoint = Endpoint(own.Id);
        Assert.Null(endpoint.Pin);
        Assert.Equal(Pinned, Assert.Single(endpoint.FormerPins).Fingerprint);
        Assert.Null(Assert.Single(_harness.Local.Sent).CertificatePin);
    }

    [Fact]
    public async Task Only_an_endpoint_marked_local_is_pinned_or_read_and_a_fingerprint_must_be_one()
    {
        var (external, saved) = _harness.Sources.Add(
            Chain.Bitcoin, new Uri("https://chain.example/api/"), "esplora", null);
        await saved;
        var own = await OwnFirstAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Sources.PinAsync(Chain.Bitcoin, external.Id, Pinned, "Anna Lind"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Sources.ReadCertificateAsync(Chain.Bitcoin, external.Id));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Sources.PinAsync(Chain.Bitcoin, own.Id, "abc", "Anna Lind"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Sources.PinAsync(Chain.Bitcoin, own.Id, Pinned, " "));
        Assert.Null(Endpoint(external.Id).Pin);
    }

    [Fact]
    public async Task The_node_s_certificate_is_read_straight_from_it()
    {
        var own = await OwnFirstAsync();

        var shown = await _harness.Sources.ReadCertificateAsync(Chain.Bitcoin, own.Id);

        Assert.Equal(_harness.Local.Shows, shown.Fingerprint);
        Assert.Equal(new Uri(Own), Assert.Single(_harness.Local.CertificatesRead));
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task The_pin_is_kept_in_the_file_and_one_that_cannot_be_read_or_is_on_the_internet_is_dropped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chainsaw-pin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new ChainSourcesOptions { Directory = directory };
            var api = new EsploraApi(new BitcoinSourceOptions());
            var first = new ChainSources([api], _harness.Http, null, null, _harness.Clock, options, _harness.Local);
            var (own, saved) = first.Add(Chain.Bitcoin, new Uri(Own), "esplora", null, local: true);
            await saved;
            await first.PinAsync(Chain.Bitcoin, own.Id, Pinned, "Anna Lind");
            await first.PinAsync(Chain.Bitcoin, own.Id, Other, "Bo Ek");

            var again = new ChainSources([api], _harness.Http, null, null, _harness.Clock, options)
                .Get(Chain.Bitcoin).Endpoints.Single(e => e.Endpoint.Id == own.Id).Endpoint;
            Assert.Equal(Other, again.Pin!.Fingerprint);
            Assert.Equal("Bo Ek", again.Pin.PinnedBy);
            Assert.Equal(Pinned, Assert.Single(again.FormerPins).Fingerprint);

            var path = Path.Combine(directory, ChainSourcesOptions.FileName);
            File.WriteAllText(path, $$"""
                {"Chains": [{"Chain": "Bitcoin", "Mode": "Fastest", "Endpoints": [
                  {"Id": "{{Guid.NewGuid()}}", "Address": "https://esplora.lan/api/", "Api": "esplora",
                   "Enabled": true, "Local": true,
                   "Pin": {"Fingerprint": "not-a-fingerprint", "PinnedAt": "2026-11-08T10:00:00Z", "PinnedBy": "X"}
                  },
                  {"Id": "{{Guid.NewGuid()}}", "Address": "https://chain.example/api/", "Api": "esplora",
                   "Enabled": true,
                   "Pin": {"Fingerprint": "{{Pinned}}", "PinnedAt": "2026-11-08T10:00:00Z", "PinnedBy": "X"}
                  }]}]}
                """);
            var edited = new ChainSources([api], _harness.Http, null, null, _harness.Clock, options).Get(Chain.Bitcoin);
            Assert.All(edited.Endpoints, view => Assert.Null(view.Endpoint.Pin));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void A_proxy_that_asks_for_a_login_is_the_way_outs_failure_not_the_endpoints()
    {
        Assert.True(ChainSources.IsTheWayOuts(new ExitException(ExitError.ProxyLoginRequired)));
        Assert.False(ChainSources.IsTheWayOuts(new ExitException(ExitError.CertificateChanged)));
    }

    /// <summary>Adds the own node first in the list, in priority order, and waits until it is written.</summary>
    private async Task<SourceEndpoint> OwnFirstAsync()
    {
        var (endpoint, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Own), "esplora", null, local: true);
        await saved;
        await _harness.Sources.MoveAsync(Chain.Bitcoin, endpoint.Id, -5);
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        _harness.Local.Answer(Own + "blocks/tip/height", "tip-height.txt");
        return endpoint;
    }

    /// <summary>Gives an endpoint as the sources hold it now.</summary>
    private SourceEndpoint Endpoint(Guid id) =>
        _harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(view => view.Endpoint.Id == id).Endpoint;
}
