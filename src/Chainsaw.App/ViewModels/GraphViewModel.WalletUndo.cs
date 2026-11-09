namespace Chainsaw.App.ViewModels;

/// <summary>
/// Taking back an approved wallet, confirmed in the graph (sketch PL1b): the wallet's cluster is chosen and fitted to
/// the screen, and the card with its addresses and the box under it stand beside it; choosing anything else leaves
/// the approval as it was.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>Whether the graph is choosing the wallet's cluster itself, which must not let go of the box.</summary>
    private bool _choosingWalletUndo;

    /// <summary>
    /// Chooses the wallet's cluster and shows it, so that its card opens beside it (sketch PL1b); when none of its
    /// addresses is drawn the card stands under the graph's corner.
    /// </summary>
    /// <param name="undo">The wallet whose take-back waits.</param>
    private void ShowWalletUndo(WalletUndo undo)
    {
        var drawn = undo.Nodes.Where(key => Scene?.Node(key) is not null).ToList();
        if (drawn.Count == 0)
        {
            return;
        }

        _choosingWalletUndo = true;
        try
        {
            Choose(drawn[0], null);
        }
        finally
        {
            _choosingWalletUndo = false;
        }

        ShowNodesWanted?.Invoke(this, drawn);
    }

    /// <summary>
    /// Lets go of a wallet's take-back that waits in the graph when the user chooses something outside the wallet.
    /// </summary>
    private void LetGoOfWalletUndo()
    {
        if (!_choosingWalletUndo && Checks.PendingWalletUndo is { } undo
            && (SelectedNode is null || !undo.Nodes.Contains(SelectedNode, StringComparer.Ordinal)))
        {
            Checks.CancelWalletUndo();
        }
    }
}
