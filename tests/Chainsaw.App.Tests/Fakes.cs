using Chainsaw.App.Commands;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>Runs posted work at once, as if the test were the UI thread.</summary>
internal sealed class InlineUi : IUiThread
{
    public void Post(Action work) => work();
}

/// <summary>A clock the test moves.</summary>
internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
}

/// <summary>A secret field that hands out what the test wrote, keeping the array to check it was cleared.</summary>
internal sealed class TestEntry(string text) : ISecretEntry
{
    public List<char[]> Taken { get; } = [];

    public string Text { get; set; } = text;

    public char[] Take()
    {
        var characters = Text.ToCharArray();
        Text = string.Empty;
        Taken.Add(characters);
        return characters;
    }

    public bool AllCleared => Taken.All(array => array.All(c => c == '\0'));
}

/// <summary>A vault that opens with one key, keeps what it was given only as a copy, and can be made to fail.</summary>
internal sealed class TestVault(VaultState state, string key = "correct horse battery") : IVault
{
    private string _key = key;

    public string? ChangedTo { get; private set; }

    public VaultState State { get; private set; } = state;

    public event EventHandler<VaultState>? StateChanged;

    public string? CreatedWith { get; private set; }

    public int UnlockCalls { get; private set; }

    public int LockCalls { get; private set; }

    public Exception? Failure { get; set; }

    public void Create(ReadOnlySpan<char> masterKey)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        CreatedWith = masterKey.ToString();
        Set(VaultState.Unlocked);
    }

    public void Unlock(ReadOnlySpan<char> masterKey)
    {
        UnlockCalls++;
        if (Failure is not null)
        {
            throw Failure;
        }

        if (!masterKey.SequenceEqual(_key))
        {
            throw new VaultException(VaultError.WrongMasterKey);
        }

        Set(VaultState.Unlocked);
    }

    public void Lock()
    {
        LockCalls++;
        if (State == VaultState.Unlocked)
        {
            Set(VaultState.Locked);
        }
    }

    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        if (!currentMasterKey.SequenceEqual(_key))
        {
            throw new VaultException(VaultError.WrongMasterKey);
        }

        _key = newMasterKey.ToString();
        ChangedTo = _key;
    }

    public void Set(VaultState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}

internal sealed class TestProfiles : IOperatorProfileStore
{
    public OperatorProfile? Saved { get; private set; }

    public Exception? Failure { get; set; }

    public OperatorProfile? Get() => Saved;

    public void Save(OperatorProfile profile)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Saved = profile;
    }
}

/// <summary>The way out chosen, kept in memory, as Network keeps it.</summary>
internal sealed class TestExitRoute : IExitRoute
{
    public ExitRoute Current { get; private set; }

    public event EventHandler<ExitRoute>? Changed;

    public List<ExitRoute> Chosen { get; } = [];

    public void Choose(ExitRoute route)
    {
        Chosen.Add(route);
        if (Current != route)
        {
            Current = route;
            Changed?.Invoke(this, route);
        }
    }
}

/// <summary>Whether the computer has a network, as the test says; online at first.</summary>
internal sealed class TestNetwork : INetworkPresence
{
    public bool IsAvailable { get; private set; } = true;

    public event EventHandler<bool>? Changed;

    public void Set(bool available)
    {
        IsAvailable = available;
        Changed?.Invoke(this, available);
    }
}

/// <summary>The reconnecting of a fallen tunnel, as the test says; nothing waits at first.</summary>
internal sealed class TestReconnect : IExitReconnect
{
    public event EventHandler? Changed;

    public DateTimeOffset? NextAttempt { get; private set; }

    public int Attempts { get; private set; }

