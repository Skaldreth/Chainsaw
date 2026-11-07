using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Lookups through the chain's endpoints (sketch KL1a): every sealed response says which endpoint it came from, the
/// next endpoint is asked when one fails, and nothing is asked when the chain is off.
/// </summary>
public sealed class EndpointLookupTests : IDisposable
{
    private const string Second = "https://second.example/api/";

    private readonly SourceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Every_sealed_response_says_which_endpoint_it_came_from()
    {
        _harness.AnswerAll();

        var lookup = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        var endpoint = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint;
        var origin = lookup.Origins[0];
        Assert.Equal(endpoint.Id, origin.EndpointId);
        Assert.Equal("esplora.test", origin.Endpoint);
        Assert.Equal("esplora", origin.Source);
        var row = _harness.EndpointColumns()[0];
        Assert.Equal(("esplora", endpoint.Id.ToString("D"), "esplora.test"), (row.Source, row.Id, row.Name));
        Assert.True(_harness.Verify().IsIntact);

        var again = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed, LookupMode.CaseOnly);
        Assert.Equal(endpoint.Id, again.Origins[0].EndpointId);
        Assert.Equal("esplora.test", again.Origins[0].Endpoint);
    }

    [Fact]
    public async Task A_dead_endpoint_is_passed_over_and_asked_last_afterwards()
    {
        _harness.AnswerAll();
        var (second, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        await saved;
        await _harness.Sources.MoveAsync(Chain.Bitcoin, second.Id, -1);
        _harness.Http.FailingHosts.Add("second.example");

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(["second.example", "esplora.test"], _harness.Http.Sent.Select(r => r.Uri.Host));
        Assert.Equal("esplora.test", Assert.Single(lookup.Origins).Endpoint);
        Assert.True(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].FailedSince);
        Assert.Equal("esplora.test", _harness.Sources.Get(Chain.Bitcoin).InUse!.Endpoint.Name);
    }

    [Fact]
    public async Task An_answer_that_cannot_be_used_is_sealed_and_the_next_endpoint_asked()
    {
        _harness.AnswerAll();
        var (second, _) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, second.Id, -1);
        _harness.Http.AnswerBytes(Second + "blocks/tip/height", "busy"u8.ToArray(), 503);

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(["second.example", "esplora.test"], lookup.Origins.Select(o => o.Endpoint));
        Assert.Equal(["second.example", "esplora.test"], _harness.EndpointColumns().Select(r => r.Name));
    }

    [Fact]
    public async Task Not_found_from_an_endpoint_that_answered_its_probe_is_an_answer_and_no_other_is_asked()
    {
        _harness.AnswerAll();
        _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        _harness.Http.FailingHosts.Add("second.example");
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        _harness.Http.Sent.Clear();

        var lookup = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(LookupStatus.NotFound, lookup.Status);
        Assert.Equal(["esplora.test"], _harness.Http.Sent.Select(r => r.Uri.Host));
        Assert.DoesNotContain(_harness.Sources.Get(Chain.Bitcoin).Endpoints, e => e.FailedSince);
    }

    [Fact]
    public async Task When_every_endpoint_fails_the_case_answers_with_every_failed_response()
    {
        _harness.AnswerAll();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        _harness.Http.AnswerBytes(Second + "blocks/tip/height", "busy"u8.ToArray(), 503);
        _harness.Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), "busy"u8.ToArray(), 500);

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, lookup.Status);
        Assert.Equal(3, lookup.Origins.Count);
        Assert.Contains("503", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Av_asks_nothing_and_the_case_answers()
    {
        _harness.AnswerAll();
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        _harness.Http.Sent.Clear();
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Off);

        var stored = await _harness.Source.GetTipHeightAsync(_harness.Case);
        var missing = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Empty(_harness.Http.Sent);
        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, stored.Status);
        Assert.Contains("Av", stored.Problem, StringComparison.Ordinal);
        Assert.Equal(LookupStatus.Offline, missing.Status);
    }

    [Fact]
    public async Task With_every_endpoint_off_nothing_is_asked()
    {
        var id = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint.Id;
        await _harness.Sources.SetEnabledAsync(Chain.Bitcoin, id, false);

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Empty(_harness.Http.Sent);
        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Contains("påslagen", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_exit_nothing_is_asked_and_no_endpoint_counts_as_failed()
    {
        _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        _harness.Http.Connected = false;

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Offline, lookup.Status);
        Assert.Equal(1, _harness.Http.Refused);
        Assert.DoesNotContain(_harness.Sources.Get(Chain.Bitcoin).Endpoints, e => e.FailedSince);
    }

    [Fact]
    public async Task Not_found_from_an_endpoint_that_lags_behind_asks_the_next()
    {
        await LaggingFirstAsync();

        var lookup = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal("second.example", _harness.Http.Sent[0].Uri.Host);
        Assert.Contains("esplora.test", lookup.Origins.Select(o => o.Endpoint));
    }

    [Fact]
    public async Task A_missing_key_puts_the_endpoint_last()
    {
        _harness.AnswerAll();
        var (second, _) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", "missing");
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, second.Id, -1);

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(["esplora.test"], _harness.Http.Sent.Select(r => r.Uri.Host));
        Assert.True(_harness.Sources.Get(Chain.Bitcoin).Endpoints[0].FailedSince);
    }

    [Fact]
    public async Task Not_found_from_one_not_believed_and_a_failure_from_the_next_is_no_answer()
    {
        await LaggingFirstAsync();
        _harness.Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), "busy"u8.ToArray(), 500);

        var lookup = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Contains("500", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_found_from_an_endpoint_not_tried_yet_is_believed()
    {
        _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);

        var lookup = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(LookupStatus.NotFound, lookup.Status);
        Assert.Single(_harness.Http.Sent);
    }

    /// <summary>Puts an endpoint that lags behind first, in Prioriterad, after a round.</summary>
    private async Task LaggingFirstAsync()
    {
        _harness.AnswerAll();
        var (second, _) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        _harness.Http.AnswerBytes(Second + "blocks/tip/height", "1"u8.ToArray(), 200);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, second.Id, -1);
        _harness.Http.Sent.Clear();
    }
}
