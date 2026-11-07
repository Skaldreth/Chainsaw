using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chainsaw.App.Field;

namespace Chainsaw.App.Controls;

/// <summary>
/// The field of characters behind the lock screen and the first-run setup (sketches ID7 and FS1a), with the chain in
/// the accent. Each frame is worked out and drawn into pixels off the UI thread by <see cref="FieldEngine"/> and
/// <see cref="FieldRasterizer"/>; the UI thread only takes the mouse and copies the finished pixels. The field rests
/// when it is not shown or the window is minimised, and stands still when Windows has its animations turned off.
/// </summary>
public sealed class AsciiField : FrameworkElement
{
    /// <summary>The form the field is quiet around.</summary>
    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(FrameworkElement), typeof(AsciiField), new PropertyMetadata(null));

    /// <summary>About thirty frames a second, as the sketch.</summary>
    private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(33);

    /// <summary>The field's logic, used by one frame at a time.</summary>
    private readonly FieldEngine _engine = new();

    /// <summary>The field's clock.</summary>
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>The pixels shown, or <see langword="null"/> before the first frame of this size.</summary>
    private WriteableBitmap? _bitmap;

    /// <summary>Whether <see cref="_bitmap"/> has been drawn into, so that no empty bitmap is ever shown.</summary>
    private bool _hasFrame;

    /// <summary>The characters' masks for the screen's scale.</summary>
    private GlyphAtlas? _atlas;

    /// <summary>The buffer each frame is drawn into, off the UI thread.</summary>
    private int[] _pixels = [];

    /// <summary>Stops the running loop.</summary>
    private CancellationTokenSource? _loop;

    /// <summary>
    /// Whether a frame is being worked out, so that a loop started while an older one finishes its last frame never
    /// uses the engine or the pixels at the same time.
    /// </summary>
    private bool _drawing;

    /// <summary>Creates the field; it takes no input of its own and never takes focus.</summary>
    public AsciiField()
    {
        IsHitTestVisible = false;
        Focusable = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        IsVisibleChanged += (_, _) => Restart();
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>Gets or sets the form the field is quiet around.</summary>
    public FrameworkElement? Form
    {
        get => (FrameworkElement?)GetValue(FormProperty);
        set => SetValue(FormProperty, value);
    }

    /// <summary>Draws the last frame, if there is one.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_bitmap is { } bitmap && _hasFrame && _atlas is { } atlas)
        {
            var size = new Size(bitmap.PixelWidth / atlas.Scale, bitmap.PixelHeight / atlas.Scale);
            drawingContext.DrawImage(bitmap, new Rect(size));
        }
    }

    /// <summary>Draws the masks again for the new scale when the window moves to another screen.</summary>
    /// <param name="oldDpi">The old scale.</param>
    /// <param name="newDpi">The new scale.</param>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _atlas = null;
        base.OnDpiChanged(oldDpi, newDpi);
    }

    /// <summary>Starts the loop when the field is shown, and stops it when it is not.</summary>
    private void Restart()
    {
        Stop();
        if (IsVisible && IsLoaded)
        {
            _loop = new CancellationTokenSource();
            _ = RunAsync(_loop.Token);
        }
    }

    /// <summary>Stops the loop.</summary>
    private void Stop()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
    }

    /// <summary>
    /// The loop: on each tick, takes what the page looks like on the UI thread, works out and draws the frame on the
    /// thread pool, and copies the pixels back on the UI thread. Ticks that come while a frame is worked out are
    /// dropped, so that the loop never piles up.
    /// </summary>
    /// <param name="token">Stops the loop.</param>
    /// <returns>A task that ends when the loop is stopped.</returns>
    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(FrameTime);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (_drawing || Prepare() is not { } job || !_engine.NeedsFrame(job.Input))
                {
                    continue;
                }

                _drawing = true;
                try
                {
                    await Task.Run(() => FieldRasterizer.Draw(
                        _engine.Next(job.Input), job.Atlas, FieldPalette.Default, job.Pixels, job.Width, job.Height));
                }
                finally
                {
                    _drawing = false;
                }

                Present(job);
            }
        }
        catch (OperationCanceledException)
        {
            // The field was hidden or unloaded.
        }
    }

    /// <summary>Takes what the next frame needs from the page, making the masks and the pixels for its size.</summary>
    /// <returns>The frame's job, or <see langword="null"/> when nothing is to be drawn now.</returns>
    private FrameJob? Prepare()
    {
        var window = Window.GetWindow(this);
        if (ActualWidth < 1 || ActualHeight < 1 || window is null || window.WindowState == WindowState.Minimized)
        {
            return null;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (_atlas is null || _atlas.Scale != scale)
        {
            _atlas = GlyphAtlasFactory.Create(scale);
        }

        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Bgr32, null);
            _pixels = new int[width * height];
            _hasFrame = false;
        }

        var input = new FieldInput(
            width / scale, height / scale, _atlas.CellWidth, _atlas.CellHeight, _clock.Elapsed.TotalSeconds,
            SystemParameters.ClientAreaAnimation, Pointer(window), FormBounds());
        return new FrameJob(input, _atlas, _pixels, width, height);
    }

    /// <summary>Copies a finished frame into the bitmap shown, unless the page changed size meanwhile.</summary>
    /// <param name="job">The frame's job.</param>
    private void Present(FrameJob job)
    {
        if (_bitmap is not { } bitmap || bitmap.PixelWidth != job.Width || bitmap.PixelHeight != job.Height)
        {
            return;
        }

        bitmap.WritePixels(new Int32Rect(0, 0, job.Width, job.Height), job.Pixels, job.Width * 4, 0);
        if (!_hasFrame)
        {
            _hasFrame = true;
            InvalidateVisual();
        }
    }

    /// <summary>Gives the mouse over the field and whether Ctrl is held, while the window is active.</summary>
    /// <param name="window">The window.</param>
    /// <returns>The mouse, or <see langword="null"/> when it is not over the field.</returns>
    private FieldPointer? Pointer(Window window)
    {
        if (!window.IsActive)
        {
            return null;
        }

        var position = Mouse.GetPosition(this);
        if (position.X < 0 || position.Y < 0 || position.X > ActualWidth || position.Y > ActualHeight)
        {
            return null;
        }

        return new FieldPointer(position.X, position.Y, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
    }

    /// <summary>Gives the form's bounds on the field, or <see langword="null"/> when no form is shown.</summary>
    /// <returns>The bounds.</returns>
    private FieldRect? FormBounds()
    {
        if (Form is not { IsVisible: true } form)
        {
            return null;
        }

        var bounds = form.TransformToVisual(this).TransformBounds(new Rect(form.RenderSize));
        return new FieldRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    /// <summary>What one frame needs: its input, the masks and the pixels to draw into.</summary>
    /// <param name="Input">What the page looks like.</param>
    /// <param name="Atlas">The masks.</param>
    /// <param name="Pixels">The pixels.</param>
    /// <param name="Width">The pixels' width.</param>
    /// <param name="Height">The pixels' height.</param>
    private sealed record FrameJob(FieldInput Input, GlyphAtlas Atlas, int[] Pixels, int Width, int Height);
}
