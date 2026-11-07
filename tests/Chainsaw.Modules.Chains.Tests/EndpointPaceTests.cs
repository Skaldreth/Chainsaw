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

    [Fact]
    public async Task Spreading_never_picks_an_endpoint_behind_while_one_that_answers_only_waits_for_its_pace()
    {
        _harness.AnswerAll();
        await AddSecondAsync();
        Height(Tip, 800_010);
        Height(SecondTip, 800_005);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        Height(SecondTip, 800_010);
        _harness.Http.Sent.Clear();

        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(["esplora.test", "esplora.test"], _harness.Http.Sent.Select(r => r.Uri.Host));
        Assert.Contains(SourcePace.Public, _harness.Clock.Waits);
    }

    [Fact]
    public async Task Spreading_never_picks_an_endpoint_in_its_pause_while_one_that_answers_only_waits_for_its_pace()
    {
        _harness.AnswerAll();
        var second = await AddSecondAsync();
        _harness.Http.Answer(SecondTip, "tip-height.txt");
        _harness.Sources.Failed(Chain.Bitcoin, second.Id);

        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(["esplora.test", "esplora.test"], _harness.Http.Sent.Select(r => r.Uri.Host));
    }

    [Fact]
    public async Task In_prioriterad_the_users_order_holds_and_the_next_is_taken_only_when_the_first_asks_to_wait()
    {
        _harness.AnswerAll();
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        await AddSecondAsync();
        _harness.Http.Answer(SecondTip, "tip-height.txt");

        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        _harness.Sources.AskedToWait(Chain.Bitcoin, _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id,
            TimeSpan.FromSeconds(60));
        await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(["esplora.test", "esplora.test", "second.example"], _harness.Http.Sent.Select(r => r.Uri.Host));
    }

    [Fact]
    public void A_turn_that_is_not_used_is_given_back_unless_a_later_one_was_taken()
    {
        var pacer = new EndpointPacer(_harness.Clock);
        var endpoint = new PlannedEndpoint(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint, new EsploraApi(
            new BitcoinSourceOptions()));
        var first = pacer.Take([endpoint], SourceLookups.LongestWait).Turn!;
        var second = pacer.Take([endpoint], SourceLookups.LongestWait).Turn!;
        var third = pacer.Take([endpoint], SourceLookups.LongestWait).Turn!;

        pacer.Release(second);
        Assert.Equal(SourcePace.Public * 3, pacer.Take([endpoint], SourceLookups.LongestWait).Turn!.Wait);

        var fourth = pacer.Take([endpoint], SourceLookups.LongestWait).Turn!;
        pacer.Release(fourth);
        Assert.Equal(SourcePace.Public * 4, pacer.Take([endpoint], SourceLookups.LongestWait).Turn!.Wait);
        Assert.Equal(TimeSpan.Zero, first.Wait);
        Assert.Equal(SourcePace.Public * 2, third.Wait);
    }

    [Fact]
    public async Task A_cancelled_lookup_gives_its_turn_back_and_ends_its_wait_notice()
    {
        _harness.AnswerAll();
        var id = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;
        _harness.Sources.AskedToWait(Chain.Bitcoin, id, TimeSpan.FromSeconds(20));
        using var cancel = new CancellationTokenSource();
        _harness.Clock.OnDelay = () => cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _harness.Source.GetTipHeightAsync(_harness.Case, cancel.Token));

        Assert.Null(_harness.Sources.Get(Chain.Bitcoin).WaitingUntil);
        _harness.Clock.OnDelay = null;
        _harness.Clock.UtcNow += TimeSpan.FromSeconds(21);
        _harness.Clock.Waits.Clear();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        Assert.DoesNotContain(_harness.Clock.Waits, wait => wait > TimeSpan.Zero);
    }

    [Fact]
    public async Task When_only_the_pace_queue_is_full_the_lookup_says_so_and_not_that_a_source_asked()
    {
        _harness.AnswerAll();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        var endpoint = new PlannedEndpoint(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint, new EsploraApi(
            new BitcoinSourceOptions()));
        while (_harness.Sources.Pacer.Take([endpoint], SourceLookups.LongestWait).Turn is not null)
        {
        }

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Contains("egen takt", lookup.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("ber Chainsaw", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_probe_round_that_only_waited_keeps_the_endpoints_earlier_probe()
    {
        _harness.AnswerAll();
        Height(Tip, 800_010);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        var id = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;
        _harness.Sources.AskedToWait(Chain.Bitcoin, id, TimeSpan.FromMinutes(5));
        _harness.Http.Sent.Clear();

        await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        Assert.Empty(_harness.Http.Sent);
        var view = _harness.Sources.Get(Chain.Bitcoin);
        Assert.Equal(ProbeOutcome.Answered, view.Endpoints[0].Probe!.Outcome);
        Assert.Single(_harness.Sources.Plan(Chain.Bitcoin, inStep: true).Endpoints);
    }

    [Fact]
    public async Task A_kept_probe_is_measured_against_the_new_rounds_height_and_is_not_in_step_when_left_behind()
    {
        _harness.AnswerAll();
        var second = await AddSecondAsync();
        Height(Tip, 800_010);
        Height(SecondTip, 800_010);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        var first = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;
        _harness.Sources.AskedToWait(Chain.Bitcoin, first, TimeSpan.FromMinutes(5));
        Height(SecondTip, 800_013);

        await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        var kept = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Probe!;
        Assert.Equal(ProbeOutcome.Behind, kept.Outcome);
        Assert.Equal(3, kept.BlocksBehind);
        var inStep = _harness.Sources.Plan(Chain.Bitcoin, inStep: true).Endpoints;
        Assert.Equal(second.Id, Assert.Single(inStep).Endpoint.Id);
        Assert.False(_harness.Sources.TrustsNotFound(Chain.Bitcoin, first));
    }

    [Fact]
    public async Task A_new_answer_lower_than_a_kept_probe_is_behind_and_its_not_found_is_not_believed()
    {
        _harness.AnswerAll();
        var second = await AddSecondAsync();
        Height(Tip, 900_000);
        Height(SecondTip, 899_000);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        var first = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;
        _harness.Sources.AskedToWait(Chain.Bitcoin, first, TimeSpan.FromMinutes(5));
        Height(SecondTip, 899_050);

        await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        var view = _harness.Sources.Get(Chain.Bitcoin);
        Assert.Equal(ProbeOutcome.Answered, view.Endpoints[0].Probe!.Outcome);
        var lower = view.Endpoints.Single(e => e.Endpoint.Id == second.Id).Probe!;
        Assert.Equal(ProbeOutcome.Behind, lower.Outcome);
        Assert.Equal(950, lower.BlocksBehind);
        Assert.False(_harness.Sources.TrustsNotFound(Chain.Bitcoin, second.Id));
        Assert.Equal(first, Assert.Single(_harness.Sources.Plan(Chain.Bitcoin, inStep: true).Endpoints).Endpoint.Id);
    }

    [Fact]
    public async Task A_kept_probe_that_was_behind_stays_behind_when_nothing_higher_answers_this_round()
    {
        _harness.AnswerAll();
        var second = await AddSecondAsync();
        Height(Tip, 900_000);
        Height(SecondTip, 899_000);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        _harness.Sources.AskedToWait(Chain.Bitcoin, second.Id, TimeSpan.FromMinutes(5));
        _harness.Http.AnswerBytes(Tip, "broken"u8.ToArray(), 500);

        await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        var kept = _harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(e => e.Endpoint.Id == second.Id).Probe!;
        Assert.Equal(ProbeOutcome.Behind, kept.Outcome);
        Assert.Equal(1_000, kept.BlocksBehind);
        Assert.False(_harness.Sources.TrustsNotFound(Chain.Bitcoin, second.Id));
        Assert.Empty(_harness.Sources.Plan(Chain.Bitcoin, inStep: true).Endpoints);
    }

    [Fact]
    public async Task A_probe_round_that_only_waited_keeps_a_failed_lookups_pause()
    {
        _harness.AnswerAll();
        Height(Tip, 800_010);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        var id = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;
        _harness.Sources.Failed(Chain.Bitcoin, id);
        _harness.Sources.AskedToWait(Chain.Bitcoin, id, TimeSpan.FromMinutes(5));

        await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        Assert.True(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].FailedSince);
        Assert.Empty(_harness.Sources.Plan(Chain.Bitcoin, inStep: true).Endpoints);
    }

    [Fact]
    public async Task A_probe_is_not_sent_when_the_endpoint_asked_to_wait_while_the_probe_waited_for_its_turn()
    {
        _harness.AnswerAll();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        var id = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;
        _harness.Clock.OnDelay = () => _harness.Sources.AskedToWait(Chain.Bitcoin, id, TimeSpan.FromSeconds(60));
        _harness.Http.Sent.Clear();

        await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        Assert.Empty(_harness.Http.Sent);
        Assert.Equal(ProbeOutcome.NotSent, _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Probe!.Outcome);
    }

    [Fact]
    public async Task A_503_that_asks_for_no_wait_is_a_failure_and_the_endpoint_is_paused()
    {
        _harness.AnswerAll();
        _harness.Http.AnswerBusy(Tip, TimeSpan.Zero, status: 503);

        await _harness.Source.GetTipHeightAsync(_harness.Case);

        var endpoint = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0];
        Assert.True(endpoint.FailedSince);
        Assert.Null(endpoint.WaitUntil);
    }

    [Fact]
    public async Task A_429_that_asks_for_no_wait_is_still_waited_for_a_moment()
    {
        _harness.AnswerAll();
        _harness.Http.AnswerBusy(Tip, TimeSpan.Zero);
        var started = _harness.Clock.UtcNow;

        await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(started + SourceLookups.NoticeableWait,
            _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].WaitUntil);
    }

    /// <summary>Answers an endpoint's height.</summary>
    private void Height(string url, long height) => _harness.Http.AnswerBytes(
        url, System.Text.Encoding.ASCII.GetBytes(height.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        200);

    /// <summary>Adds the second endpoint and waits until it is written.</summary>
    private async Task<SourceEndpoint> AddSecondAsync()
    {
        var (endpoint, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        await saved;
        return endpoint;
    }
}
