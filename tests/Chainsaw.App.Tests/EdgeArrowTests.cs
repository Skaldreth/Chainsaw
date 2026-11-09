using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>The chevron on a line that shows which way the money flows.</summary>
public sealed class EdgeArrowTests
{
    /// <summary>Nothing stands on the line.</summary>
    private static readonly GraphRect[] Nothing = [];

    /// <summary>On a forward line the arrow stands at the middle and points to the right, towards the receiver.</summary>
    [Fact]
    public void ForwardArrowPointsTowardsTheReceiver()
    {
        var curve = EdgeCurve.Between(new GraphPoint(0, 0), new GraphPoint(400, 0), hopColumns: false);

        var arrow = EdgeArrow.On(curve, 2, Nothing)!;

        Assert.True(arrow.Tip.X > arrow.Wing1.X && arrow.Tip.X > arrow.Wing2.X);
        Assert.True(arrow.Bounds().Inflate(1).Contains(curve.Middle));
    }

    /// <summary>On money that turns back the arrow still points the way the line runs: left, over the top.</summary>
    [Fact]
    public void ArrowOnALineThatTurnsBackPointsLeftOverTheTop()
    {
        var curve = EdgeCurve.Between(new GraphPoint(600, 0), new GraphPoint(0, 0), hopColumns: true);

        var arrow = EdgeArrow.On(curve, 2, Nothing)!;

        Assert.True(arrow.Tip.X < arrow.Wing1.X && arrow.Tip.X < arrow.Wing2.X);
        Assert.True(arrow.Tip.Y < 0);
    }

    /// <summary>The arrow keeps clear of the amount and its wings reach out past a thick line.</summary>
    [Fact]
    public void ArrowKeepsClearOfTheAmountAndReachesPastTheLine()
    {
        var curve = EdgeCurve.Between(new GraphPoint(0, 0), new GraphPoint(500, 0), hopColumns: false);
        var amount = new GraphRect(curve.Middle.X - 40, curve.Middle.Y - 10, 80, 20);

        var arrow = EdgeArrow.On(curve, 8, [amount])!;

        Assert.False(arrow.Bounds().Intersects(amount));
        Assert.True(arrow.Bounds().Height >= 8 + (2 * EdgeArrow.WingReach) - 0.001);
    }

    /// <summary>A line too short to carry an arrow is drawn plain between its ports.</summary>
    [Fact]
    public void ShortLineHasNoArrow()
    {
        var curve = new EdgeCurve(
            new GraphPoint(0, 0), new GraphPoint(10, 0), new GraphPoint(20, 0), new GraphPoint(30, 0), IsArc: false);

        Assert.Null(EdgeArrow.On(curve, 2, Nothing));
    }
}
