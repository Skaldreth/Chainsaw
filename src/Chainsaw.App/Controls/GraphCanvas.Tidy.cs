using System.Windows.Input;

namespace Chainsaw.App.Controls;

/// <summary>The question before the graph is sorted, answered from the surface when it has the keyboard.</summary>
public sealed partial class GraphCanvas
{
    /// <summary>
    /// The question's keys, before the others: Enter sorts and Esc cancels while the question is open.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it was the question's key.</returns>
    private bool TidyKey(Key key)
    {
        var model = _model!;
        switch (key)
        {
            case Key.Enter when model.IsTidyConfirmOpen:
                model.ConfirmTidyCommand.Execute(null);
                return true;
            case Key.Escape when model.IsTidyConfirmOpen:
                model.CancelTidy();
                return true;
            default:
                return false;
        }
    }
}
