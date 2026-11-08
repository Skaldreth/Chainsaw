using Chainsaw.App.Links;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Receives <c>chainsaw:</c> links, from Chainsaw's own argument or from another start of Chainsaw, and hands them to
/// the shell only while it is shown. While Chainsaw is locked or being set up, the latest link waits, and nothing of
/// it is shown or done; the lock screen only says that a link waits (sketch LN1a). After the unlock, the user decides
/// what becomes of it (CLAUDE.md, *Öppna med en adress*): the case picker folds out with the link at the top.
/// </summary>
/// <remarks>
/// A link never writes anything by itself: a link that waited is put at the top of the case picker, for the user to
/// lay in a case or start a new one with; a link that comes while Chainsaw is open is put in the command bar for the
/// user to confirm with Enter, or, with no case open, fills in Nytt ärende. So another program that can send links
/// cannot add start points to a case, or do anything else, without the user.
/// </remarks>
public sealed class LinkInbox
{
    /// <summary>The shell that shows a link.</summary>
    private readonly ShellViewModel _shell;

    /// <summary>The clock, for when a link came.</summary>
    private readonly IClock? _clock;

    /// <summary>The link that waits for the shell, or <see langword="null"/>.</summary>
    private ChainStartPoint? _waiting;

    /// <summary>When the waiting link came.</summary>
    private DateTimeOffset _arrivedUtc;

    /// <summary>Whether the shell is shown, so that a link can be handed over at once.</summary>
    private bool _isOpen;

    /// <summary>Creates the inbox, closed.</summary>
    /// <param name="shell">The shell that shows a link.</param>
    /// <param name="clock">The clock, for when a link came; none in some tests.</param>
    public LinkInbox(ShellViewModel shell, IClock? clock = null)
    {
        _shell = shell;
        _clock = clock;
    }

    /// <summary>Raised when a link begins or stops waiting, so that the lock screen can say that one waits.</summary>
    public event EventHandler? WaitingChanged;

    /// <summary>Gets whether a link waits for the unlock.</summary>
    public bool HasWaiting => _waiting is not null;

    /// <summary>Receives a text that may be a link; on the UI thread. Anything that is not a link is ignored.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it was a link.</returns>
    public bool Receive(string? text)
    {
        if (!ChainsawLink.TryParse(text, out var startPoint))
        {
            return false;
        }

        _waiting = startPoint;
        _arrivedUtc = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        if (_isOpen)
        {
            Deliver(waited: false);
        }
        else
        {
            WaitingChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>The shell is shown: a link that waited is handed over, at the top of the case picker.</summary>
    public void Open()
    {
        _isOpen = true;
        Deliver(waited: true);
    }

    /// <summary>The shell is hidden (locked or being set up): links wait from now on.</summary>
    public void Close() => _isOpen = false;

    /// <summary>Hands the waiting link, if any, to the shell.</summary>
    /// <param name="waited">Whether it waited for the unlock.</param>
    private void Deliver(bool waited)
    {
        if (_waiting is not { } startPoint)
        {
            return;
        }

        _waiting = null;
        if (waited)
        {
            WaitingChanged?.Invoke(this, EventArgs.Empty);
            _shell.OfferWaitingLink(startPoint, _arrivedUtc);
        }
        else
        {
            _shell.Offer(startPoint);
        }
    }
}