    public void Set(DateTimeOffset? next, int attempts)
    {
        (NextAttempt, Attempts) = (next, attempts);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Builds the way out for a test, as the program wires it, on the test's own thread.</summary>
internal static class TestWays
{
    public static ExitWay For(IExitConnection exit, IExitRoute? route = null) =>
        new(route ?? new TestExitRoute(), exit, new InlineUi());
}

/// <summary>The currency and resolution in Pricing, kept in memory.</summary>
internal sealed class TestPriceDefaults : IPriceDefaults
{
    public FiatCurrency Currency { get; private set; } = FiatCurrency.Sek;

    public PriceResolution Resolution { get; private set; } = PriceResolution.Hour;

    public void Choose(FiatCurrency currency, PriceResolution resolution) =>
        (Currency, Resolution) = (currency, resolution);
}

internal sealed class TestExitConnection : IExitConnection
{
    public ExitStatus Status { get; private set; } = ExitStatus.NotConnected;

    public event EventHandler<ExitStatus>? StatusChanged;

    public Task<ExitStatus> ConnectAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DisconnectAsync() => throw new NotSupportedException();

    public void Set(ExitStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}

internal sealed class TestExitStore : IExitStore
{
    public List<(string Name, string Configuration)> Added { get; } = [];

    public Guid? Selected { get; private set; }

    public Exception? Failure { get; set; }

    public List<ExitInfo> Known { get; } = [];

    public IReadOnlyList<ExitInfo> List() => Known;

    public ExitInfo Add(string name, string place, ExitKind kind, string configuration)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Added.Add((name, configuration));
        return new ExitInfo(Guid.NewGuid(), name, place, kind);
    }

    public Task<bool> RemoveAsync(Guid id) => Task.FromResult(false);

    public ExitInfo? FindSelected() => null;

    public Task SelectAsync(Guid? id)
    {
        Selected = id;
        return Task.CompletedTask;
    }
}

internal sealed class TestCase(string caseNumber) : ICase
{
    public int Disposed { get; private set; }

    public CaseInfo Info { get; } = new(Guid.NewGuid(), caseNumber, DateTimeOffset.UnixEpoch);

    public string Title { get; private set; } = string.Empty;

    public string Path => caseNumber + ".cut";

    public bool IsClosed => Disposed > 0;

    public bool HasSharingPassword => SharingPassword is not null;

    /// <summary>The sharing password as it was given, copied, to check that it arrived.</summary>
    public string? SharingPassword { get; private set; }

    public List<JournalDraft> Journal { get; } = [];

    /// <summary>When set, <see cref="Close"/> throws this after counting, as a failed save does.</summary>
    public Exception? CloseFailure { get; set; }

    public IReadOnlyList<string> RecoveryFingerprints => [];

    public T Read<T>(Func<ICaseReader, T> work) => throw new NotSupportedException();

    public T Write<T>(Func<ICaseTransaction, T> work) => throw new NotSupportedException();

    public JournalEntry AppendJournal(JournalDraft draft)
    {
        Journal.Add(draft);
        return null!;
    }

    public IReadOnlyList<JournalEntry> ReadJournal(long fromSequence = 1, int maximumCount = int.MaxValue) => [];

    public bool SetTitle(string title)
    {
        Title = CaseTitleRules.Normalize(title);
        return true;
    }

    public void SetSharingPassword(ReadOnlySpan<char> password) => SharingPassword = password.ToString();

    public void RemoveSharingPassword() => throw new NotSupportedException();

    public void AddOperatorAccess() => throw new NotSupportedException();

    public void Save()
    {
    }

    public void Close()
    {
        Disposed++;
        if (CloseFailure is not null)
        {
            throw CloseFailure;
        }
    }

    public void Dispose() => Disposed++;
}

internal sealed class TestCaseStore : ICaseStore
{
    /// <summary>When set, <see cref="Create"/> runs this first, so a test can lock while a case is created.</summary>
    public Action? DuringCreate { get; set; }

    public List<string> Created { get; } = [];

    public List<TestCase> Cases { get; } = [];

    public List<CaseFileInfo> Files { get; } = [];

    public Exception? Failure { get; set; }

    public string Directory => "Ärenden";

    public RecoveryPublicKey? RecoveryKey { get; set; }

    /// <summary>When set, <see cref="List"/> throws it, as a folder that cannot be read.</summary>
    public Exception? ListFailure { get; set; }

    public IReadOnlyList<CaseFileInfo> List() => ListFailure is null ? Files : throw ListFailure;

    public void AddFile(string caseNumber, int minutesAgo) =>
        Files.Add(new CaseFileInfo(
            caseNumber,
            caseNumber + ".cut",
            1,
            new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero).AddMinutes(-minutesAgo)));

    public ICase Create(string caseNumber)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        DuringCreate?.Invoke();
        Created.Add(caseNumber);
        var created = new TestCase(caseNumber);
        Cases.Add(created);
        return created;
    }

    public ICase Open(string caseNumber)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        if (!Files.Any(file => file.CaseNumber == caseNumber))
        {
            throw new CaseException(CaseError.NotFound);
        }

