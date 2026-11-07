namespace Chainsaw.App.Field;

/// <summary>Where the mouse is over the field and whether Ctrl is held, which lights the lantern.</summary>
/// <param name="X">The horizontal position on the page.</param>
/// <param name="Y">The vertical position on the page.</param>
/// <param name="Ctrl">Whether Ctrl is held.</param>
public readonly record struct FieldPointer(double X, double Y, bool Ctrl);
