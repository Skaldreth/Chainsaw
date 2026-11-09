namespace Chainsaw.App.Graph;

/// <summary>A part of a frame of the graph's surface whose time the performance HUD shows.</summary>
public enum FrameStage
{
    /// <summary>Everything the UI thread did for the frame: the frame loop and the surface's drawing.</summary>
    Total,

    /// <summary>What is under the pointer, its tooltip and the light on a card or a mark.</summary>
    Hover,

    /// <summary>The motions' steps.</summary>
    Motions,

    /// <summary>The lines' layer.</summary>
    Lines,

    /// <summary>The nodes' visuals, all of them or only those touched.</summary>
    Nodes,

    /// <summary>The layer over the nodes.</summary>
    OverNodes,

    /// <summary>What stands on the screen over the scene.</summary>
    Overlay,

    /// <summary>The comments' notes put in place.</summary>
    Comments,
}

/// <summary>How much a frame drew, from least to most; a frame counts as the most it did.</summary>
public enum FrameWork
{
    /// <summary>Nothing was drawn.</summary>
    None,

    /// <summary>The scene's drawing was moved and what stands over it drawn.</summary>
    View,

    /// <summary>Some of the scene's layers, or some nodes, were drawn again.</summary>
    Partial,

    /// <summary>The whole scene was drawn again.</summary>
    Full,
}

/// <summary>
/// What the graph's surface did in its frames over the last second, for the performance HUD (Debug, Ctrl+Shift+P):
/// frames per second, the time between frames, the time of each part of a frame, how much each frame drew and how
/// many nodes and lines it drew. Without WPF, so that it is tested on its own.
/// </summary>
/// <remarks>
/// A frame is everything done from one frame of the screen to the next, since WPF draws the surface after the frame
/// loop has run. A pause longer than <see cref="Pause"/> begins a new run, so that the numbers say how the surface
/// moves while it moves, not how long it rested.
/// </remarks>
public sealed class FrameStats
{
    /// <summary>How far back the numbers look.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    /// <summary>The longest time between two frames of the same run.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);

    /// <summary>The frames in the window, oldest first, the current last.</summary>
    private readonly Queue<Frame> _frames = new();

    /// <summary>The frame being gathered, or <see langword="null"/> before the first.</summary>
    private Frame? _current;

    /// <summary>
    /// Begins a new frame at a frame of the screen, ending the one before; a second call at the same time goes on with
    /// the same frame, since WPF can raise its frame more than once.
    /// </summary>
    /// <param name="renderingTime">The screen's time of the frame.</param>
    public void Begin(TimeSpan renderingTime)
    {
        if (_current is { } current && current.Time == renderingTime)
        {
            return;
        }

        if (_current is { } last && renderingTime - last.Time > Pause)
        {
            _frames.Clear();
        }

        _current = new Frame(renderingTime);
        _frames.Enqueue(_current);
        while (_frames.Peek().Time < renderingTime - Window)
        {
            _frames.Dequeue();
        }
    }

    /// <summary>Adds time to a part of the current frame; a part done twice in a frame adds up.</summary>
    /// <param name="stage">The part.</param>
    /// <param name="milliseconds">The time.</param>
    public void Add(FrameStage stage, double milliseconds)
    {
        if (_current is { } current)
        {
            current.Times[(int)stage] += milliseconds;
        }
    }

    /// <summary>Notes how much the current frame drew; the most it did counts.</summary>
    /// <param name="work">What it did.</param>
    public void Mark(FrameWork work)
    {
        if (_current is { } current && work > current.Work)
        {
            current.Work = work;
        }
    }

    /// <summary>Notes nodes and lines the current frame drew.</summary>
    /// <param name="nodes">The nodes drawn.</param>
    /// <param name="edges">The lines drawn.</param>
    public void Drew(int nodes, int edges)
    {
        if (_current is { } current)
        {
            current.Nodes += nodes;
            current.Edges += edges;
        }
    }

    /// <summary>Sums up the frames in the window.</summary>
    /// <returns>The numbers.</returns>
    public FrameSummary Summary()
    {
        var frames = _frames.ToArray();
        var span = frames.Length > 1 ? (frames[^1].Time - frames[0].Time).TotalSeconds : 0;
        var gaps = frames.Skip(1).Select((frame, at) => (frame.Time - frames[at].Time).TotalMilliseconds).ToArray();
        var stages = Enum.GetValues<FrameStage>()
            .Select(stage => new StageTime(
                stage,
                frames.Length == 0 ? 0 : frames.Average(frame => frame.Times[(int)stage]),
                frames.Length == 0 ? 0 : frames.Max(frame => frame.Times[(int)stage])))
            .ToArray();
        return new FrameSummary(
            span > 0 ? (frames.Length - 1) / span : 0,
            gaps.Length == 0 ? 0 : gaps.Average(),
            gaps.Length == 0 ? 0 : gaps.Max(),
            stages,
            Enum.GetValues<FrameWork>().ToDictionary(work => work, work => frames.Count(frame => frame.Work == work)),
            frames.Length == 0 ? 0 : frames.Max(frame => frame.Nodes),
            frames.Length == 0 ? 0 : frames.Max(frame => frame.Edges));
    }

    /// <summary>A frame as the window keeps it.</summary>
    /// <param name="Time">The screen's time of the frame.</param>
    private sealed record Frame(TimeSpan Time)
    {
        /// <summary>Gets the time of each part, in milliseconds, by <see cref="FrameStage"/>.</summary>
        public double[] Times { get; } = new double[Enum.GetValues<FrameStage>().Length];

        /// <summary>Gets or sets how much it drew.</summary>
        public FrameWork Work { get; set; }

        /// <summary>Gets or sets how many nodes it drew.</summary>
        public int Nodes { get; set; }

        /// <summary>Gets or sets how many lines it drew.</summary>
        public int Edges { get; set; }
    }
}

/// <summary>The time a part of the frames took over the window.</summary>
/// <param name="Stage">The part.</param>
/// <param name="Average">The average per frame, in milliseconds.</param>
/// <param name="Longest">The longest in a frame, in milliseconds.</param>
public sealed record StageTime(FrameStage Stage, double Average, double Longest);

/// <summary>The graph surface's frames over the last second, summed up.</summary>
/// <param name="FramesPerSecond">Frames per second.</param>
/// <param name="Interval">The average time between frames, in milliseconds.</param>
/// <param name="LongestInterval">The longest time between two frames, in milliseconds: a stutter shows here.</param>
/// <param name="Stages">The time of each part of a frame.</param>
/// <param name="Work">How many frames did how much.</param>
/// <param name="Nodes">The most nodes a frame drew.</param>
/// <param name="Edges">The most lines a frame drew.</param>
public sealed record FrameSummary(
    double FramesPerSecond,
    double Interval,
    double LongestInterval,
    IReadOnlyList<StageTime> Stages,
    IReadOnlyDictionary<FrameWork, int> Work,
    int Nodes,
    int Edges);
