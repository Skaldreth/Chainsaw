using Chainsaw.App.Motion;

namespace Chainsaw.App.Graph;

/// <summary>
/// The graph surface's one frame loop: the changes asked for and the motions that run are gathered here, and each
/// frame of the screen steps the motions that are due and gives back what changed in all, once (principle 6).
/// Nothing on the surface asks WPF for a frame or hooks the screen's frames by itself; it asks the pump.
/// </summary>
/// <remarks>
/// Each motion has its own clock, so that it moves by the time since its own last step, and may have a least time
/// between steps (the rings move 15 times a second, which is enough for them). Without WPF, so that the rules are
/// tested on their own; the surface hooks <see cref="Tick"/> to the screen's frames while <see cref="IsIdle"/> is
/// false.
/// </remarks>
public sealed class FramePump
{
    /// <summary>How much earlier than its least time a motion may step, so that a frame a little early is not lost.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(4);

    /// <summary>The motions known to the pump, by what they are.</summary>
    private readonly Dictionary<GraphMotion, Entry> _motions = [];

    /// <summary>The changes asked for since the last frame.</summary>
    private GraphChange _asked;

    /// <summary>Gets whether nothing runs and nothing is asked for, so that the screen's frames can be let go.</summary>
    public bool IsIdle => _asked == GraphChange.None && !_motions.Values.Any(entry => entry.IsRunning);

    /// <summary>Makes a motion known: what it does in a step, and the least time between its steps.</summary>
    /// <param name="motion">The motion.</param>
    /// <param name="step">
    /// Moves it by the seconds since its last step and gives what that changed; it stops itself with
    /// <see cref="Stop"/> when it is over.
    /// </param>
    /// <param name="interval">The least time between steps; zero for every frame.</param>
    public void Add(GraphMotion motion, Func<double, GraphChange> step, TimeSpan interval = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!_motions.TryAdd(motion, new Entry(step, interval)))
        {
            throw new InvalidOperationException($"The motion {motion} is already known.");
        }
    }

    /// <summary>Starts a motion from the next frame; a motion that runs goes on as it was.</summary>
    /// <param name="motion">The motion.</param>
    public void Start(GraphMotion motion)
    {
        var entry = EntryOf(motion);
        if (!entry.IsRunning)
        {
            entry.Clock.Reset();
            entry.LastStep = null;
            entry.IsRunning = true;
        }
    }

    /// <summary>Stops a motion where it is.</summary>
    /// <param name="motion">The motion.</param>
    public void Stop(GraphMotion motion) => EntryOf(motion).IsRunning = false;

    /// <summary>Says whether a motion runs.</summary>
    /// <param name="motion">The motion.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool IsRunning(GraphMotion motion) => EntryOf(motion).IsRunning;

    /// <summary>Asks for a change to be drawn in the next frame, together with everything else asked for.</summary>
    /// <param name="change">The change.</param>
    public void Request(GraphChange change) => _asked |= change;

    /// <summary>
    /// Steps the motions that run and are due, in their order, and gives what changed in this frame: theirs and
    /// what was asked for; what was asked for is then forgotten.
    /// </summary>
    /// <param name="renderingTime">The screen's time of the frame.</param>
    /// <returns>What changed.</returns>
    public GraphChange Tick(TimeSpan renderingTime)
    {
        foreach (var motion in Enum.GetValues<GraphMotion>())
        {
            if (_motions.TryGetValue(motion, out var entry) && entry.IsRunning && entry.IsDue(renderingTime)
                && entry.Clock.Tick(renderingTime) is { } seconds)
            {
                entry.LastStep = renderingTime;
                _asked |= entry.Step(seconds);
            }
        }

        var changed = _asked;
        _asked = GraphChange.None;
        return changed;
    }

    /// <summary>Gives a known motion.</summary>
    /// <param name="motion">The motion.</param>
    /// <returns>Its entry.</returns>
    private Entry EntryOf(GraphMotion motion) => _motions.TryGetValue(motion, out var entry)
        ? entry
        : throw new InvalidOperationException($"The motion {motion} is not known.");

    /// <summary>A motion as the pump keeps it.</summary>
    /// <param name="Step">What it does in a step.</param>
    /// <param name="Interval">The least time between its steps.</param>
    private sealed record Entry(Func<double, GraphChange> Step, TimeSpan Interval)
    {
        /// <summary>Gets the time between its steps.</summary>
        public FrameClock Clock { get; } = new();

        /// <summary>Gets or sets whether it runs.</summary>
        public bool IsRunning { get; set; }

        /// <summary>Gets or sets the frame of its last step, or <see langword="null"/> before the first.</summary>
        public TimeSpan? LastStep { get; set; }

        /// <summary>Says whether its least time since the last step has passed.</summary>
        /// <param name="now">The frame's time.</param>
        /// <returns><see langword="true"/> when it may step.</returns>
        public bool IsDue(TimeSpan now) => LastStep is not { } last || now - last >= Interval - Slack;
    }
}
