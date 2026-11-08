using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The open Chainsaw's side of the background watching: the plan, the hold, starting with Windows, bringing in what
/// the background saw and saying honestly what it does (sketch BV1a, the lock screen's line in ID8c).
/// </summary>
public sealed class BackgroundLinkTests
{
    private static readonly AppSettings Windows = new() { ExitRoute = ExitRoute.Windows };

    private readonly TestWatchSource _source = new();

    private readonly BackgroundFakes _fakes = new();

    [Fact]
    public async Task An_active_watch_through_Windows_is_planned_started_and_set_to_start_with_Windows()
    {
        var (session, link) = Link(Windows);
        _fakes.Status.Status = new BackgroundStatus(BackgroundState.Running, "mempool.space", TestWatchSource.At, 7);

        await link.WriteCaseAsync();

        var plan = _fakes.Plan.Plan;
        Assert.True(plan.Enabled);
        Assert.Equal(ExitRoute.Windows, plan.Route);
        Assert.Equal(WatchAlerts.Windows | WatchAlerts.Sound | WatchAlerts.Screen, plan.Alerts);
        Assert.Equal([session.Current!.Info.Id], plan.Cases.Select(@case => @case.CaseId));
        Assert.Equal(BackgroundFakes.Command, _fakes.Autostart.Command);
        Assert.Equal(1, _fakes.Process.Starts);
        Assert.Equal(BackgroundMode.Running, link.Mode);
        Assert.Equal("mempool.space", link.InStepWith);
    }

    [Fact]
    public async Task With_VPN_the_background_is_neither_started_nor_set_and_it_says_it_waits_for_the_exit()
    {
        _fakes.Process.Running = true;
        var (_, link) = Link(new AppSettings());

        await link.WriteCaseAsync();

        Assert.Null(_fakes.Autostart.Command);
        Assert.Equal(0, _fakes.Process.Starts);
        Assert.Equal(1, _fakes.Process.Stops);
        Assert.Equal(BackgroundMode.NeedsWindows, link.Mode);
    }

    [Fact]
    public async Task Turned_off_it_is_stopped_and_taken_away_from_the_start_with_Windows()
    {
        var (_, link) = Link(Windows);
        await link.WriteCaseAsync();
        _fakes.Process.Running = true;

        await link.ApplyAsync(Windows with { WatchInBackground = false });

        Assert.False(_fakes.Plan.Plan.Enabled);
        Assert.Null(_fakes.Autostart.Command);
        Assert.Equal(1, _fakes.Process.Stops);
        Assert.Equal(BackgroundMode.Off, link.Mode);
    }

    [Fact]
    public async Task A_plan_that_cannot_be_written_still_stops_the_background_when_VPN_is_chosen()
    {
        var (_, link) = Link(Windows);
        await link.WriteCaseAsync();
        _fakes.Plan.Fails = true;

        await link.ApplyAsync(new AppSettings());
        await link.RefreshAsync();

        // The file still says Windows, but nothing more is looked up through it, and it is not started again.
        Assert.Equal(ExitRoute.Windows, _fakes.Plan.Plan.Route);
        Assert.False(_fakes.Process.Running);
        Assert.Equal(1, _fakes.Process.Stops);
        Assert.Equal(1, _fakes.Process.Starts);
        Assert.Null(_fakes.Autostart.Command);
        Assert.Equal(BackgroundMode.NeedsWindows, link.Mode);
    }

    [Fact]
    public async Task Without_an_active_watch_it_rests()
    {
        _fakes.Watches.Active = false;
        var (_, link) = Link(Windows);

        await link.WriteCaseAsync();

        Assert.Empty(_fakes.Plan.Plan.Cases);
        Assert.Null(_fakes.Autostart.Command);
        Assert.Equal(BackgroundMode.Idle, link.Mode);
    }