        var opened = new TestCase(caseNumber);
        Cases.Add(opened);
        return opened;
    }

    public ICase OpenWithPassword(string path, ReadOnlySpan<char> password) => throw new NotSupportedException();

    public ICase OpenWithRecoveryKey(
        string path, System.Security.Cryptography.ECDiffieHellman privateKey, string administrator) =>
        throw new NotSupportedException();

    public IReadOnlyList<string> ReadRecoveryFingerprints(string path) => throw new NotSupportedException();
}

/// <summary>Tracing as far as the window uses it: start points, remembered per case.</summary>
internal sealed class TestTracing : ITracingService
{
    public List<(ICase Case, TraceStartPoint StartPoint)> Added { get; } = [];

    public Exception? Failure { get; set; }

    public IReadOnlyList<TracingMethodId> Methods => [];

    public IReadOnlyList<TracingMethodId> AccountMethods => [];

    public Func<TraceGraph, AccountTraceRequest, AccountTraceResult>? AccountTracer { get; set; }

    public List<AccountTraceRequest> AccountTraced { get; } = [];

    public Task<AccountTraceResult> TraceAccountsAsync(
        TraceGraph graph, AccountTraceRequest request, CancellationToken cancellationToken = default)
    {
        AccountTraced.Add(request);
        return Task.FromResult(AccountTracer?.Invoke(graph, request)
            ?? new AccountTraceResult(new TracingMethodId(request.Method, 1), request.Seeds, []));
    }

    public CrossChainLink LinkCrossChain(ICase @case, AccountSeed from, ChainTransactionRef to, string reason) =>
        throw new NotSupportedException();

    public TraceGraph? Graph { get; set; }

    public Func<TraceGraph, TraceRequest, TraceResult>? Tracer { get; set; }

    public List<TraceRequest> Traced { get; } = [];

    public List<ExpansionRequest> Expanded { get; } = [];

    public IReadOnlyList<Txid> NewPerExpansion { get; set; } = [];

    /// <summary>How many times the graph was read.</summary>
    public int Loads { get; private set; }

    public Task<TraceGraph> LoadAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        Loads++;
        return Graph is { } graph ? Task.FromResult(graph) : throw new NotSupportedException();
    }

    public bool AddStartPoint(ICase @case, TraceStartPoint startPoint)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        if (Added.Any(item => item.Case == @case && item.StartPoint == startPoint))
        {
            return false;
        }

        Added.Add((@case, startPoint));
        return true;
    }

    public bool RemoveStartPoint(ICase @case, TraceStartPoint startPoint) => throw new NotSupportedException();

    public Task<ExpansionResult> ExpandAsync(
        ICase @case, ExpansionRequest request, CancellationToken cancellationToken = default)
    {
        Expanded.Add(request);
        var outcome = Expanded.Count == 1 || NewPerExpansion.Count > 0
            ? ExpansionOutcome.Expanded
            : ExpansionOutcome.NotExpanded;
        var problem = outcome == ExpansionOutcome.NotExpanded ? "Ingen utgång är ansluten." : null;
        return Task.FromResult(new ExpansionResult(
            outcome, NewPerExpansion, problem, Graph ?? throw new NotSupportedException()));
    }

    public HeldBackList? HeldBack { get; set; }

    public List<IReadOnlyCollection<Txid>> Taken { get; } = [];

    public Task<HeldBackList> ListHeldBackAsync(
        ICase @case,
        BitcoinAddress address,
        DateTimeOffset? around = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HeldBack ?? new HeldBackList(address, [], true, null));

    public Task<ExpansionResult> TakeHeldBackAsync(
        ICase @case,
        BitcoinAddress address,
        IReadOnlyCollection<Txid> transactions,
        CancellationToken cancellationToken = default)
    {
        Taken.Add(transactions);
        return Task.FromResult(new ExpansionResult(
            ExpansionOutcome.Expanded, [.. transactions], null, Graph ?? throw new NotSupportedException()));
    }

    public CrossChainLink LinkCrossChain(ICase @case, Outpoint from, ChainTransactionRef to, string reason) =>
        throw new NotSupportedException();

    public bool UnlinkCrossChain(ICase @case, CrossChainLink link, string? reason) =>
        throw new NotSupportedException();

    public IReadOnlyList<CrossChainLinkHistoryEntry> GetCrossChainHistory(ICase @case) => [];

    public void SavePosition(ICase @case, string nodeKey, GraphLayout layout, NodePosition position) =>
        throw new NotSupportedException();

    public void SavePositions(
        ICase @case, GraphLayout layout, IReadOnlyDictionary<string, NodePosition> positions) =>
        throw new NotSupportedException();

    public void PickFromPile(ICase @case, IReadOnlyCollection<string> nodeKeys) =>
        throw new NotSupportedException();

    public void ReturnToPile(ICase @case, IReadOnlyCollection<string> nodeKeys) =>
        throw new NotSupportedException();

    public TraceResult Trace(TraceGraph graph, TraceRequest request)
    {
        Traced.Add(request);
        return Tracer is { } tracer ? tracer(graph, request) : throw new NotSupportedException();
    }

    /// <summary>The tokens the asynchronous trace was given.</summary>
    public List<CancellationToken> TraceTokens { get; } = [];

    public Task<TraceResult> TraceAsync(
        TraceGraph graph, TraceRequest request, CancellationToken cancellationToken = default)
    {
        TraceTokens.Add(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Trace(graph, request));
    }

    /// <summary>The view the case holds.</summary>
    public GraphViewState View { get; set; } = GraphViewState.Default;

    /// <summary>The views stored, in order.</summary>
    public List<GraphViewState> SavedViews { get; } = [];

    public GraphViewState GetView(ICase @case) => View;

    public bool SaveView(ICase @case, GraphViewState view)
    {
        SavedViews.Add(view);
        var changed = view.Method != View.Method;
        View = view;
        return changed;
    }
}

