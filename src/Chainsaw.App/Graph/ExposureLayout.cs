using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// The graph's names and looks for the view Exponering: the actors drawn, and every node's sort, also one in the trash.
/// </summary>
/// <param name="Actors">The actors drawn, by key.</param>
/// <param name="Looks">Every node's sort, by key.</param>
public sealed record ExposureNames(
    IReadOnlyDictionary<string, NodeView> Actors, IReadOnlyDictionary<string, SortLook> Looks)
{
    /// <summary>Gets names and looks for no graph.</summary>
    public static ExposureNames None { get; } =
        new(new Dictionary<string, NodeView>(), new Dictionary<string, SortLook>());

    /// <summary>Reads them from a graph.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The names and looks.</returns>
    public static ExposureNames Of(GraphScene scene) => new(ExposureMap.Actors(scene), ExposureMap.Looks(scene));

    /// <summary>Gives a node's sort: as drawn, as its labels make it, or unknown.</summary>
    /// <param name="key">The node.</param>
    /// <returns>The sort's look.</returns>
    public SortLook LookOf(string key) =>
        Actors.GetValueOrDefault(key)?.Look ?? Looks.GetValueOrDefault(key) ?? VisualLanguage.Of(NodeSort.Unknown);

    /// <summary>Gives a node's name: as drawn, or its key's subject shortened.</summary>
    /// <param name="key">The node.</param>
    /// <returns>The name.</returns>
    public string NameOf(string key) => Actors.GetValueOrDefault(key)?.Name
        ?? GraphScene.Shorten(key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..]);
}

/// <summary>
/// A way out of an actor that the user approved among a suggestion's candidates (sketch KF2b), as a mixer's likely
/// payout: a judgement, never a fact (principle 2).
/// </summary>
/// <param name="From">The actor the suggestion is about.</param>
/// <param name="To">The approved candidate's actor.</param>
public sealed record ApprovedWayOut(string From, string To);

/// <summary>
/// An actor in the rings of the view Exponering (sketch EP1b): a service as a node in the visual language, larger the
/// larger its share, or an actor on the way as a small point.
/// </summary>
/// <param name="Key">The actor's node.</param>
/// <param name="Side">Paid out (right) or got (left).</param>
/// <param name="At">Its centre, in the picture's units.</param>
/// <param name="Size">Its width and height.</param>
/// <param name="Hop">Its ring.</param>
/// <param name="IsService">Whether the money stops here and is counted; otherwise it is an actor on the way.</param>
/// <param name="Look">Its sort.</param>
/// <param name="Name">Its name, as the graph gives it.</param>
/// <param name="Line">Its second line: the amount and the share, for a service.</param>
/// <param name="IsSanctioned">Whether it carries the sanction mark.</param>
/// <param name="IsCandidate">
/// Whether it is an approved likely way out beyond a mixer: drawn dashed, with <em>förslag</em> instead of a share,
/// and never counted (TODO, <em>Exponering, mixerns troliga utgångar</em>).
/// </param>
public sealed record RingPlace(
    string Key,
    ExposureDirection Side,
    GraphPoint At,
    double Size,
    int Hop,
    bool IsService,
    SortLook Look,
    string Name,
    string Line,
    bool IsSanctioned,
    bool IsCandidate = false)
{
    /// <summary>Gets the place's id: the side and the key, since an actor may stand on both sides.</summary>
    public string Id => ExposureLayout.IdOf(Side, Key);

    /// <summary>Gets the colour its frame and icon are drawn in.</summary>
    public GraphInk Ink => Look.Ink;
}

