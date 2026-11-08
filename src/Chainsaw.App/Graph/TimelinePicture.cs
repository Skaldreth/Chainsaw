namespace Chainsaw.App.Graph;

/// <summary>An event placed on the lanes.</summary>
/// <param name="Event">The event.</param>
/// <param name="X">Where it stands.</param>
/// <param name="Lane">The lane it starts on: the sender's, or the case's own for a case event.</param>
/// <param name="ToLane">The lane it ends on: the receiver's, or the actor's a case event concerns.</param>
/// <param name="IsHidden">Whether its chip is off, so that it is drawn faint.</param>
/// <param name="Amount">The amount written beside it, or an empty text when there is no room.</param>
/// <param name="Value">The value written under the amount, or an empty text.</param>
/// <param name="Thickness">How thick its stroke is: thicker the larger the amount (GR3a).</param>
public sealed record PlacedEvent(
    TimelineEvent Event, double X, int Lane, int ToLane, bool IsHidden, string Amount, string Value, double Thickness);

/// <summary>Transfers so close that they share a pixel column: one number, which zooms to hours (sketch TL3a).</summary>
/// <param name="Items">The transfers, oldest first.</param>
/// <param name="X">Where the number stands.</param>
/// <param name="X0">Where the first stands.</param>
/// <param name="X1">Where the last stands.</param>
/// <param name="Lane">The sender's lane, above which the number stands.</param>
/// <param name="Top">The highest lane any of them touches.</param>
/// <param name="Bottom">The lowest.</param>
/// <param name="ToolTip">
/// What the pointer's tooltip says, for example <c>3 tx på 25 min · klicka för timmar</c>.
/// </param>
public sealed record TimelineBundle(
    IReadOnlyList<TimelineEvent> Items, double X, double X0, double X1, int Lane, int Top, int Bottom, string ToolTip);

/// <summary>What a click on the lanes hit.</summary>
/// <param name="EventId">The event's id, or <see langword="null"/> for a bundle.</param>
/// <param name="Bundle">The bundle, or <see langword="null"/>.</param>
/// <param name="ToolTip">What the pointer's tooltip says.</param>
public sealed record TimelineHit(string? EventId, TimelineBundle? Bundle, string ToolTip);

/// <summary>
/// The lanes of the view Tidslinje laid out (sketch TL3a): where every lane and event stands, the bundles, the day or
/// hour lines and the cuts. No WPF, so that it is tested; the control only draws it and scrolls the lanes.
/// </summary>
/// <param name="Lanes">The lanes, top down.</param>
/// <param name="Events">The events shown.</param>
/// <param name="Bundles">The bundles.</param>
/// <param name="Scale">The time along the x axis.</param>
/// <param name="NowX">Where now stands, or <see langword="null"/> when it is not shown.</param>
/// <param name="SelectedId">The chosen event's id, or <see langword="null"/>.</param>
/// <param name="Lit">The lanes the chosen event touches.</param>
public sealed record TimelinePicture(
    IReadOnlyList<TimelineLane> Lanes,
    IReadOnlyList<PlacedEvent> Events,
    IReadOnlyList<TimelineBundle> Bundles,
    TimelineScale Scale,
    double? NowX,
    string? SelectedId,
    IReadOnlySet<string> Lit)
{
    /// <summary>How tall a lane is.</summary>
    public const double LaneHeight = 43;

    /// <summary>Where the first lane starts, below the day labels.</summary>
    public const double Top = 46;

    /// <summary>How wide the lanes' names are, left of the time.</summary>
    public const double NamesWidth = 160;

    /// <summary>How far the case's own lane stands apart from the actors'.</summary>
    public const double CaseGap = 10;

    /// <summary>How close two transfers may stand before they are bundled.</summary>
    public const double BundleDistance = 9;

    /// <summary>Gets an empty picture.</summary>
    public static TimelinePicture None { get; } = new(
        [], [], [], TimelineScale.Days([], TimeZoneInfo.Utc, 0), null, null, new HashSet<string>());

    /// <summary>Gets how tall the lanes are together.</summary>
    public double Height => Top + (Lanes.Count * LaneHeight) + CaseGap + 8;

    /// <summary>Gives the middle of a lane, from the top of the picture.</summary>
    /// <param name="lane">The lane's index.</param>
    /// <returns>The y.</returns>
    public double LaneY(int lane)
    {
        var apart = lane >= 0 && lane < Lanes.Count && Lanes[lane].IsCase ? CaseGap : 0;
        return Top + (lane * LaneHeight) + (LaneHeight / 2) + apart;
    }

    /// <summary>Gives what lies under a point, the y counted from the top of the picture.</summary>
    /// <param name="x">The point's x, from the left of the time.</param>
    /// <param name="y">The point's y.</param>
    /// <returns>The hit, or <see langword="null"/>.</returns>
    public TimelineHit? At(double x, double y)
    {
        foreach (var bundle in Bundles)
        {
            var top = Math.Min(LaneY(bundle.Top), LaneY(bundle.Lane) - 24);
            if (x >= bundle.X0 - 9 && x <= bundle.X1 + 9 && y >= top && y <= LaneY(bundle.Bottom) + 8)
            {
                return new TimelineHit(null, bundle, bundle.ToolTip);
            }
        }

        foreach (var placed in Events.Where(each => !Bundled(each.Event)))
        {
            if (Math.Abs(x - placed.X) > 10)
            {
                continue;
            }

            var (a, b) = (LaneY(placed.Lane), LaneY(placed.ToLane));
            if (y >= Math.Min(a, b) - 12 && y <= Math.Max(a, b) + 12)
            {
                return new TimelineHit(placed.Event.Id, null, TipOf(placed));
            }
        }

        return null;
    }

    /// <summary>Gives whether an event is drawn in a bundle, where a click zooms instead of choosing it.</summary>
    /// <param name="timelineEvent">The event.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Bundled(TimelineEvent timelineEvent) =>
        Bundles.Any(bundle => bundle.Items.Contains(timelineEvent));

    /// <summary>Gives an event's tooltip: the time and the amount, or the case event's title.</summary>
    /// <param name="placed">The event.</param>
    /// <returns>The tooltip.</returns>
    private string TipOf(PlacedEvent placed)
    {
        var timelineEvent = placed.Event;
        if (!timelineEvent.IsTransfer)
        {
            return timelineEvent.Title;
        }

        var when = timelineEvent.Time is { } time ? TimelineScale.Short(time, Scale.Zone) : "Obekräftad";
        return timelineEvent.Amount is { } amount ? $"{when} · {AmountText.WithUnit(amount)}" : when;
    }
}
