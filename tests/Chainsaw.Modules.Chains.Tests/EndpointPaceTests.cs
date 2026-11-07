using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The endpoints, second part (etapp 25): each endpoint's own pace, <c>429</c> and <c>Retry-After</c> followed and the
/// lookups spread over the endpoints, a quiet fallback that comes back, and every attempt sealed. Never the network:
/// the exit is a fake, and every wait moves the test's clock.
/// </summary>
public sealed class EndpointPaceTests : IDisposable
{
    private const string Second = "https://second.example/api/";

    private readonly SourceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private string Tip => Fixtures.Url("blocks/tip/height");

    private string SecondTip => Second + "blocks/tip/height";

    [Fact]
    public async Task One_endpoint_is_never_asked_faster_than_its_pace()
    {
        _harness.AnswerAll();

        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(3, _harness.Http.Sent.Count);
        Assert.Equal([SourcePace.Public, SourcePace.Public], _harness.Clock.Waits);
    }

    [Fact]
    public async Task The_lookups_are_spread_over_the_endpoints_instead_of_waiting_for_one()
    {
        _harness.AnswerAll();
        await AddSecondAsync();
        _harness.Http.Answer(SecondTip, "tip-height.txt");

        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(["esplora.test", "second.example"], _harness.Http.Sent.Select(r => r.Uri.Host));
        Assert.All(_harness.Clock.Waits, wait => Assert.Equal(TimeSpan.Zero, wait));
    }

    [Fact]
    public async Task A_429_with_Retry_After_is_waited_for_while_the_others_answer_and_it_is_not_failed()
    {
        _harness.AnswerAll();
        _harness.Http.AnswerBusy(Tip, TimeSpan.FromSeconds(90));
        var second = await AddSecondAsync();
        _harness.Http.Answer(SecondTip, "tip-height.txt");
        var started = _harness.Clock.UtcNow;

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(["esplora.test", "second.example"], _harness.Http.Sent.Select(r => r.Uri.Host));
        Assert.Equal([429, 200], lookup.Origins.Select(origin => origin.StatusCode));
        Assert.Equal(2, _harness.EndpointColumns().Count);
        var first = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0];
        Assert.False(first.FailedSince);
        Assert.Equal(started.AddSeconds(90), first.WaitUntil);
        Assert.Null(_harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(e => e.Endpoint.Id == second.Id).WaitUntil);

        // While it waits it is not asked; afterwards it comes back by itself.
        _harness.Http.Sent.Clear();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        Assert.Equal(["second.example"], _harness.Http.Sent.Select(r => r.Uri.Host));

