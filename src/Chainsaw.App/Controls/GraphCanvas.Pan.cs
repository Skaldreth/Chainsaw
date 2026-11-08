using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.Motion;

namespace Chainsaw.App.Controls;

/// <summary>
/// The pan: the right or the middle button drags the surface anywhere, also over a node, and a pan let go while moving
/// glides on and slows down, as in Gawk (the user's decision 2026-11-07). A right click that does not move opens the
/// node's menu as before.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How far the pointer must move before a press of a pan button becomes a pan, in screen pixels.</summary>
    private const double PanThreshold = 3;

    /// <summary>A pan let go slower than this does not glide, in pixels per second.</summary>
    private const double GlideMinSpeed = 120;

    /// <summary>A glide stops once slower than this, in pixels per second.</summary>
    private const double GlideStopSpeed = 15;

    /// <summary>The glide's time constant: its speed fades by e every this many seconds.</summary>
    private const double GlideFade = 0.22;

    /// <summary>The pointer's speed during a pan, for the glide.</summary>
    private readonly VelocityTracker _panVelocity = new();

    /// <summary>The time between the glide's frames.</summary>
    private readonly FrameClock _glideClock = new();

    /// <summary>The button that holds the pan, or <see langword="null"/> while none does.</summary>
    private MouseButton? _panButton;

    /// <summary>Where the pan button was pressed, on screen.</summary>
    private Point _panFrom;

    /// <summary>Where the pointer was at the last move of the pan, on screen.</summary>
    private Point _panLast;

    /// <summary>Whether the press has moved far enough to be a pan, and not a click.</summary>
    private bool _panMoved;

    /// <summary>The glide's speed, in pixels per second, while it runs.</summary>
    private Vector _glideVelocity;

    /// <summary>Whether the surface glides after a pan.</summary>
    private bool _gliding;

    /// <summary>Starts a pan with the right button.</summary>
    /// <param name="e">The press.</param>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseRightButtonDown(e);
        BeginPan(e, MouseButton.Right);
    }

    /// <summary>Starts a pan with the middle button.</summary>
    /// <param name="e">The press.</param>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            BeginPan(e, MouseButton.Middle);
        }
    }

    /// <summary>Ends a pan with the middle button.</summary>
    /// <param name="e">The release.</param>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            EndPan(e);
        }
    }

    /// <summary>Takes hold of the surface to pan it, catching a glide that still runs.</summary>
    /// <param name="e">The press.</param>
    /// <param name="button">The button that holds the pan.</param>
    private void BeginPan(MouseButtonEventArgs e, MouseButton button)
    {
        if (_panButton is not null || _gesture.IsPressed || _blast != BlastPhase.None)
        {
            // The selection view stands still: it is drawn in screen units, not on the surface.
            return;
        }

        StopGlide();
        CloseHoverTip();
        Focus();
        _panButton = button;
        _panFrom = e.GetPosition(this);
        _panLast = _panFrom;
        _panMoved = false;
        _panVelocity.Start(e.Timestamp, _panFrom.X, _panFrom.Y);
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Moves the surface with the pointer while a pan button is held.</summary>
    /// <param name="e">The move.</param>
    /// <returns>Whether the move belonged to a pan.</returns>
    private bool MovePan(MouseEventArgs e)
    {
        if (_panButton is null)
        {
            return false;
        }

        var at = e.GetPosition(this);
        if (!_panMoved && (at - _panFrom).Length < PanThreshold)
        {
            return true;
        }

        if (!_panMoved)
        {
            _panMoved = true;
            _model?.CloseMenu();
            Mouse.OverrideCursor = Cursors.SizeAll;
        }

        _viewport = _viewport.Pan(at.X - _panLast.X, at.Y - _panLast.Y);
        _panLast = at;
        _panVelocity.Add(e.Timestamp, at.X, at.Y);
        InvalidateVisual();
        return true;
    }

    /// <summary>Lets go of a pan: it glides on if it was let go moving, and the place is stored.</summary>
    /// <param name="e">The release.</param>
    /// <returns>Whether the press had become a pan; a press that did not move is a click.</returns>
    private bool EndPan(MouseButtonEventArgs e)
    {
        if (_panButton is null)
        {
            return false;
        }

        var moved = _panMoved;
        var (x, y) = moved ? _panVelocity.Release(e.Timestamp) : default;
        StopPan();
        if (moved)
        {
            _model?.ViewportChanged(_viewport);
            StartGlide(new Vector(x, y));
        }

        e.Handled = moved;
        return moved;
    }

    /// <summary>Forgets the pan and gives back the pointer.</summary>
    private void StopPan()
    {
        _panButton = null;
        _panMoved = false;
        Mouse.OverrideCursor = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// Lets the surface glide on after a pan, slowing down, on the frames of the rendering; not when Windows has
    /// animations turned off.
    /// </summary>
    /// <param name="velocity">The pointer's speed at the release.</param>
    private void StartGlide(Vector velocity)
    {
        StopGlide();
        if (velocity.Length < GlideMinSpeed || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        _glideVelocity = velocity;
        _glideClock.Reset();
        _gliding = true;
        CompositionTarget.Rendering += OnGlideFrame;
    }

    /// <summary>Stops a glide where it is, and stores the place it reached.</summary>
    private void StopGlide()
    {
        if (!_gliding)
        {
            return;
        }

        _gliding = false;
        CompositionTarget.Rendering -= OnGlideFrame;
        _model?.ViewportChanged(_viewport);
    }

    /// <summary>Moves the surface one frame of the glide, by the rendering time rather than counting frames.</summary>
    /// <param name="sender">The rendering.</param>
    /// <param name="e">The frame.</param>
    private void OnGlideFrame(object? sender, EventArgs e)
    {
        if (_glideClock.Tick(((RenderingEventArgs)e).RenderingTime) is not { } seconds)
        {
            return;
        }

        _viewport = _viewport.Pan(
            Easing.GlideDistance(_glideVelocity.X, seconds, GlideFade),
            Easing.GlideDistance(_glideVelocity.Y, seconds, GlideFade));
        _glideVelocity *= Easing.Fade(seconds, GlideFade);
        InvalidateVisual();
        if (_glideVelocity.Length < GlideStopSpeed)
        {
            StopGlide();
        }
    }
}
