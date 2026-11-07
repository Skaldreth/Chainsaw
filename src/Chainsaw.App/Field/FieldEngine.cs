namespace Chainsaw.App.Field;

/// <summary>
/// The field behind the lock screen and the first-run setup (sketches ID7 and FS1a), after asciify-engine's field
/// (MIT, see THIRD_PARTY.md), rewritten for Chainsaw: every cell gets a character from the scene's tone, quiet around
/// the form, lit by the lantern while Ctrl is held, and the chain stands in the accent down the left part. It knows
/// nothing of WPF, so that it runs off the UI thread and is tested in Chainsaw.App.Tests.
/// </summary>
/// <remarks>Not thread-safe: one frame at a time, from one loop.</remarks>
public sealed class FieldEngine
{
    /// <summary>The characters of the chain, from faint to strong.</summary>
    public const string ChainRamp = " .:-=+*#%@";

    /// <summary>How fast the field's own time runs against the clock.</summary>
    private const double TimeScale = 0.9;

    /// <summary>The longest step the lantern takes at once, so that a pause never makes it jump.</summary>
    private const double LongestStep = 0.1;

    /// <summary>The lantern that follows the mouse while Ctrl is held.</summary>
    private readonly Lantern _lantern = new();

    /// <summary>The chain's cover for the last size of the page, with that size.</summary>
    private (int Columns, int Rows, double CellWidth, double CellHeight, float[] Cover)? _chain;

    /// <summary>The input of the last frame drawn, to tell whether a still field must be drawn again.</summary>
    private FieldInput? _last;

    /// <summary>The scene of the last frame drawn.</summary>
    private FieldSceneKind? _lastScene;

    /// <summary>Gets or sets the scene; Drift, as the sketch shows first, unless chosen otherwise.</summary>
    public FieldSceneKind Scene { get; set; } = FieldSceneKind.Drift;

    /// <summary>Gets the lantern, so that its state can be looked at.</summary>
    public Lantern Lantern => _lantern;

    /// <summary>
    /// Tells whether a frame from this input would look different from the last one. A still field (Windows'
    /// animations off) is drawn again only when the page, the form, the scene or the lantern changes, so that it
    /// rests and costs nothing.
    /// </summary>
    /// <param name="input">The input of the next frame.</param>
    /// <returns><see langword="true"/> when the frame must be drawn.</returns>
    public bool NeedsFrame(FieldInput input)
    {
        if (_last is null || input.Animate || _lantern.IsActive || input.Pointer is { Ctrl: true })
        {
            return true;
        }

        return _lastScene != Scene || _last with { Seconds = 0, Pointer = null } != input with
        {
            Seconds = 0, Pointer = null,
        };
    }

    /// <summary>Works out the next frame.</summary>
    /// <param name="input">What the page looks like now.</param>
    /// <returns>The frame.</returns>
    public FieldFrame Next(FieldInput input)
    {
        var step = _last is null ? 0 : Math.Clamp(input.Seconds - _last.Seconds, 0, LongestStep);
        _lantern.Step(step, input.Pointer);
        _last = input;
        _lastScene = Scene;

        var columns = Math.Max(1, (int)Math.Ceiling(input.Width / input.CellWidth));
        var rows = Math.Max(1, (int)Math.Ceiling(input.Height / input.CellHeight));
        var cover = ChainCover(columns, rows, input.CellWidth, input.CellHeight);
        var time = input.Animate ? input.Seconds * TimeScale : 0;
        return Compose(input, columns, rows, cover, time);
    }

    /// <summary>Gives every cell its character, the chain's cells and the lantern's.</summary>
    /// <param name="input">What the page looks like.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="cover">The chain's cover of every cell.</param>
    /// <param name="time">The field's time.</param>
    /// <returns>The frame.</returns>
    private FieldFrame Compose(FieldInput input, int columns, int rows, float[] cover, double time)
    {
        var scene = FieldScene.For(Scene);
        var (width, height) = (input.Width, input.Height);
        var aspect = width / height;
        var dx = input.CellWidth / width * aspect;
        var dy = input.CellHeight / height;
        var reach = Math.Pow(height * 0.14, 2);
        var strongest = scene.Ramp.Length - 1;
        var background = new char[columns * rows];
        var chain = new List<FieldMark>();
        var lit = new List<FieldMark>();

        for (var row = 0; row < rows; row++)
        {
            var y = (row + 0.5) * input.CellHeight;
            var py = y / height - 0.5;
            for (var column = 0; column < columns; column++)
            {
                var x = (column + 0.5) * input.CellWidth;
                var tone = scene.ShapedTone((x / width - 0.5) * aspect, py, time, dx, dy);
                var glow = _lantern.Glow(x, y, reach);
                if (glow > 0.003)
                {
                    tone = Math.Min(1, tone + glow * 0.5 * (0.7 + 0.6 * FieldNoise.Hash(column, row)));
                }

                var index = row * columns + column;
                if (cover[index] > ChainShape.CoverThreshold)
                {
                    background[index] = ' ';
                    chain.Add(ChainMark(column, row, cover[index], tone, glow));
                    continue;
                }

                var open = CalmArea.Openness(input.Form, x, y);
                var character = scene.Ramp[(int)Math.Floor(tone * open * strongest + 0.5)];
                background[index] = character;
                if (glow * open > 0.05 && character != ' ')
                {
                    lit.Add(new FieldMark(column, row, character, Math.Min(1, glow * open)));
                }
            }
        }

        return new FieldFrame(columns, rows, input.CellWidth, input.CellHeight, background, chain, lit);
    }

    /// <summary>
    /// Gives a cell of the chain its character: denser where the chain covers more of the cell, a little fainter
    /// where the field behind is weak, and brighter in the lantern's light.
    /// </summary>
    /// <param name="column">The cell's column.</param>
    /// <param name="row">The cell's row.</param>
    /// <param name="covered">How much of the cell the chain covers.</param>
    /// <param name="tone">The field's tone in the cell.</param>
    /// <param name="glow">The lantern's light in the cell.</param>
    /// <returns>The mark.</returns>
    private static FieldMark ChainMark(int column, int row, double covered, double tone, double glow)
    {
        var strongest = ChainRamp.Length - 1;
        var level = Math.Floor((0.35 + 0.65 * covered) * strongest - (1 - tone) * 2 + 0.5);
        var index = (int)Math.Max(1, Math.Min(strongest, level));
        return new FieldMark(column, row, ChainRamp[index], Math.Min(1, 0.55 + 0.45 * covered + glow * 0.3));
    }

    /// <summary>Gives the chain's cover for this size of the page, worked out once per size.</summary>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="cellWidth">The width of a cell.</param>
    /// <param name="cellHeight">The height of a cell.</param>
    /// <returns>The cover of every cell.</returns>
    private float[] ChainCover(int columns, int rows, double cellWidth, double cellHeight)
    {
        if (_chain is { } known && known.Columns == columns && known.Rows == rows
            && known.CellWidth == cellWidth && known.CellHeight == cellHeight)
        {
            return known.Cover;
        }

        var cover = ChainShape.Cover(columns, rows, cellWidth, cellHeight);
        _chain = (columns, rows, cellWidth, cellHeight, cover);
        return cover;
    }
}
