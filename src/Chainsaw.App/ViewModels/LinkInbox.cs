using Chainsaw.App.Links;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Receives <c>chainsaw:</c> links, from Chainsaw's own argument or from another start of Chainsaw, and hands them to
/// the shell only while it is shown. While Chainsaw is locked or being set up, the latest link waits, and nothing of
/// it is shown or done; after the unlock, the user decides what becomes of it (CLAUDE.md, *Öppna med en adress*).
/// </summary>
/// <remarks>
/// A link never writes anything by itself: with a case open, it is put in the command bar for the user to confirm
/// with Enter; with none open, it fills in Nytt ärende. So another program that can send links cannot add start
/// points to a case, or do anything else, without the user.
/// </remarks>
public sealed class LinkInbox
{
    /// <summary>The shell that shows a link.</summary>
    private readonly ShellViewModel _shell;

    /// <summary>The link that waits for the shell, or <see langword="null"/>.</summary>
    private ChainStartPoint? _waiting;

    /// <summary>Whether the shell is shown, so that a link can be handed over at once.</summary>
    private bool _isOpen;

    /// <summary>Creates the inbox, closed.</summary>
    /// <param name="shell">The shell that shows a link.</param>
    public LinkInbox(ShellViewModel shell) => _shell = shell;

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
        DeliverIfOpen();
        return true;
    }

    /// <summary>Tells the inbox that the shell is shown, and hands over a link that waits.</summary>
    public void Open()
    {
        _isOpen = true;
        DeliverIfOpen();
    }

    /// <summary>Tells the inbox that the shell is hidden (locked); links wait from now on.</summary>
    public void Close() => _isOpen = false;

    /// <summary>Hands the waiting link to the shell when it is shown.</summary>
    private void DeliverIfOpen()
    {
        if (_isOpen && _waiting is { } startPoint)
        {
            _waiting = null;
            _shell.Offer(startPoint);
        }
    }
}