/// <summary>A line of the money in the rings, as thick as its share; the last step into a service in its colour.</summary>
/// <param name="EdgeKey">The graph's line.</param>
/// <param name="Side">Paid out or got.</param>
/// <param name="From">Its end nearer the subject.</param>
/// <param name="To">Its end farther away.</param>
/// <param name="Width">Its thickness.</param>
/// <param name="Ink">Its colour.</param>
/// <param name="IsDotted">Whether it is a suggestion, drawn dotted: the way to an approved likely way out.</param>
public sealed record RingLine(
    string EdgeKey,
    ExposureDirection Side,
    GraphPoint From,
    GraphPoint To,
    double Width,
    GraphInk Ink,
    bool IsDotted = false)
{
    /// <summary>
    /// Gets the curve's control point: the middle, pushed a little away from the centre, so that the lines bow
    /// outwards as in the sketch.
    /// </summary>
    public GraphPoint Bend
    {
        get
        {
            var middle = new GraphPoint((From.X + To.X) / 2, (From.Y + To.Y) / 2);
            var centre = ExposureLayout.Centre;
            return new GraphPoint(
                middle.X - ((centre.X - middle.X) * ExposureLayout.Bow),
                middle.Y - ((centre.Y - middle.Y) * ExposureLayout.Bow));
        }
    }
}

/// <summary>The picture of the view Exponering: the rings, the services, the actors on the way and the lines.</summary>
/// <param name="Depth">How many rings are lit.</param>
/// <param name="Services">The services, both sides.</param>
/// <param name="Via">The actors on the way.</param>
/// <param name="Lines">The money's lines.</param>
/// <param name="SubjectName">The subject's name, under the centre.</param>
/// <param name="SubjectLook">The subject's sort, drawn in the centre.</param>
/// <param name="InText">The left side's words at the bottom, for example <c>← Fått från · 0,7800 BTC</c>.</param>
/// <param name="OutText">The right side's words at the bottom.</param>
public sealed record ExposurePicture(
    int Depth,
    IReadOnlyList<RingPlace> Services,
    IReadOnlyList<RingPlace> Via,
    IReadOnlyList<RingLine> Lines,
    string SubjectName,
    SortLook SubjectLook,
    string InText,
    string OutText)
{
    /// <summary>Gets an empty picture, drawn as nothing.</summary>
    public static ExposurePicture None { get; } = new(
        1, [], [], [], string.Empty, VisualLanguage.Of(NodeSort.Cluster), string.Empty, string.Empty);

    /// <summary>Gets whether there is anything to draw.</summary>
    public bool HasSubject => SubjectName.Length > 0;

    /// <summary>
    /// Gets the approved likely ways out beyond the mixers the money was paid to, which are suggestions and never
    /// counted (TODO, <em>Exponering, mixerns troliga utgångar</em>).
    /// </summary>
    public IReadOnlyList<RingPlace> Candidates { get; init; } = [];

    /// <summary>Gives what is under a point: a service or an actor on the way, with its name beside it.</summary>
    /// <param name="point">The point, in the picture's units.</param>
    /// <returns>The place, or <see langword="null"/>.</returns>
    public RingPlace? At(GraphPoint point)
    {
        foreach (var place in Services.Concat(Via))
        {
            var half = (place.Size / 2) + ExposureLayout.HitSlack;
            var dy = Math.Abs(point.Y - place.At.Y);
            var dx = point.X - place.At.X;
            var labelSide = place.Side == ExposureDirection.Out ? dx : -dx;
            var onNode = Math.Abs(dx) <= half && dy <= half;
            var onLabel = place.IsService && labelSide >= 0 && labelSide <= half + ExposureLayout.LabelWidth && dy <= half;
            if (onNode || onLabel)
            {
                return place;
            }
        }

        return null;
    }
}

/// <summary>
/// Lays out the rings of the view Exponering (sketch EP1b), without WPF: the subject in the centre, a ring per hop,
/// what was paid out to the right and what came in to the left. On each ring the actors are spread over their side in
/// the order of the actor they came from, so that a way keeps together. The units are the sketch's picture
/// (<see cref="Width"/> × <see cref="Height"/>); the view scales it to its room.
/// </summary>
public static class ExposureLayout
{
    /// <summary>The picture's width.</summary>
    public const double Width = 960;

    /// <summary>The picture's height.</summary>
    public const double Height = 832;

    /// <summary>How far the lines bow outwards, as a share of the way to the centre.</summary>
    public const double Bow = 0.08;

