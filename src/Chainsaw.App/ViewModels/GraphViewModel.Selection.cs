using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Picking several actors, as on the Windows desktop (the user's wish 2026-11-08, built on the user's description of
/// the standard pattern, without a sketch): a box drawn with the left button on the empty surface picks every actor it
/// touches, into the same pick as Ctrl+click (<see cref="ChecksViewModel.Picked"/>), so that Delete throws them all and
/// Z draws an own area around them; a picked actor that is dragged takes the others with it, and their places are
/// stored in one write.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The box being drawn, or <see langword="null"/> while none is.</summary>
    private BoxSelection? _box;

    /// <summary>Where each actor of a dragged group stood when the drag began; null while none is dragged.</summary>
    private Dictionary<string, GraphPoint>? _group;

    /// <summary>Where the comments tied to a dragged group stood when the drag began.</summary>
    private Dictionary<CommentNote, GraphPoint> _groupNotes = [];

    /// <summary>Gets whether a box is being drawn on the surface.</summary>
    public bool IsBoxing => _box is not null;

    /// <summary>
    /// Begins a box on the empty surface: without a key it replaces the pick, so the pick is emptied at once, as
    /// Explorer does.
    /// </summary>
    /// <param name="mode">How the box changes the pick.</param>
    public void BeginBox(BoxMode mode)
    {
        _box = new BoxSelection(mode, Checks.Picked);
        Checks.SetPicked(_box.Picked);
    }

    /// <summary>Picks with the box as it stands now; the pick changes only when another actor is touched.</summary>
    /// <param name="box">The box on the surface.</param>
    public void BoxTo(GraphRect box)
    {
        if (_box is { } drawn && Scene is { } scene && drawn.Update(box, scene.Nodes, Places))
        {
            Checks.SetPicked(drawn.Picked);
        }
    }

    /// <summary>Lets go of the box: the pick stays as it picked.</summary>
    public void EndBox() => _box = null;

    /// <summary>Cancels the box (Esc): the pick is given back as it was when the box began.</summary>
    public void CancelBox()
    {
        if (_box is { } drawn)
        {
            _box = null;
            Checks.SetPicked(drawn.Before);
        }
    }

    /// <summary>Says whether a dragged actor takes the other picked actors along: it is picked, not alone.</summary>
    /// <param name="nodeKey">The dragged actor.</param>
    /// <returns><see langword="true"/> when the whole pick moves.</returns>
    private bool MovesGroup(string nodeKey) => Checks.Picked.Count > 1 && Checks.IsPicked(nodeKey);

    /// <summary>
    /// Moves every picked actor by as much as the dragged one moved, each kept to its column in the hop columns; on the
    /// free surface the comments tied to them follow, as when the graph is sorted.
    /// </summary>
    /// <param name="nodeKey">The dragged actor.</param>
    /// <param name="start">Where it stood when the drag began.</param>
    /// <param name="at">Where it is dragged to.</param>
    private void MoveGroup(string nodeKey, GraphPoint start, GraphPoint at)
    {
        if (_group is null)
        {
            _group = PlacesOf(Checks.Picked);
            _group[nodeKey] = start;
            _groupNotes = Layout == GraphLayout.Free ? NotesTiedTo(_group.Keys) : [];
        }

        var (dx, dy) = (at.X - start.X, at.Y - start.Y);
        foreach (var (key, from) in _group)
        {
            _moved[(key, Layout)] = GraphPlacement.Drag(Layout, from, new GraphPoint(from.X + dx, from.Y + dy));
        }

        foreach (var (note, from) in _groupNotes)
        {
            Comments.Move(note, new GraphPoint(from.X + dx, from.Y + dy));
        }
    }

    /// <summary>Takes the group dragged now, and forgets it.</summary>
    /// <returns>Where its actors and comments stood when the drag began; null when no group was dragged.</returns>
    private (Dictionary<string, GraphPoint> Actors, Dictionary<CommentNote, GraphPoint> Notes)? TakeGroup()
    {
        if (_group is not { } actors)
        {
            return null;
        }

        var notes = _groupNotes;
        _group = null;
        _groupNotes = [];
        return (actors, notes);
    }

    /// <summary>
    /// Stores where a dragged group was left: the actors' places in one write and each comment's as when it is dragged.
    /// </summary>
    /// <param name="actors">The group's actors.</param>
    /// <param name="notes">The comments that followed them.</param>
    /// <returns>A task that completes when everything is stored or could not be.</returns>
    private async Task DropGroupAsync(IReadOnlyCollection<string> actors, IEnumerable<CommentNote> notes)
    {
        if (_session.Current is not { } openCase)
        {
            return;
        }

        var layout = Layout;
        var places = actors
            .Where(key => _moved.ContainsKey((key, layout)))
            .ToDictionary(key => key, key => _moved[(key, layout)], StringComparer.Ordinal);
        try
        {
            await _source.SavePositionsAsync(openCase, layout, places);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The case may have closed meanwhile; the places still hold in this session.
            Tell(ToastKind.Warning, TidySaveFailedText);
        }

        foreach (var note in notes)
        {
            await Comments.DropAsync(note);
        }
    }

    /// <summary>Puts a group dropped on the bin back where it stood, comments too, and asks to throw it.</summary>
    /// <param name="actors">Where each of its actors stood when the drag began.</param>
    /// <param name="notes">Where its comments stood.</param>
    private void ThrowGroup(Dictionary<string, GraphPoint> actors, Dictionary<CommentNote, GraphPoint> notes)
    {
        foreach (var (key, place) in actors)
        {
            _moved[(key, Layout)] = place;
        }

        foreach (var (note, place) in notes)
        {
            Comments.Move(note, place);
        }

        Arrange();
        AskThrow([.. Checks.Picked.Where(actors.ContainsKey)]);
    }
}