    [Fact]
    public async Task A_background_that_should_run_but_does_not_is_said_and_started_again_at_most_once_a_minute()
    {
        _fakes.Process.StartWorks = false;
        var (_, link) = Link(Windows);
        await link.WriteCaseAsync();

        await link.RefreshAsync();
        await link.RefreshAsync();

        Assert.Equal(BackgroundMode.NotRunning, link.Mode);
        Assert.Equal(1, _fakes.Process.Starts);
        _fakes.Clock.UtcNow += BackgroundLink.StartAgain;
        await link.RefreshAsync();
        Assert.Equal(2, _fakes.Process.Starts);
    }

    // The two tests below start the link's loop, whose waits pass at once. Its look at the background holds the open
    // case on the UI thread, so they run on one (UiThread), as in the window: run inline on the loop's own thread, it
    // would hold the case at the same time as the test (between Forget and Changed in Open), twice.
    [Fact]
    public Task The_open_case_is_held_and_let_go_when_another_opens_or_it_closes() =>
        UiThread.RunAsync(TheOpenCaseIsHeldAndLetGoAsync);

    [Fact]
    public Task A_case_let_go_is_written_into_the_plan_first_so_the_background_never_watches_what_was_taken_back() =>
        UiThread.RunAsync(ACaseLetGoIsWrittenIntoThePlanFirstAsync);

    private async Task TheOpenCaseIsHeldAndLetGoAsync()
    {
        var (session, link) = Link(Windows, new PostedUi(SynchronizationContext.Current!));
        link.Start();
        var first = session.Current!.Info.Id;
        Assert.Equal([first], _fakes.Holds.Held);

        var other = new TestCase("2026-0027");
        session.Open(other);
        Assert.Equal([other.Info.Id], _fakes.Holds.Held);

        await session.CloseAsync();
        Assert.Empty(_fakes.Holds.Held);
        link.Dispose();
    }

    private async Task ACaseLetGoIsWrittenIntoThePlanFirstAsync()
    {
        var (session, link) = Link(Windows, new PostedUi(SynchronizationContext.Current!));
        link.Start();
        await link.WriteCaseAsync();
        Assert.Single(_fakes.Plan.Plan.Cases);

        // The last watch is taken back, and the case closed before the board's change reached the plan.
        _fakes.Watches.Active = false;
        await session.CloseAsync();

        Assert.Empty(_fakes.Plan.Plan.Cases);
        link.Dispose();
    }

    [Fact]
    public async Task What_the_background_saw_is_brought_into_the_open_case_and_its_hits_never_told_as_hits()
    {
        var (session, link) = Link(Windows);
        var board = link.Board;
        var watch = _source.Add(WatchSamples.Subject(GraphSamples.Address(1)), WatchSamples.Filter());
        var tip = new WatchTip(9, watch.Id, Chain.Bitcoin,
            WatchSamples.Out(WatchSamples.On(GraphSamples.Address(2)), 0.1m), Guid.NewGuid(), [1], 2,
            TestWatchSource.At);
        _fakes.Watches.Result = new BackgroundImport(1, 2, [tip], []);
        IReadOnlyList<WatchHit>? told = null;
        board.HitsFound += (_, hits) => told = hits;

        await link.ImportAsync(session.Current!);
        Assert.Equal(0, _fakes.Watches.Imports);

        _fakes.Queue.Entries[session.Current!.Info.Id] = [Guid.NewGuid()];
        await link.ImportAsync(session.Current!);

        // A tip is no hit until the runner's own lookup gives it; only then is it told.
        Assert.Equal(1, _fakes.Watches.Imports);
        Assert.Null(told);
    }

    [Fact]
    public async Task The_runner_brings_in_what_the_background_saw_before_its_own_checks()
    {
        var (session, board) = WatchSamples.Board(_source);
        var order = new List<string>();
        _source.Add(WatchSamples.Subject(GraphSamples.Address(1)), WatchSamples.Filter());
        var runner = new WatchRunner(board, new TestClock(), (_, _) => Task.CompletedTask, @case =>
        {
            order.Add("import " + @case.Info.CaseNumber);
            return Task.CompletedTask;
        });
        await board.ReloadAsync();

        await runner.RunOnceAsync();

        Assert.Equal(["import 2026-0026"], order);
        Assert.NotEmpty(_source.Checks);
        session.Forget();
        await runner.RunOnceAsync();
        Assert.Single(order);
    }

