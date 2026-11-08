using System.Text.RegularExpressions;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph surface's frames, read from its source since WPF does not run in the tests: a fit drops every motion that
/// would move the new place on, the frames stop while the surface is hidden and are taken up again when it is shown,
/// the loop skips its frames while the window is minimised, and only the loop holds frames.
/// </summary>
public sealed class CanvasFramesTests
{
    /// <summary>A fit, a fit to some nodes and the place the case stored drop the pan's glide and the search's glide.</summary>
    [Fact]
    public void A_fit_drops_the_glides()
    {
        var zoom = Body(Read("GraphCanvas.Zoom.cs"), "private void DropZoom()");

        Assert.Contains("DropGlide();", zoom, StringComparison.Ordinal);
        Assert.Contains("DropFoundGlide();", zoom, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewportChanged", Body(Read("GraphCanvas.Pan.cs"), "private bool DropGlide()"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Hidden, the card timer stops; shown again, the moving frames are taken up, so that a blast home that was halfway
    /// ends instead of keeping the surface from the wheel and the pointer.
    /// </summary>
    [Fact]
    public void Hiding_stops_the_cards_and_showing_takes_up_the_motion()
    {
        var source = Read("GraphCanvas.cs");
        var handler = source[source.IndexOf("IsVisibleChanged +=", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("};", StringComparison.Ordinal)];

        Assert.Contains("KeepTurning(false);", handler, StringComparison.Ordinal);
        Assert.Contains("KeepMoving();", handler, StringComparison.Ordinal);
    }

    /// <summary>No frame of the surface or the time strip is drawn while the window is minimised.</summary>
    [Fact]
    public void Frames_are_skipped_while_minimised()
    {
        Assert.Contains("IsMinimised(this)", Body(Read("GraphCanvas.Frames.cs"), "private void OnFrame("),
            StringComparison.Ordinal);
        Assert.Contains("IsMinimised(this)", Body(Read("TimeStrip.cs"), "private void OnSonarFrame"),
            StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"DispatcherTimer\([^)]*=> InvalidateVisual"), Read("TimeStrip.cs"));
    }

    /// <summary>
    /// The surface has one frame loop (GraphCanvas.Frames.cs): no other part of it holds the screen's frames, runs a
    /// timer of its own or asks WPF to draw; they ask the loop for a change or start a motion.
    /// </summary>
    [Fact]
    public void Only_the_frame_loop_holds_frames_and_asks_WPF_to_draw()
    {
        var controls = Path.Combine(GraphSceneTests.Root(), "src", "Chainsaw.App", "Controls");
        foreach (var path in Directory.GetFiles(controls, "GraphCanvas*.cs"))
        {
            var name = Path.GetFileName(path);
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
            if (name != "GraphCanvas.Frames.cs")
            {
                Assert.DoesNotContain("CompositionTarget.Rendering", source, StringComparison.Ordinal);
            }

            if (name is not ("GraphCanvas.Frames.cs" or "GraphCanvas.Scene.cs"))
            {
                Assert.DoesNotContain("InvalidateVisual()", source, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Reads a control's source.</summary>
    /// <param name="file">The file under the controls.</param>
    /// <returns>Its text.</returns>
    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(GraphSceneTests.Root(), "src", "Chainsaw.App", "Controls", file));

    /// <summary>Gives a member's body, up to the first closing brace at its own indentation.</summary>
    /// <param name="source">The source.</param>
    /// <param name="signature">The member's signature.</param>
    /// <returns>The body.</returns>
    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var rest = source[start..];
        return rest[..rest.IndexOf("\n    }", StringComparison.Ordinal)];
    }
}