/// <summary>The label library as the window uses it: what was taken in and given, and a failure on demand.</summary>
internal sealed class TestLibrary : ILabelLibrary
{
    public List<ICase> Absorbed { get; } = [];

    public List<IReadOnlyCollection<BitcoinAddress>> Shared { get; } = [];

    public IReadOnlyList<LabelEntry> ToShare { get; set; } = [];

    public Exception? Failure { get; set; }

    public LibraryAbsorption Absorb(ICase @case)
    {
        Absorbed.Add(@case);
        return Failure is null ? new LibraryAbsorption(0, 0) : throw Failure;
    }

    public IReadOnlyList<LabelEntry> ShareInto(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        Shared.Add(addresses);
        if (Failure is not null)
        {
            throw Failure;
        }

        var given = ToShare;
        ToShare = [];
        return given;
    }

    public IReadOnlyList<LabelEntry> ShareInto(ICase @case, IReadOnlyCollection<ChainAddress> addresses) =>
        throw new NotSupportedException();

    /// <summary>Gets or sets what the library lists.</summary>
    public IReadOnlyList<LibraryLabel> Listed { get; set; } = [];

    /// <summary>Gets or sets a failure listing throws, as a locked vault does.</summary>
    public Exception? ListFailure { get; set; }

    public IReadOnlyList<LibraryLabel> List() => ListFailure is null ? Listed : throw ListFailure;
}

/// <summary>A verifier that gives what the test set.</summary>
internal sealed class TestVerifier : ICaseVerifier
{
    public CaseVerification Result { get; set; } =
        new(DateTimeOffset.UnixEpoch, [], [new ChainVerification("cases.journal", 3, 3, "ab", null)]);

    public CaseVerification Verify(ICase @case) => Result;
}

/// <summary>Everything a window needs, wired as the program wires it, with test doubles for the modules.</summary>
internal sealed class GateRig
{
    public GateRig(VaultState state)
    {
        Vault = new TestVault(state);
        AutoLock = new AutoLock(Clock, TimeSpan.FromMinutes(15));
        Workflow = new CaseWorkflow(Cases, Tracing, Vault, Session, Library);
        NewCase = new NewCaseViewModel(Workflow);
        var input = new StartPointInput(Workflow, Session, NewCase);
        Commands = new CommandRegistry(
            [
                new NewCaseCommand(NewCase),
                new OpenCaseCommand(Workflow),
                new CloseCaseCommand(Workflow),
                new VerifyCommand(Session, Verifier),
                new LockCommand(LockSignal),
            ],
            input);
        Sanctions = new SanctionListImport(SanctionLists, Picker);
        Way = new ExitWay(Route, Exit, Ui, network: Network, reconnect: Reconnect);
        ExitPanel = new ExitPanelViewModel(Exit, Way, Exits, Picker, Ui);
        Settings = new SettingsViewModel(
            new AppSettings(), SettingsStore, Way, ExitPanel, AutoLock, Prices, Profiles, Vault, Cases, Build);
        Toaster = new Toaster(Ui);
        Shell = new ShellViewModel(
            Session, Commands, NewCase, Workflow, Way, Exits, Sanctions, ExitPanel, Settings, Ui, toaster: Toaster);
        Links = new LinkInbox(Shell);
        LockScreen = new LockViewModel(Vault, Way, AutoLock, Build);
        FirstRun = new FirstRunViewModel(Vault, Profiles, Exits, Workflow);
        Gate = new GateViewModel(Vault, LockScreen, FirstRun, Shell, Session, AutoLock, Links, LockSignal, Ui);
    }

