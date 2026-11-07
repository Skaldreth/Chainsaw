namespace Chainsaw.App.Field;

/// <summary>
/// One frame of the field, worked out without WPF: the character in every cell of the field, the cells the chain
/// covers and the cells the lantern lights, each with how strongly it shows.
/// </summary>
/// <param name="Columns">The number of columns of cells.</param>
/// <param name="Rows">The number of rows of cells.</param>
/// <param name="CellWidth">The width of a cell, in device-independent pixels.</param>
/// <param name="CellHeight">The height of a cell, in device-independent pixels.</param>
/// <param name="Background">The field's character in every cell, row by row; a space where nothing shows.</param>
/// <param name="Chain">The cells of the chain, drawn in the accent over the field.</param>
/// <param name="Lit">The cells the lantern lights, drawn light over the field.</param>
public sealed record FieldFrame(
    int Columns,
    int Rows,
    double CellWidth,
    double CellHeight,
    char[] Background,
    IReadOnlyList<FieldMark> Chain,
    IReadOnlyList<FieldMark> Lit);

/// <summary>A cell drawn over the field: the chain or the lantern's light.</summary>
/// <param name="Column">The cell's column.</param>
/// <param name="Row">The cell's row.</param>
/// <param name="Character">The character.</param>
/// <param name="Alpha">How strongly it shows, from 0 to 1.</param>
public readonly record struct FieldMark(int Column, int Row, char Character, double Alpha);