    /// <summary>The slack around a node that still counts as on it.</summary>
    public const double HitSlack = 4;

    /// <summary>How wide a service's name and line are, beside it.</summary>
    public const double LabelWidth = 130;

    /// <summary>The size of an actor on the way.</summary>
    public const double ViaSize = 14;

    /// <summary>The smallest service.</summary>
    private const double SmallestService = 24;

    /// <summary>How much a whole share adds to a service, by the square root so that the area follows the share.</summary>
    private const double ServiceGrowth = 30;

    /// <summary>The thinnest line.</summary>
    private const double ThinnestLine = 1.5;

    /// <summary>How much a whole share adds to a line.</summary>
    private const double LineGrowth = 13;

    /// <summary>The widest a side spreads, in degrees, from its top to its bottom.</summary>
    private const double SideSpan = 150;

    /// <summary>The ring the money of each hop stands on: its radius, from the centre outwards.</summary>
    public static IReadOnlyList<double> Rings { get; } = [0, 130, 232, 330];

    /// <summary>
    /// Where the likely ways out beyond a mixer on the last ring stand: past the last ring, inside the picture.
    /// </summary>
    public const double Beyond = 404;

    /// <summary>The word a likely way out has in place of its share.</summary>
    public const string CandidateWord = "förslag";

    /// <summary>The room between two likely ways out of one mixer, in degrees.</summary>
    private const double CandidateStep = 9;

    /// <summary>How far from the side's middle a likely way out may stand, in degrees, so that it stays in the picture.</summary>
    private const double CandidateSpan = 62;

    /// <summary>The most room between two actors on a ring, in degrees, per ring.</summary>
    private static readonly double[] WidestStep = [0, 40, 30, 24];

    /// <summary>Gets the centre, where the subject stands.</summary>
    public static GraphPoint Centre { get; } = new(470, 448);

    /// <summary>Gives a place's id.</summary>
    /// <param name="side">Its side.</param>
    /// <param name="key">Its node.</param>
    /// <returns>For example <c>out:cluster:bc1q…</c>.</returns>
    public static string IdOf(ExposureDirection side, string key) =>
        (side == ExposureDirection.Out ? "out:" : "in:") + key;

    /// <summary>Lays out the picture.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="paid">What it paid out, or <see langword="null"/>.</param>
    /// <param name="got">What it got, or <see langword="null"/>.</param>
    /// <param name="graph">The graph's actors and sorts, for their names and looks.</param>
    /// <param name="waysOut">The approved likely ways out, drawn beyond the services they leave; none when null.</param>
    /// <returns>The picture.</returns>
    public static ExposurePicture Build(
        NodeView subject,
        ExposureResult? paid,
        ExposureResult? got,
        ExposureNames graph,
        IReadOnlyList<ApprovedWayOut>? waysOut = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(graph);
        var depth = paid?.Depth ?? got?.Depth ?? 1;
        var places = new List<RingPlace>();
        var lines = new List<RingLine>();
        foreach (var result in new[] { paid, got }.OfType<ExposureResult>())
        {
            var side = Side(result, graph);
            places.AddRange(side.Places);
            lines.AddRange(side.Lines);
        }

        var candidates = WaysOut(subject.Key, places, waysOut ?? [], graph);
        lines.AddRange(candidates.Lines);
        return new ExposurePicture(
            depth,
            [.. places.Where(place => place.IsService)],
            [.. places.Where(place => !place.IsService)],
            lines,
            subject.Name,
            subject.Look,
            "← Fått från · " + Total(got),
            "Betalat till · " + Total(paid) + " →")
        {
            Candidates = candidates.Places,
        };
    }

