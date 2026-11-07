using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Watcher.Tests;

/// <summary>
/// The background watching from the plan to the case: the same check through the real Esplora source and endpoints,
/// the queue, the alert, and the hits sealed in the case when it is opened.
/// </summary>
public sealed class WatcherLoopTests : IDisposable
{
    private readonly World _world = new();

    public void Dispose() => _world.Dispose();

    private WatcherLoop Loop => _world.Background<WatcherLoop>();

    private IWatchQueue Queue => _world.App<IWatchQueue>();

    [Fact]
    public async Task A_hit_seen_when_Chainsaw_is_closed_is_queued_said_once_and_sealed_in_the_case_later()
    {
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        _world.WritePlan();
        _world.Height(800_010);
        _world.Clock.UtcNow += TimeSpan.FromMinutes(5);

        Assert.True(await Loop.RunOnceAsync());

        var id = Assert.Single(Queue.List(_world.Case.Info.Id));
        var entry = Queue.Read(_world.Case.Info.Id, id)!;
        Assert.Equal(QueuedKind.Checked, entry.Kind);
        Assert.Equal([World.Spent, World.Coinbase], entry.Matches.Select(match => match.Transaction));
        Assert.Equal(3, entry.Responses.Count);
        Assert.All(entry.Responses, response => Assert.Contains(
            BackgroundNames.Fetcher.Name, response.FetchedBy, StringComparison.Ordinal));
        Assert.Equal(800_010, entry.Checked!.Height);
        Assert.Equal([WatchAlerts.Sound], _world.Alerts.Raised);
        var status = _world.App<IBackgroundStatusFile>().Read()!;
        Assert.Equal((BackgroundState.Running, "esplora.test"), (status.State, status.InStepWith));
        Assert.True(Queue.HasHits());

        // No new block: nothing is asked again, and nothing is said twice.
        _world.Clock.UtcNow += TimeSpan.FromMinutes(2);
        var sent = _world.Http.Sent.Count;
        Assert.True(await Loop.RunOnceAsync());
        Assert.Single(Queue.List(_world.Case.Info.Id));
        Assert.Single(_world.Alerts.Raised);
        Assert.Equal(sent + 1, _world.Http.Sent.Count);

        var caseWatch = _world.App<IMonitoringService>().GetWatches(_world.Case).Single();
        Assert.Equal(caseWatch.StartHeight, entry.Start);
        Assert.Equal(caseWatch.Filter.Pace, entry.Filter.Pace);
        Assert.Equal(caseWatch.Filter.Direction, entry.Filter.Direction);
        var import = _world.App<IBackgroundWatches>().Import(_world.Case);
        Assert.Equal(string.Empty, string.Join(";", import.Problems));
        Assert.Equal(1, import.Entries);

        Assert.Equal(2, import.Hits.Count);
        var service = _world.App<IMonitoringService>();
        Assert.Equal(2, service.GetHits(_world.Case).Count);
        var sealedResponses = _world.Case.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT count(*) FROM chains_raw_responses");
            return (long)command.ExecuteScalar()!;
        });
        Assert.All(import.Hits, hit => Assert.All(hit.Origins, origin => Assert.InRange(origin, 1, sealedResponses)));
        Assert.True(_world.App<ICaseVerifier>().Verify(_world.Case).IsIntact);
        Assert.False(Queue.HasHits());
    }

    [Fact]
    public async Task A_case_open_in_Chainsaw_is_left_to_it()
    {
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        _world.WritePlan();
        _world.Height(800_010);
        using var hold = _world.App<ICaseHolds>().Hold(_world.Case.Info.Id);

        Assert.True(await Loop.RunOnceAsync());

        Assert.Empty(Queue.List(_world.Case.Info.Id));
        Assert.DoesNotContain(_world.Http.Sent, request => request.Uri.AbsoluteUri.Contains("address/"));
    }

    [Fact]
    public async Task With_VPN_as_the_way_out_nothing_is_looked_up_and_it_says_it_waits_for_the_exit()
    {
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        _world.WritePlan(ExitRoute.Vpn);
        _world.Height(800_010);

        Assert.True(await Loop.RunOnceAsync());

        Assert.Empty(_world.Http.Sent);
        Assert.Empty(Queue.List(_world.Case.Info.Id));
        Assert.Equal(BackgroundState.WaitingForExit, _world.App<IBackgroundStatusFile>().Read()!.State);
    }

    [Fact]
    public async Task Without_anything_to_watch_it_says_it_stops_and_ends()
    {
        Assert.False(await Loop.RunOnceAsync());
        Assert.Equal(BackgroundState.Stopped, _world.App<IBackgroundStatusFile>().Read()!.State);

        await _world.WatchAsync();
        _world.App<IBackgroundPlan>().Update(_ => _world.WritePlan() with { Enabled = false });
        Assert.False(await Loop.RunOnceAsync());
        Assert.Empty(_world.Http.Sent);
    }

    [Fact]
    public async Task A_watch_without_a_start_gets_the_height_as_its_start_and_is_checked_from_it()
    {
        await _world.UseTestEndpointAsync();
        var watch = _world.App<IMonitoringService>().Add(
            _world.Case,
            new WatchSubject(WatchSubjectKind.Address, new ChainAddress(Chain.Bitcoin, World.Watched), "x"),
            new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null));
        _world.WritePlan();
        _world.Height(799_999);

        await Loop.RunOnceAsync();

        var started = Queue.Read(_world.Case.Info.Id, Assert.Single(Queue.List(_world.Case.Info.Id)))!;
        Assert.Equal((QueuedKind.Started, 799_999L), (started.Kind, started.Height));
        Assert.Empty(_world.Alerts.Raised);

        _world.Height(800_010);
        _world.Clock.UtcNow += TimeSpan.FromMinutes(1);
        await Loop.RunOnceAsync();

        Assert.Equal(2, Queue.List(_world.Case.Info.Id).Count);
        _world.App<IBackgroundWatches>().Import(_world.Case);
        var service = _world.App<IMonitoringService>();
        Assert.Equal(799_999, service.GetWatches(_world.Case).Single(each => each.Id == watch.Id).StartHeight);
        Assert.Equal(2, service.GetHits(_world.Case).Count);
    }

    [Fact]
    public async Task A_height_that_cannot_be_had_is_said_as_failing_and_nothing_is_queued()
    {
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        _world.WritePlan();

        await Loop.RunOnceAsync();

        Assert.Empty(Queue.List(_world.Case.Info.Id));
        Assert.Equal(BackgroundState.Failing, _world.App<IBackgroundStatusFile>().Read()!.State);
    }

    [Fact]
    public async Task A_chain_whose_sources_are_off_is_not_asked()
    {
        await _world.UseTestEndpointAsync();
        await _world.App<IChainSources>().SetModeAsync(Chain.Bitcoin, SourceMode.Off);
        await _world.WatchAsync();
        _world.WritePlan();
        _world.Height(800_010);

        await Loop.RunOnceAsync();

        Assert.Empty(_world.Http.Sent);
        Assert.Equal(BackgroundState.SourcesOff, _world.App<IBackgroundStatusFile>().Read()!.State);
    }

    [Fact]
    public async Task Its_progress_survives_a_restart_so_that_a_hit_is_not_said_again()
    {
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        _world.WritePlan();
        _world.Height(800_010);
        _world.Clock.UtcNow += TimeSpan.FromMinutes(5);
        await Loop.RunOnceAsync();

        var progress = Assert.Single(_world.App<IBackgroundProgressFile>().Read());
        Assert.Equal(800_010, progress.Checked!.Height);
        Assert.Equal(2, progress.Alerted.Count);
    }

    [Fact]
    public async Task Every_chain_s_endpoints_are_tried_also_when_another_chain_has_one_in_step()
    {
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        var service = _world.App<IMonitoringService>();
        service.Add(
            _world.Case,
            new WatchSubject(WatchSubjectKind.Address, Modules.Monitoring.Tests.Synthetic.On(
                Modules.Monitoring.Tests.Synthetic.Eth("e")), "0x…e"),
            new WatchFilter(Chain.Ethereum, WatchDirection.Both, [], [], null, null));
        _world.WritePlan();
        _world.Height(800_010);

        await Loop.RunOnceAsync();

        var ethereum = _world.Background<IChainSources>().Get(Chain.Ethereum);
        Assert.All(ethereum.Endpoints, endpoint => Assert.NotNull(endpoint.Probe));
        Assert.Contains(
            _world.Http.Sent, request => !request.Uri.AbsoluteUri.StartsWith(World.Base, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_watches_on_one_address_both_carry_the_responses_their_hits_rest_on()
    {
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        await _world.WatchAsync();
        _world.WritePlan();
        _world.Height(800_010);
        _world.Clock.UtcNow += TimeSpan.FromMinutes(5);

        await Loop.RunOnceAsync();

        var ids = Queue.List(_world.Case.Info.Id);
        Assert.Equal(2, ids.Count);
        foreach (var entry in ids.Select(id => Queue.Read(_world.Case.Info.Id, id)!))
        {
            Assert.NotEmpty(entry.Origins);
            Assert.Subset(entry.Responses.Select(response => response.Sequence).ToHashSet(), entry.Origins.ToHashSet());
        }

        var import = _world.App<IBackgroundWatches>().Import(_world.Case);
        Assert.Equal(4, import.Hits.Count);
        Assert.All(import.Hits, hit => Assert.NotEmpty(hit.Origins));
    }

    [Fact]
    public async Task A_hit_the_open_Chainsaw_already_sealed_is_never_told_again()
    {
        await _world.UseTestEndpointAsync();
        var watch = await _world.WatchAsync();
        var plan = _world.WritePlan();
        var known = plan.Cases.Single().Watches.Single() with
        {
            Known =
            [
                $"{watch.Id}|{World.Spent}|Out|BTC",
                $"{watch.Id}|{World.Coinbase}|In|BTC",
            ],
        };
        _world.App<IBackgroundPlan>().Update(current => current.With(
            current.Cases.Single() with { Watches = [known] }));
        _world.Height(800_010);
        _world.Clock.UtcNow += TimeSpan.FromMinutes(5);

        await Loop.RunOnceAsync();

        Assert.Empty(_world.Alerts.Raised);
        Assert.False(Queue.HasHits());
        Assert.Single(Queue.List(_world.Case.Info.Id));
    }

    [Fact]
    public async Task A_start_set_by_the_background_is_kept_at_once()
    {
        await _world.UseTestEndpointAsync();
        _world.App<IMonitoringService>().Add(
            _world.Case,
            new WatchSubject(WatchSubjectKind.Address, new ChainAddress(Chain.Bitcoin, World.Watched), "x"),
            new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null));
        _world.WritePlan();
        _world.Height(799_999);

        await Loop.RunOnceAsync();

        Assert.Equal(799_999, Assert.Single(_world.App<IBackgroundProgressFile>().Read()).StartHeight);
    }

    [Fact]
    public async Task A_plan_that_cannot_be_read_just_now_keeps_the_last_one()
    {
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        _world.WritePlan(ExitRoute.Vpn);
        Assert.True(await Loop.RunOnceAsync());
        var path = Path.Combine(_world.AppData, BackgroundNames.Folder, "plan.bin");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(_world.App<IBackgroundPlan>().TryRead(out _));
            Assert.True(await Loop.RunOnceAsync());
        }
    }
}
