using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The lock screen (sketch ID7): the master key opens the vault, a wrong key is said honestly, and the lines below
/// the form say what Chainsaw does while it is locked. The master key never becomes a property here: it is taken from
/// the field as characters, given to the vault off the UI thread and cleared.
/// </summary>
public sealed partial class LockViewModel : ObservableObject
{
    /// <summary>What the lock screen says when no exit is connected.</summary>
    public const string ExitWaitsText =
        "Utgången ansluts efter upplåsning. Chainsaw slår inte upp något innan dess.";

    /// <summary>What the lock screen says when an exit stays connected while Chainsaw is locked (2026-11-07).</summary>
    public const string ExitStaysText = "Utgången står kvar, men Chainsaw slår inte upp något medan det är låst.";

    /// <summary>What the lock screen says when lookups go through Windows (decided 2026-11-07).</summary>
    public const string WindowsWaitsText =
        "Uppslag går genom Windows. Chainsaw slår inte upp något medan det är låst.";

    /// <summary>What the lock screen says when the vault did not open for a reason it does not name.</summary>
    public const string UnknownFailureText = "Valvet gick inte att öppna.";

    /// <summary>What the lock screen says when the field was empty.</summary>
    public const string EmptyKeyText = "Skriv huvudnyckeln.";

    /// <summary>The vault the master key opens.</summary>
    private readonly IVault _vault;

    /// <summary>The way out and the exit, whose state the lock screen tells.</summary>
    private readonly ExitWay _exit;

    /// <summary>Creates the lock screen.</summary>
    /// <param name="vault">The vault.</param>
    /// <param name="exit">The way out and the exit; its changes are told on the UI thread.</param>
    /// <param name="autoLock">The automatic lock, whose time is shown.</param>
    /// <param name="build">The build, shown in the corner.</param>
    public LockViewModel(IVault vault, ExitWay exit, AutoLock autoLock, BuildInfo build)
    {
        _vault = vault;
        _exit = exit;
        AutoLockText = autoLock.Description;
        autoLock.Changed += (_, _) => AutoLockText = autoLock.Description;
        AboutText = $"Om Chainsaw · {build.ShortCommit}";
        AboutDetails = $"Chainsaw {build.Version}, commit {build.Commit ?? "okänd"}. "
            + "Teckenfältet bygger på asciify-engine (MIT).";
        ExitText = TextFor(exit);
        exit.Changed += (_, _) => ExitText = TextFor(exit);
    }

    /// <summary>Gets why the last attempt failed, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether an error is shown.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets whether the vault is being opened; the button waits meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnlockLabel))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets the button's text.</summary>
    public string UnlockLabel => IsBusy ? "Låser upp …" : "Lås upp";

    /// <summary>Gets the line about the exit.</summary>
    [ObservableProperty]
    public partial string ExitText { get; private set; }

    /// <summary>Gets the line about the automatic lock, which follows the settings.</summary>
    [ObservableProperty]
    public partial string AutoLockText { get; private set; }

    /// <summary>Gets the text in the corner: <c>Om Chainsaw · commit</c>.</summary>
    public string AboutText { get; }

    /// <summary>Gets what *Om Chainsaw* tells: the version, the commit and the field's origin.</summary>
    public string AboutDetails { get; }

    /// <summary>Opens the vault with what is written in the field.</summary>
    /// <param name="entry">The field; it is emptied, and the key is cleared when the vault has answered.</param>
    /// <returns><see langword="true"/> when the vault was opened.</returns>
    public async Task<bool> UnlockAsync(ISecretEntry entry)
    {
        var masterKey = entry.Take();
        if (IsBusy)
        {
            Secret.Clear(masterKey);
            return false;
        }

        try
        {
            if (masterKey.Length == 0)
            {
                ErrorText = EmptyKeyText;
                return false;
            }

            IsBusy = true;
            ErrorText = string.Empty;
            await Task.Run(() => _vault.Unlock(masterKey));
            return true;
        }
        catch (VaultException exception)
        {
            ErrorText = TextFor(exception.Error);
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorText = "Valvet gick inte att läsa. Kontrollera att mappen finns och går att läsa.";
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing unexpected may take the lock screen down; the message is never shown, it could name a path.
            ErrorText = UnknownFailureText;
            return false;
        }
        finally
        {
            Secret.Clear(masterKey);
            IsBusy = false;
        }
    }

    /// <summary>Forgets the last error, as when the lock screen is shown again.</summary>
    public void Reset() => ErrorText = string.Empty;

    /// <summary>Gives the line about the way out: through Windows, or the exit's state.</summary>
    /// <param name="exit">The way out and the exit.</param>
    /// <returns>The line.</returns>
    private static string TextFor(ExitWay exit)
    {
        if (exit.IsWindows)
        {
            return WindowsWaitsText;
        }

        return exit.Status.State == ExitState.Connected ? ExitStaysText : ExitWaitsText;
    }

    /// <summary>Gives the Swedish text for why the vault did not open.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The text; empty when Chainsaw was locked again meanwhile, which needs no explanation.</returns>
    private static string TextFor(VaultError error) => error switch
    {
        VaultError.WrongMasterKey => "Fel huvudnyckel. Valvet öppnades inte.",
        VaultError.Corrupt => "Valvet är skadat eller har ändrats utanför Chainsaw.",
        VaultError.UnsupportedVersion => "Valvet är skrivet av en nyare version av Chainsaw.",
        VaultError.NotCreated => "Det finns inget valv på den här datorn.",
        VaultError.Locked => string.Empty,
        _ => UnknownFailureText,
    };
}
