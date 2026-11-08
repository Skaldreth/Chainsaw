using Chainsaw.Modules.Security.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The gate in front of the shell: shows the first-run setup when there is no vault, the lock screen when it is
/// locked, and the shell only while it is open. Locking (Ctrl+L, inactivity, Windows locked) hides everything first,
/// empties the command bar, forgets the open case and then locks the vault.
/// </summary>
public sealed partial class GateViewModel : ObservableObject
{
    /// <summary>The vault that decides what is shown.</summary>
    private readonly IVault _vault;

    /// <summary>The shell, whose command bar is emptied when Chainsaw locks.</summary>
    private readonly ShellViewModel _shell;

    /// <summary>The open case, forgotten when Chainsaw locks.</summary>
    private readonly CaseSession _session;

    /// <summary>The automatic lock after inactivity.</summary>
    private readonly AutoLock _autoLock;

    /// <summary>The links that wait while Chainsaw is locked.</summary>
    private readonly LinkInbox _links;

    /// <summary>Creates the gate and chooses what is shown first from the vault's state.</summary>
    /// <param name="vault">The vault.</param>
    /// <param name="lockScreen">The lock screen.</param>
    /// <param name="firstRun">The first-run setup.</param>
    /// <param name="shell">The shell.</param>
    /// <param name="session">The open case.</param>
    /// <param name="autoLock">The automatic lock.</param>
    /// <param name="links">The links, handed to the shell only while it is shown.</param>
    /// <param name="lockSignal">Requests to lock from the command bar (<c>/lock</c>).</param>
    /// <param name="ui">The UI thread, where the vault's changes are shown.</param>
    public GateViewModel(
        IVault vault,
        LockViewModel lockScreen,
        FirstRunViewModel firstRun,
        ShellViewModel shell,
        CaseSession session,
        AutoLock autoLock,
        LinkInbox links,
        LockSignal lockSignal,
        IUiThread ui)
    {
        _vault = vault;
        _shell = shell;
        _session = session;
        _autoLock = autoLock;
        _links = links;
        LockScreen = lockScreen;
        FirstRun = firstRun;
        Mode = vault.State switch
        {
            VaultState.NotCreated => GateMode.FirstRun,
            VaultState.Unlocked => GateMode.Unlocked,
            _ => GateMode.Locked,
        };
        vault.StateChanged += (_, state) => ui.Post(() => OnVaultChanged(state));
        firstRun.Completed += (_, _) => OnSetupCompleted();
        lockSignal.Requested += (_, _) => Lock();
        if (Mode == GateMode.Unlocked)
        {
            shell.OnShown();
            links.Open();
        }
    }

    /// <summary>Gets the lock screen.</summary>
    public LockViewModel LockScreen { get; }

    /// <summary>Gets the first-run setup.</summary>
    public FirstRunViewModel FirstRun { get; }

    /// <summary>Gets what the window shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShellVisible), nameof(IsLockVisible), nameof(IsFirstRunVisible))]
    [NotifyPropertyChangedFor(nameof(IsGateVisible))]
    [NotifyCanExecuteChangedFor(nameof(LockCommand))]
    public partial GateMode Mode { get; private set; }

    /// <summary>Gets whether the shell is shown; only while the vault is open.</summary>
    public bool IsShellVisible => Mode == GateMode.Unlocked;

    /// <summary>Gets whether the gate's page (the field with the lock screen or the setup) is shown.</summary>
    public bool IsGateVisible => Mode != GateMode.Unlocked;

    /// <summary>Gets whether the lock screen is shown.</summary>
    public bool IsLockVisible => Mode == GateMode.Locked;

    /// <summary>Gets whether the first-run setup is shown.</summary>
    public bool IsFirstRunVisible => Mode == GateMode.FirstRun;

    /// <summary>
    /// Locks Chainsaw now (Ctrl+L): hides the shell, empties the command bar, forgets the case and locks the vault.
    /// Before the vault exists there is nothing to lock.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLock))]
    public void Lock()
    {
        if (_vault.State == VaultState.NotCreated)
        {
            return;
        }

        HideEverything();
        _vault.Lock();
    }

    /// <summary>Records that the user did something, which puts off the automatic lock.</summary>
    public void Touch() => _autoLock.Touch();

    /// <summary>Locks when the time without activity has run out; the window asks now and then.</summary>
    public void CheckIdle()
    {
        if (_vault.State == VaultState.Unlocked && Mode != GateMode.Locked && _autoLock.IsDue())
        {
            Lock();
        }
    }

    /// <summary>
    /// Locks because Windows was locked, the session was disconnected or the computer is going to sleep.
    /// </summary>
    /// <remarks>
    /// The vault is locked whenever it exists, also when it already reads as locked: an unlock that is still deriving
    /// its key then fails, so that Chainsaw never opens behind Windows' lock screen or while the computer sleeps.
    /// </remarks>
    public void OnWindowsLocked() => Lock();

    /// <summary>
    /// Locks because Windows was locked, unless the settings say Chainsaw stays unlocked then (page Låsning). Sleep and
    /// a disconnected session go to <see cref="OnWindowsLocked"/>, which always locks.
    /// </summary>
    public void OnWindowsSessionLocked()
    {
        if (_autoLock.LocksWithWindows)
        {
            OnWindowsLocked();
        }
    }

    /// <summary>Tells whether there is anything to lock.</summary>
    /// <returns><see langword="true"/> unless the lock screen is already shown.</returns>
    private bool CanLock() => Mode != GateMode.Locked;

    /// <summary>
    /// Shows what the vault's state allows. The state is read again here rather than taken from the event, since a
    /// lock may have come between the event and this post, and a stale event must never show the shell.
    /// </summary>
    /// <param name="reported">The state the event reported; only a lock is acted on without reading again.</param>
    private void OnVaultChanged(VaultState reported)
    {
        if (_vault.State == VaultState.NotCreated)
        {
            // The vault file went away: only a new setup can make one, so the setup is shown.
            HideEverything();
            Mode = GateMode.FirstRun;
        }
        else if (reported == VaultState.Locked || _vault.State == VaultState.Locked)
        {
            HideEverything();
        }
        else if (_vault.State == VaultState.Unlocked && Mode == GateMode.Locked)
        {
            Open();
        }
    }

    /// <summary>
    /// Shows the shell when the setup is done, but only if the setup is still shown and the vault still open: a lock
    /// while the last step worked has already shown the lock screen, and it stays.
    /// </summary>
    private void OnSetupCompleted()
    {
        if (Mode == GateMode.FirstRun && _vault.State == VaultState.Unlocked)
        {
            Open();
        }
    }

    /// <summary>
    /// Shows the lock screen in place of everything else and lets go of what the shell held: the command bar, Nytt
    /// ärende with what was written in it, the settings, the menus and the exits' panel, and the open case. Links wait
    /// from now on.
    /// </summary>
    private void HideEverything()
    {
        Mode = GateMode.Locked;
        _links.Close();
        _shell.ClearForLock();
        _shell.DismissAll();
        _shell.NewCase.Dismiss();
        _session.Forget();
        LockScreen.Reset();
    }

    /// <summary>Shows the shell, starts the time without activity anew and hands over a link that waits.</summary>
    private void Open()
    {
        _autoLock.Touch();
        Mode = GateMode.Unlocked;
        _shell.OnShown();

        // The first case's answer goes first, so that a link that waited explains itself after it.
        if (FirstRun.TakeCreationReply() is { } reply)
        {
            _shell.Say(reply);
        }

        _links.Open();
    }
}
