using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
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

/// <summary>A secret field that hands out what the test wrote, and keeps the array to check that it was cleared.</summary>
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

        if (!masterKey.SequenceEqual(key))
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

    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey) =>
        throw new NotSupportedException();

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

    public IReadOnlyList<ExitInfo> List() => [];

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

    public string Path => caseNumber + ".cut";

    public bool IsClosed => Disposed > 0;

    public bool HasSharingPassword => false;

    public IReadOnlyList<string> RecoveryFingerprints => [];

    public T Read<T>(Func<ICaseReader, T> work) => throw new NotSupportedException();

    public T Write<T>(Func<ICaseTransaction, T> work) => throw new NotSupportedException();

    public JournalEntry AppendJournal(JournalDraft draft) => throw new NotSupportedException();

    public IReadOnlyList<JournalEntry> ReadJournal(long fromSequence = 1, int maximumCount = int.MaxValue) =>
        throw new NotSupportedException();

    public void SetSharingPassword(ReadOnlySpan<char> password) => throw new NotSupportedException();

    public void RemoveSharingPassword() => throw new NotSupportedException();

    public void AddOperatorAccess() => throw new NotSupportedException();

    public void Save()
    {
    }

    public void Close() => Disposed++;

    public void Dispose() => Disposed++;
}

internal sealed class TestCaseStore : ICaseStore
{
    public List<string> Created { get; } = [];

    public Exception? Failure { get; set; }

    public string Directory => "Ärenden";

    public RecoveryPublicKey? RecoveryKey => null;

    public IReadOnlyList<CaseFileInfo> List() => [];

    public ICase Create(string caseNumber)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Created.Add(caseNumber);
        return new TestCase(caseNumber);
    }

    public ICase Open(string caseNumber) => throw new NotSupportedException();

    public ICase OpenWithPassword(string path, ReadOnlySpan<char> password) => throw new NotSupportedException();

    public ICase OpenWithRecoveryKey(
        string path, System.Security.Cryptography.ECDiffieHellman privateKey, string administrator) =>
        throw new NotSupportedException();

    public IReadOnlyList<string> ReadRecoveryFingerprints(string path) => throw new NotSupportedException();
}

/// <summary>Everything a gate needs, wired as the program wires it, with test doubles for the modules.</summary>
internal sealed class GateRig
{
    public GateRig(VaultState state)
    {
        Vault = new TestVault(state);
        AutoLock = new AutoLock(Clock, TimeSpan.FromMinutes(15));
        Shell = new ShellViewModel(Session);
        LockScreen = new LockViewModel(Vault, Exit, AutoLock, BuildInfo.Parse("0.1.0+abcdef0123456"), Ui);
        FirstRun = new FirstRunViewModel(Vault, Profiles, Exits, Cases, Session);
        Gate = new GateViewModel(Vault, LockScreen, FirstRun, Shell, Session, AutoLock, Ui);
    }

    public InlineUi Ui { get; } = new();

    public TestClock Clock { get; } = new();

    public TestVault Vault { get; }

    public TestProfiles Profiles { get; } = new();

    public TestExitConnection Exit { get; } = new();

    public TestExitStore Exits { get; } = new();

    public TestCaseStore Cases { get; } = new();

    public CaseSession Session { get; } = new();

    public AutoLock AutoLock { get; }

    public ShellViewModel Shell { get; }

    public LockViewModel LockScreen { get; }

    public FirstRunViewModel FirstRun { get; }

    public GateViewModel Gate { get; }
}
