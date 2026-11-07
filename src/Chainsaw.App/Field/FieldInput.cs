namespace Chainsaw.App.Field;

/// <summary>What the field needs to know to draw one frame, taken from the page on the UI thread.</summary>
/// <param name="Width">The page's width, in device-independent pixels.</param>
/// <param name="Height">The page's height, in device-independent pixels.</param>
/// <param name="CellWidth">The width of a character cell.</param>
/// <param name="CellHeight">The height of a character cell.</param>
/// <param name="Seconds">The time since the field started, in seconds.</param>
/// <param name="Animate">
/// Whether the field moves; <see langword="false"/> when Windows has its animations turned off, which holds the
/// field still.
/// </param>
/// <param name="Pointer">The mouse over the page, or <see langword="null"/> when it is elsewhere.</param>
/// <param name="Form">The form's bounds, around which the field is quiet; <see langword="null"/> without a form.</param>
public sealed record FieldInput(
    double Width,
    double Height,
    double CellWidth,
    double CellHeight,
    double Seconds,
    bool Animate,
    FieldPointer? Pointer,
    FieldRect? Form);