    [Fact]
    public async Task A_watch_without_a_start_has_the_queue_brought_in_again_after_the_height_and_before_the_check()
    {
        // A start the background set waits in the queue until the case has seen the chain that far: the height asked
        // shows it, and it must come in before the check would start the watch at that height.
        var (_, board) = WatchSamples.Board(_source);
        var checksAtImport = new List<int>();
        _source.Add(WatchSamples.Subject(GraphSamples.Address(1)), WatchSamples.Filter(), start: null);
        var runner = new WatchRunner(board, new TestClock(), (_, _) => Task.CompletedTask, _ =>
        {
            checksAtImport.Add(_source.Checks.Count);
            return Task.CompletedTask;
        });
        await board.ReloadAsync();

        await runner.RunOnceAsync();

        Assert.Equal([0, 0], checksAtImport);
        Assert.NotEmpty(_source.Checks);
    }

    [Fact]
    public async Task An_entry_that_could_not_be_brought_in_is_said_on_the_board()
    {
        var (session, link) = Link(Windows);
        _fakes.Queue.Entries[session.Current!.Info.Id] = [Guid.NewGuid()];
        _fakes.Watches.Result = new BackgroundImport(0, 0, [], ["En kontroll kunde inte läsas."]);

        await link.ImportAsync(session.Current!);

        Assert.Equal("En kontroll kunde inte läsas.", link.Board.Problem);
    }

    [Fact]
    public async Task A_hit_waiting_in_the_queue_is_said_on_the_lock_screen_and_nothing_more()
    {
        var (_, link) = Link(Windows);
        var lockScreen = new LockViewModel(new TestVault(Modules.Security.Contracts.VaultState.Locked),
            new ExitWay(new TestExitRoute(), new TestExitConnection(), new InlineUi()),
            new AutoLock(new TestClock(), TimeSpan.FromMinutes(15)), BuildInfo.From(typeof(LockViewModel).Assembly),
            link);
        Assert.False(lockScreen.HasWatchHit);

        _fakes.Queue.Hits = true;
        await link.RefreshAsync();

        Assert.True(lockScreen.HasWatchHit);
        Assert.Equal("En bevakning har slagit till. Den visas efter upplåsning.", LockViewModel.WatchHitText);
    }

    [Theory]
    [InlineData(true, ExitRoute.Windows, true, BackgroundMode.Running, WatchWords.BackgroundRunningText, false, false)]
    [InlineData(true, ExitRoute.Vpn, false, BackgroundMode.NeedsWindows, WatchWords.BackgroundWaitsText, true, false)]
    [InlineData(false, ExitRoute.Windows, false, BackgroundMode.Off, WatchWords.BackgroundOffText, false, true)]
    public async Task The_view_s_head_says_what_the_background_does(
        bool on, ExitRoute route, bool running, BackgroundMode mode, string text, bool waiting, bool off)
    {
        _fakes.Process.StartWorks = running;
        var (_, link) = Link(new AppSettings { WatchInBackground = on, ExitRoute = route });
        var view = new MonitoringViewModel(link.Board, new WatchFormViewModel(link.Board), new TestClock(), link);

        await link.WriteCaseAsync();

        Assert.Equal(mode, link.Mode);
        Assert.Equal(text, view.BackgroundText);
        Assert.Equal((waiting, off), (view.IsWaiting, view.IsOff));
        Assert.Equal(waiting || off, view.IsFrameDashed);
    }