        _harness.Http.Answer(Tip, "tip-height.txt");
        _harness.Clock.UtcNow = started.AddSeconds(91);
        _harness.Http.Sent.Clear();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        Assert.Equal(["esplora.test"], _harness.Http.Sent.Select(r => r.Uri.Host));
        Assert.Null(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].WaitUntil);
    }

    [Fact]
    public async Task A_429_without_Retry_After_is_waited_for_the_default_time()
    {
        _harness.AnswerAll();
        _harness.Http.AnswerBusy(Tip, retryAfter: null);
        var started = _harness.Clock.UtcNow;

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Contains("vänta", lookup.Problem, StringComparison.Ordinal);
        var endpoint = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0];
        Assert.Equal(started + SourceLookups.DefaultBusyWait, endpoint.WaitUntil);
        Assert.False(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].FailedSince);
    }

    [Fact]
    public async Task A_lone_endpoint_that_asks_for_a_short_wait_is_waited_for_and_the_wait_is_told()
    {
        _harness.AnswerAll();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        _harness.Sources.AskedToWait(Chain.Bitcoin, _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id,
            TimeSpan.FromSeconds(20));
        var told = new List<DateTimeOffset?>();
        _harness.Sources.Changed += (_, _) => told.Add(_harness.Sources.Get(Chain.Bitcoin).WaitingUntil);
        var started = _harness.Clock.UtcNow;

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Contains(TimeSpan.FromSeconds(20), _harness.Clock.Waits);
        Assert.Contains(started.AddSeconds(20), told);
        Assert.Null(_harness.Sources.Get(Chain.Bitcoin).WaitingUntil);
    }

    [Fact]
    public async Task When_every_endpoint_asks_for_a_long_wait_nothing_is_sent_and_the_case_answers_honestly()
    {
        _harness.AnswerAll();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        _harness.Clock.UtcNow = _harness.Clock.UtcNow.AddMinutes(5);
        _harness.Sources.AskedToWait(Chain.Bitcoin, _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id,
            TimeSpan.FromMinutes(10));
        _harness.Http.Sent.Clear();

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Empty(_harness.Http.Sent);
        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, lookup.Status);
        Assert.Contains("ber Chainsaw vänta", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_503_with_Retry_After_asks_to_wait_and_one_without_is_a_failure()
    {
        _harness.AnswerAll();
        _harness.Http.AnswerBusy(Tip, TimeSpan.FromSeconds(40), status: 503);
        var second = await AddSecondAsync();
        _harness.Http.AnswerBusy(SecondTip, retryAfter: null, status: 503);

        await _harness.Source.GetTipHeightAsync(_harness.Case);

        var view = _harness.Sources.Get(Chain.Bitcoin);
        Assert.NotNull(view.Endpoints[0].WaitUntil);
        Assert.False(view.Endpoints[0].FailedSince);
        var other = view.Endpoints.Single(e => e.Endpoint.Id == second.Id);
        Assert.Null(other.WaitUntil);
        Assert.True(other.FailedSince);
    }

    [Fact]
    public async Task A_failed_endpoint_is_left_quietly_and_comes_back_in_its_place_after_its_pause()
    {
        _harness.AnswerAll();
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        await AddSecondAsync();
        _harness.Http.Answer(SecondTip, "tip-height.txt");
        _harness.Http.FailingHosts.Add("esplora.test");

        var quiet = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, quiet.Status);
        Assert.Null(quiet.Problem);
        Assert.Equal("second.example", _harness.Sources.Plan(Chain.Bitcoin).Endpoints[0].Endpoint.Name);

        // Within its pause the others come first; after it, it is asked in its own place again and answers.
        _harness.Clock.UtcNow += ChainSources.FirstPause - TimeSpan.FromSeconds(5);
        Assert.Equal("second.example", _harness.Sources.Plan(Chain.Bitcoin).Endpoints[0].Endpoint.Name);
        _harness.Clock.UtcNow += TimeSpan.FromSeconds(10);
        Assert.Equal("esplora.test", _harness.Sources.Plan(Chain.Bitcoin).Endpoints[0].Endpoint.Name);
        _harness.Http.FailingHosts.Clear();
        _harness.Http.Sent.Clear();

        await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal("esplora.test", _harness.Http.Sent[0].Uri.Host);
        Assert.False(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].FailedSince);
    }

    [Fact]
    public async Task Each_failure_in_a_row_doubles_the_pause_up_to_the_longest()
    {
        _harness.AnswerAll();
        var id = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;
        await AddSecondAsync();
        var started = _harness.Clock.UtcNow;

        for (var i = 0; i < 8; i++)
        {
            _harness.Sources.Failed(Chain.Bitcoin, id);
        }

        _harness.Clock.UtcNow = started + ChainSources.LongestPause - TimeSpan.FromSeconds(1);
        Assert.Equal("second.example", _harness.Sources.Plan(Chain.Bitcoin).Endpoints[0].Endpoint.Name);
        _harness.Clock.UtcNow = started + ChainSources.LongestPause + TimeSpan.FromSeconds(1);
        Assert.Equal(id, _harness.Sources.Plan(Chain.Bitcoin).Endpoints[0].Endpoint.Id);
    }

    [Fact]
    public async Task A_dead_endpoint_and_a_waiting_one_still_leave_the_case_answer_with_every_attempt_sealed()
    {
        _harness.AnswerAll();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        _harness.Clock.UtcNow = _harness.Clock.UtcNow.AddMinutes(5);
        await AddSecondAsync();
        _harness.Http.AnswerBusy(SecondTip, TimeSpan.FromSeconds(30));
        _harness.Http.AnswerBytes(Tip, "broken"u8.ToArray(), 500);

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, lookup.Status);
        Assert.Equal(3, _harness.EndpointColumns().Count);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Blockscout_saying_rate_limit_in_its_body_is_a_wait_not_a_broken_source()
    {
        using var ethereum = new EthereumHarness();
        ethereum.Http.Answer(
            EthereumHarness.Base + "?module=block&action=eth_block_number", "../Ethereum/rate-limit.json");

        var lookup = await ethereum.Source.GetTipHeightAsync(ethereum.Case);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        var endpoint = ethereum.Sources.Get(Chain.Ethereum).Endpoints[0];
        Assert.NotNull(endpoint.WaitUntil);
        Assert.False(endpoint.FailedSince);
    }

    /// <summary>Adds the second endpoint and waits until it is written.</summary>
    private async Task<SourceEndpoint> AddSecondAsync()
    {
        var (endpoint, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        await saved;
        return endpoint;
    }
}