    /// <summary>
    /// Places the approved likely ways out beyond each service the money was paid out to (TODO, <em>Exponering,
    /// mixerns troliga utgångar</em>): on the next ring around the service's angle, or past the last ring, each with a
    /// dotted line from the service. A way out to an actor already drawn on that side is left out, since the money's
    /// own line reaches it, and so is one back to the subject.
    /// </summary>
    /// <param name="subject">The subject's key.</param>
    /// <param name="places">The actors placed on both sides.</param>
    /// <param name="waysOut">The approved ways out.</param>
    /// <param name="graph">The graph's names and looks.</param>
    /// <returns>The likely ways out and their lines.</returns>
    private static (List<RingPlace> Places, List<RingLine> Lines) WaysOut(
        string subject, IReadOnlyList<RingPlace> places, IReadOnlyList<ApprovedWayOut> waysOut, ExposureNames graph)
    {
        var paid = places.Where(place => place.Side == ExposureDirection.Out).ToList();
        var drawn = paid.Select(place => place.Key).Append(subject).ToHashSet(StringComparer.Ordinal);
        var result = (Places: new List<RingPlace>(), Lines: new List<RingLine>());
        foreach (var service in paid.Where(place => place.IsService))
        {
            var ways = waysOut.Where(way => way.From == service.Key && drawn.Add(way.To)).ToList();
            var radius = service.Hop + 1 < Rings.Count ? Rings[service.Hop + 1] : Beyond;
            var angle = Math.Atan2(service.At.Y - Centre.Y, service.At.X - Centre.X) * 180 / Math.PI;
            // The group is moved, not each one clamped, so that ways out near the edge never lie on each other.
            var spread = (ways.Count - 1) * CandidateStep;
            var last = Math.Max(-CandidateSpan, CandidateSpan - spread);
            var first = Math.Clamp(angle - (spread / 2), -CandidateSpan, last);
            for (var index = 0; index < ways.Count; index++)
            {
                var radians = (first + (index * CandidateStep)) * Math.PI / 180;
                var at = new GraphPoint(Centre.X + (radius * Math.Cos(radians)), Centre.Y + (radius * Math.Sin(radians)));
                var key = ways[index].To;
                var look = graph.LookOf(key);
                result.Places.Add(new RingPlace(
                    key, ExposureDirection.Out, at, SmallestService, service.Hop + 1, false, look, graph.NameOf(key),
                    CandidateWord, false, IsCandidate: true));
                result.Lines.Add(new RingLine(
                    service.Key + ">" + key, ExposureDirection.Out, service.At, at, ThinnestLine, look.Ink,
                    IsDotted: true));
            }
        }

        return result;
    }

    /// <summary>Writes a side's total with its unit, or a dash when it was not read.</summary>
    /// <param name="result">The side.</param>
    /// <returns>The text.</returns>
    private static string Total(ExposureResult? result) => result is null ? "–" : AmountText.WithUnit(result.Total);

    /// <summary>Lays out one side: its actors ring by ring, and its lines.</summary>
    /// <param name="result">The side's exposure.</param>
    /// <param name="graph">The graph's names and looks.</param>
    /// <returns>The places and lines.</returns>
    private static (List<RingPlace> Places, List<RingLine> Lines) Side(ExposureResult result, ExposureNames graph)
    {
        var arrived = result.Flows.GroupBy(flow => flow.Far, StringComparer.Ordinal).ToDictionary(
            group => group.Key,
            group => group.Aggregate(AssetAmount.Zero(result.Total.Asset), (sum, flow) => sum + flow.Amount),
            StringComparer.Ordinal);
        var parent = result.Flows.GroupBy(flow => flow.Far, StringComparer.Ordinal).ToDictionary(
            group => group.Key, group => group.MaxBy(flow => flow.Amount.Units)!.Near, StringComparer.Ordinal);
        var reached = result.Reached.ToDictionary(reach => reach.NodeKey, StringComparer.Ordinal);
        var offsets = new Dictionary<string, double>(StringComparer.Ordinal) { [result.Subject] = 0 };
        var places = new Dictionary<string, RingPlace>(StringComparer.Ordinal);
        for (var hop = 1; hop <= result.Depth; hop++)
        {
            var ring = arrived.Keys
                .Where(key => key != result.Subject && result.Hops.GetValueOrDefault(key, int.MaxValue) == hop)
                .OrderBy(key => offsets.GetValueOrDefault(parent[key]))
                .ThenByDescending(key => arrived[key].Units)
                .ThenBy(key => key, StringComparer.Ordinal)
                .ToList();
            var step = ring.Count > 1 ? Math.Min(WidestStep[hop], SideSpan / (ring.Count - 1)) : 0;
            for (var index = 0; index < ring.Count; index++)
            {
                var key = ring[index];
                var offset = (index - ((ring.Count - 1) / 2.0)) * step;
                offsets[key] = offset;
                places[key] = Place(result, key, hop, offset, reached.GetValueOrDefault(key), graph);
            }
        }

        var lines = new List<RingLine>();
        foreach (var flow in result.Flows)
        {
            if (places.TryGetValue(flow.Far, out var far) && NearEnd(result, flow, places) is { } near)
            {
                var width = ThinnestLine + (LineGrowth * Math.Min(1, result.ShareOf(flow.Amount)));
                lines.Add(new RingLine(flow.EdgeKey, result.Direction, near, far.At, width, LineInk(far)));
            }
        }

        return ([.. places.Values], lines);
    }