    public InlineUi Ui { get; } = new();

    /// <summary>The toaster, which says an answer that comes while the command bar is hidden.</summary>
    public Toaster Toaster { get; }

    public TestLibrary Library { get; } = new();

    public TestClock Clock { get; } = new();

    public TestVault Vault { get; }

    public TestProfiles Profiles { get; } = new();

    public TestExitConnection Exit { get; } = new();

    public TestExitRoute Route { get; } = new();

    public TestNetwork Network { get; } = new();

    public TestReconnect Reconnect { get; } = new();

    public ExitWay Way { get; }

    public TestPriceDefaults Prices { get; } = new();

    public BuildInfo Build { get; } = BuildInfo.Parse("0.1.0+abcdef0123456");

    /// <summary>The settings' file in a folder of the test's own, which is never created unless written.</summary>
    public AppSettingsStore SettingsStore { get; } =
        new(Path.Combine(Path.GetTempPath(), "chainsaw-app-settings-" + Guid.NewGuid().ToString("N")));

    public SettingsViewModel Settings { get; }

    public TestExitStore Exits { get; } = new();

    public TestCaseStore Cases { get; } = new();

    public TestTracing Tracing { get; } = new();

    public TestVerifier Verifier { get; } = new();

    public TestSanctionLists SanctionLists { get; } = new();

    public TestFilePicker Picker { get; } = new();

    public SanctionListImport Sanctions { get; }

    public LockSignal LockSignal { get; } = new();

    public CaseSession Session { get; } = new();

    public CaseWorkflow Workflow { get; }

    public NewCaseViewModel NewCase { get; }

    public CommandRegistry Commands { get; }

    public LinkInbox Links { get; }

    public AutoLock AutoLock { get; }

    public ExitPanelViewModel ExitPanel { get; }

    public ShellViewModel Shell { get; }

    public LockViewModel LockScreen { get; }

    public FirstRunViewModel FirstRun { get; }

    public GateViewModel Gate { get; }
}

/// <summary>A file dialog that gives what the test set, and counts how often it was asked.</summary>
internal sealed class TestFilePicker : IFilePicker
{
    public string? Path { get; set; }

    public int Asked { get; private set; }

    public string? WireGuardPath { get; set; }

    public int AskedForWireGuard { get; private set; }

    public string? PickSanctionList()
    {
        Asked++;
        return Path;
    }

    public string? PickWireGuardFile()
    {
        AskedForWireGuard++;
        return WireGuardPath;
    }
}

/// <summary>Sanction lists that keep what was imported, or fail as the test set.</summary>
internal sealed class TestSanctionLists : ISanctionLists
{
    public List<byte[]> Imported { get; } = [];

    public Exception? Failure { get; set; }

    public SanctionListInfo Info { get; set; } = new(
        "OFAC SDN",
        new string('a', 64),
        new DateOnly(2026, 11, 1),
        DateTimeOffset.UnixEpoch,
        new Dictionary<string, int> { ["XBT"] = 3, ["ETH"] = 2 },
        0);

    public SanctionListInfo? Current => Imported.Count > 0 ? Info : null;

    public SanctionListInfo Import(Stream content)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        using var copy = new MemoryStream();
        content.CopyTo(copy);
        Imported.Add(copy.ToArray());
        return Info;
    }

    public IReadOnlyList<SanctionListInfo> List() => Current is { } list ? [list] : [];

    public SanctionMatch? Find(BitcoinAddress address) => throw new NotSupportedException();

    public SanctionMatch? Find(EthereumAddress address) => throw new NotSupportedException();
}
