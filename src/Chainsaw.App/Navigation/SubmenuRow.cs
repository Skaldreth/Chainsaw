namespace Chainsaw.App.Navigation;

/// <summary>A row of a submenu in the pill (sketch PN1): what it says and does, its key, and what it needs.</summary>
/// <param name="Action">What the row does; <see cref="SubmenuAction.None"/> for a line between groups.</param>
/// <param name="Name">What the row says, e.g. <c>Startpunkt</c>; empty for a line.</param>
/// <param name="Gesture">
/// The key shown at the right, e.g. <c>S</c> or <c>Ctrl+N</c>. A single letter chooses the row while the menu is open;
/// a shortcut with a modifier is the window's own and works whether the menu is open or not.
/// </param>
/// <param name="Need">What the row needs to be chosen.</param>
public sealed record SubmenuRow(SubmenuAction Action, string Name, string Gesture, SubmenuNeed Need)
{
    /// <summary>A line between groups of rows.</summary>
    public static SubmenuRow Line { get; } = new(SubmenuAction.None, string.Empty, string.Empty, SubmenuNeed.Nothing);

    /// <summary>Gets whether the row is a line between groups rather than something to choose.</summary>
    public bool IsLine => Action == SubmenuAction.None;

    /// <summary>
    /// Gets the letter that chooses the row while the menu is open, or <see langword="null"/> when its key has a
    /// modifier or it has none.
    /// </summary>
    public char? Letter => Gesture.Length == 1 && char.IsLetter(Gesture[0]) ? char.ToUpperInvariant(Gesture[0]) : null;
}