    /// <summary>
    /// Gives where a line starts: at the actor nearer the subject, or in the centre for the subject itself and for a
    /// transaction drawn as a node right after it (on no ring, since it adds no hop).
    /// </summary>
    /// <param name="result">The side's exposure.</param>
    /// <param name="flow">The line's money.</param>
    /// <param name="places">The actors placed.</param>
    /// <returns>The start, or <see langword="null"/> when the near end is not drawn.</returns>
    private static GraphPoint? NearEnd(ExposureResult result, ExposureFlow flow, Dictionary<string, RingPlace> places)
    {
        if (places.TryGetValue(flow.Near, out var near))
        {
            return near.At;
        }

        return result.Hops.GetValueOrDefault(flow.Near, int.MaxValue) == 0 ? Centre : null;
    }

    /// <summary>Places an actor on its ring.</summary>
    /// <param name="result">The side's exposure.</param>
    /// <param name="key">The actor.</param>
    /// <param name="hop">Its ring.</param>
    /// <param name="offset">Its angle from the side's middle, in degrees, downwards positive.</param>
    /// <param name="reach">The service's row, or <see langword="null"/> for an actor on the way.</param>
    /// <param name="graph">The graph's names and looks.</param>
    /// <returns>The place.</returns>
    private static RingPlace Place(
        ExposureResult result,
        string key,
        int hop,
        double offset,
        ExposureReach? reach,
        ExposureNames graph)
    {
        var degrees = result.Direction == ExposureDirection.Out ? offset : 180 - offset;
        var radians = degrees * Math.PI / 180;
        var at = new GraphPoint(
            Centre.X + (Rings[hop] * Math.Cos(radians)), Centre.Y + (Rings[hop] * Math.Sin(radians)));
        var look = graph.LookOf(key);
        var name = graph.NameOf(key);
        var sanctioned = graph.Actors.GetValueOrDefault(key)?.Badges.Contains(NodeBadge.Sanctioned) ?? false;
        if (reach is null)
        {
            return new RingPlace(key, result.Direction, at, ViaSize, hop, false, look, name, string.Empty, sanctioned);
        }

        var share = result.ShareOf(reach.Amount);
        var line = AmountText.Number(reach.Amount) + " · " + ExposureMap.Percent(share);
        var size = SmallestService + (ServiceGrowth * Math.Sqrt(share));
        return new RingPlace(key, result.Direction, at, size, hop, true, look, name, line, sanctioned);
    }

    /// <summary>
    /// Gives a line's colour: the accent for the money on its way, the service's own colour for the last step into it
    /// (sketch EP1b).
    /// </summary>
    /// <param name="far">The line's far end.</param>
    /// <returns>The ink.</returns>
    private static GraphInk LineInk(RingPlace far) =>
        far.IsService ? ExposureMap.InkOf(ExposureMap.GroupOf(far.Look)) : GraphInk.Accent;
}
