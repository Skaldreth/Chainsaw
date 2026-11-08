using System.Windows.Controls.Primitives;

namespace Chainsaw.App.Controls;

/// <summary>
/// A pick that shows whether it is on but never turns itself on or off: a press runs its command, and whether it is
/// on comes only from its binding, so that a mode already chosen stays lit when it is pressed again (sketch EX2).
/// </summary>
public sealed class PickButton : ToggleButton
{
    /// <summary>Leaves the state to the binding.</summary>
    protected override void OnToggle()
    {
    }
}