    [Theory]
    [InlineData(BackgroundState.Failing, BackgroundMode.Failing, WatchWords.BackgroundFailingText)]
    [InlineData(BackgroundState.SourcesOff, BackgroundMode.SourcesOff, WatchWords.BackgroundSourcesOffText)]
    public async Task What_the_running_background_says_of_itself_is_shown(
        BackgroundState state, BackgroundMode mode, string text)
    {
        _fakes.Status.Status = new BackgroundStatus(state, null, TestWatchSource.At, 3);
        var (_, link) = Link(Windows);
        var view = new MonitoringViewModel(link.Board, new WatchFormViewModel(link.Board), new TestClock(), link);

        await link.WriteCaseAsync();

        Assert.Equal(mode, link.Mode);
        Assert.Equal(text, view.BackgroundText);
        Assert.True(view.IsWaiting);
    }

    [Fact]
    public async Task A_status_left_by_an_ended_process_is_not_believed()
    {
        _fakes.Status.Status = new BackgroundStatus(
            BackgroundState.Failing, null, TestWatchSource.At, BackgroundFakes.FakeProcess.Stale);
        var (_, link) = Link(Windows);

        await link.WriteCaseAsync();

        Assert.Equal(BackgroundMode.Running, link.Mode);
    }

    [Fact]
    public async Task The_endpoint_in_step_follows_the_background_s_word()
    {
        _fakes.Status.Status = new BackgroundStatus(BackgroundState.Running, "blockstream.info", TestWatchSource.At, 3);
        var (_, link) = Link(Windows);
        var view = new MonitoringViewModel(link.Board, new WatchFormViewModel(link.Board), new TestClock(), link);

        await link.WriteCaseAsync();

        Assert.Equal("Går i bakgrunden · genom Windows · i fas med blockstream.info", view.BackgroundText);
    }

    [Fact]
    public async Task A_problem_of_Chainsaw_s_own_goes_before_the_background()
    {
        _source.GateState = WatchGate.NoExit;
        var (_, link) = Link(Windows);
        var view = new MonitoringViewModel(link.Board, new WatchFormViewModel(link.Board), new TestClock(), link);

        await link.WriteCaseAsync();

        Assert.Equal(WatchWords.NoExitText, view.BackgroundText);
        Assert.False(view.IsOff);
    }

    [Fact]
    public void The_settings_give_the_ways_of_telling_and_the_screen_message_waits_for_its_sketch()
    {
        var defaults = new AppSettings();

        Assert.True(defaults.WatchInBackground);
        Assert.Equal(WatchAlerts.Windows | WatchAlerts.Sound | WatchAlerts.Screen, defaults.Alerts);
        Assert.Equal(WatchAlerts.Screen, (defaults with
        {
            AlertInWindows = false,
            AlertWithSound = false,
            AlertOnScreen = true,
        }).Alerts);
    }

    private (CaseSession Session, BackgroundLinkHarness Link) Link(AppSettings settings, IUiThread? ui = null)
    {
        var (session, board) = WatchSamples.Board(_source);
        var link = new BackgroundLinkHarness(
            new BackgroundLink(
                _fakes.Parts, session, board, ui ?? new InlineUi(), settings, (_, _) => Task.CompletedTask),
            board);
        return (session, link);
    }
}

/// <summary>The link and the board it follows.</summary>
/// <param name="link">The link.</param>
/// <param name="board">The board.</param>
internal sealed class BackgroundLinkHarness(BackgroundLink link, WatchBoard board)
{
    public WatchBoard Board => board;

    public BackgroundMode Mode => link.Mode;

    public string? InStepWith => link.InStepWith;

    public static implicit operator BackgroundLink(BackgroundLinkHarness harness) => harness.Inner;

    public BackgroundLink Inner => link;

    public void Start() => link.Start();

    public void Dispose() => link.Dispose();

    public Task WriteCaseAsync() => link.WriteCaseAsync();

    public Task ApplyAsync(AppSettings settings) => link.ApplyAsync(settings);

    public Task RefreshAsync() => link.RefreshAsync();

    public Task ImportAsync(ICase @case) => link.ImportAsync(@case);
}

