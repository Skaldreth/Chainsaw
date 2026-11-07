namespace Chainsaw.App.Field;

/// <summary>A rectangle on the field's page, in device-independent pixels from the page's top left corner.</summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Bottom">The bottom edge.</param>
public readonly record struct FieldRect(double Left, double Top, double Right, double Bottom);