/// <summary>In-memory stand-ins for the files, the process and the Run key.</summary>
internal sealed class BackgroundFakes
{
    public const string Command = "\"C:\\Chainsaw\\Chainsaw.Watcher.exe\"";

    public TestClock Clock { get; } = new();

    public FakeWatches Watches { get; } = new();

    public MemoryPlan Plan { get; } = new();

    public FakeHolds Holds { get; } = new();

    public MemoryQueue Queue { get; } = new();

    public FakeStatus Status { get; } = new();

    public FakeProcess Process { get; } = new();

    public FakeAutostart Autostart { get; } = new();

    public BackgroundParts Parts => new(Watches, Plan, Holds, Queue, Status, Process, Autostart, Clock);

    internal sealed class FakeWatches : IBackgroundWatches
    {
        public bool Active { get; set; } = true;

        public BackgroundImport Result { get; set; } = BackgroundImport.None;

        public int Imports { get; private set; }

        public BackgroundCase Describe(ICase @case, DateTimeOffset utc) => new(
            @case.Info.Id,
            Active
                ? [new BackgroundWatch("w", [new ChainAddress(Chain.Bitcoin, "bc1qx")],
                    new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null), 1, null)]
                : [],
            utc);

        public BackgroundImport Import(ICase @case)
        {
            Imports++;
            return Result;
        }
    }

    internal sealed class MemoryPlan : IBackgroundPlan
    {
        public BackgroundPlan Plan { get; private set; } = BackgroundPlan.Empty;

        public BackgroundPlan Read() => Plan;

        public bool TryRead(out BackgroundPlan plan)
        {
            plan = Plan;
            return true;
        }

        public DateTimeOffset? WrittenAt() => null;

        public bool Fails { get; set; }

        public BackgroundPlan Update(Func<BackgroundPlan, BackgroundPlan> change) =>
            Fails ? throw new IOException("Held by a scanner.") : Plan = change(Plan);
    }

    internal sealed class FakeHolds : ICaseHolds
    {
        public List<Guid> Held { get; } = [];

        public IDisposable? Hold(Guid caseId)
        {
            Held.Add(caseId);
            return new Release(() => Held.Remove(caseId));
        }

        public bool IsHeld(Guid caseId) => Held.Contains(caseId);

        private sealed class Release(Action release) : IDisposable
        {
            public void Dispose() => release();
        }
    }

    internal sealed class MemoryQueue : IWatchQueue
    {
        public Dictionary<Guid, List<Guid>> Entries { get; } = [];

        public bool Hits { get; set; }

        public void Add(QueuedCheck entry) => throw new NotSupportedException();

        public IReadOnlyList<Guid> List(Guid caseId) => Entries.GetValueOrDefault(caseId) ?? [];

        public QueuedCheck? Read(Guid caseId, Guid id) => null;

        public void Remove(Guid caseId, Guid id) => Entries.GetValueOrDefault(caseId)?.Remove(id);

        public void SetAside(Guid caseId, Guid id) => Remove(caseId, id);

        public bool HasHits() => Hits;
    }

    internal sealed class FakeStatus : IBackgroundStatusFile
    {
        public BackgroundStatus? Status { get; set; }

        public BackgroundStatus? Read() => Status;

        public void Write(BackgroundStatus status) => Status = status;
    }

    internal sealed class FakeProcess : IBackgroundProcess
    {
        public bool Running { get; set; }

        public bool StartWorks { get; set; } = true;

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public bool Exists => true;

        public string Command => BackgroundFakes.Command;

        public bool IsRunning() => Running;

        public bool IsProcess(int processId) => processId != Stale;

        public const int Stale = 99;

        public bool Start()
        {
            Starts++;
            Running = StartWorks;
            return StartWorks;
        }

        public void Stop()
        {
            Stops++;
            Running = false;
        }
    }

    internal sealed class FakeAutostart : IAutostart
    {
        public string? Command { get; private set; }

        public bool IsSet() => Command is not null;

        public void Set(string? command) => Command = command;
    }
}
