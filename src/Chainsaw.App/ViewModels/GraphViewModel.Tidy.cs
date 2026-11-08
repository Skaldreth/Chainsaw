using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// <em>Sortera grafen</em> (the user's wish 2026-11-08, built on the user's description without a sketch): the icon in
/// the graph's upper right sorts a messy free surface neatly and airily (<see cref="TidyLayout"/>), after a question
/// at the icon, so that a big piece of work is never lost to a stray click; the toast after it has <em>Ångra</em>,
/// which puts every actor back exactly where it stood. The places are ordinary data, as when nodes are dragged.
/// </summary>
/// <remarks>
/// It applies to the free surface only: the hop columns are sorted already, so the icon is not shown there
/// (principle 7). Ctrl+Z belongs to the trash and the suggestions, so the undoing is only the toast's button.
/// </remarks>
public sealed partial class GraphViewModel
{
    /// <summary>The icon's tip.</summary>
    public const string TidyToolTip = "Sortera grafen …";

    /// <summary>What the toast says after the graph was sorted.</summary>
    public const string TidiedText = "Grafen sorterad";

    /// <summary>The toast's action that puts every actor back.</summary>
    public const string TidyUndoText = "Ångra";

    /// <summary>What the toast says after the sorting was undone.</summary>
    public const string TidyUndoneText = "Aktörerna står där de stod före sorteringen.";

    /// <summary>What is said when the graph already stands as the sorting would put it.</summary>
    public const string AlreadyTidyText = "Grafen är redan sorterad.";

    /// <summary>What is said when the sorted places could not be stored.</summary>
    public const string TidySaveFailedText =
        "Platserna kunde inte sparas i ärendet; de gäller tills ärendet stängs.";

    /// <summary>The fewest actors that make sorting worth offering.</summary>
    public const int FewestToTidy = 2;

    /// <summary>The sorting the question waits to confirm, or <see langword="null"/>.</summary>
    private TidyPlan? _tidyPlan;

    /// <summary>What the toast's <em>Ångra</em> puts back, or <see langword="null"/> when nothing.</summary>
    private TidyUndo? _tidyUndo;

    /// <summary>Gets whether the icon is shown: a case, the free surface and at least two actors in the graph.</summary>
    public bool CanTidy => HasCase && IsFree && Scene is { } scene && scene.LayoutNodes.Count >= FewestToTidy;

    /// <summary>Gets whether the question at the icon is open.</summary>
    [ObservableProperty]
    public partial bool IsTidyConfirmOpen { get; private set; }

    /// <summary>Gets what the question says: how many actors are moved from where the user left them.</summary>
    [ObservableProperty]
    public partial string TidyConfirmText { get; private set; } = string.Empty;

    /// <summary>
    /// Opens the question at the icon, after working out off the UI thread where the actors would go and how many
    /// would move; says so instead when none would.
    /// </summary>
    /// <returns>A task that completes when the question is open, or it was said that none would move.</returns>
    [RelayCommand]
    public async Task AskTidyAsync()
    {
        if (!CanTidy || Scene is not { } scene)
        {
            return;
        }

        var plan = await PlanTidyAsync(scene);
        if (Scene != scene || !CanTidy)
        {
            return;
        }

        if (plan.Moving == 0)
        {
            Say(AlreadyTidyText);
            return;
        }

        _tidyPlan = plan;
        TidyConfirmText = QuestionText(plan.Moving);
        IsTidyConfirmOpen = true;
    }

    /// <summary>Closes the question and changes nothing (Esc).</summary>
    [RelayCommand]
    public void CancelTidy()
    {
        _tidyPlan = null;
        IsTidyConfirmOpen = false;
    }

    /// <summary>
    /// Sorts the graph as the question said (Enter): every actor to its new place at once, the comments tied to an
    /// actor along with it, all the places stored in one write, and a toast with <em>Ångra</em>.
    /// </summary>
    /// <returns>A task that completes when the places are stored or could not be.</returns>
    [RelayCommand]
    public async Task ConfirmTidyAsync()
    {
        var plan = _tidyPlan;
        CancelTidy();
        if (plan is null || !CanTidy || Scene is not { } scene || _session.Current is not { } openCase)
        {
            return;
        }

        if (plan.Scene != scene)
        {
            // The graph changed while the question was open: sort what is shown now.
            plan = await PlanTidyAsync(scene);
            if (Scene != scene || _session.Current != openCase)
            {
                return;
            }
        }

        var undo = new TidyUndo(openCase, PlacesOf(plan.Places.Keys), NotesTiedTo(plan.Places.Keys));
        var notes = FollowingNotes(plan.Places);
        _tidyUndo = undo;
        ToastWanted?.Invoke(this, new ToastRequest(
            ToastKind.Success, TidiedText, ActionText: TidyUndoText, Action: () => _ = UndoTidyAsync())
        {
            Lingers = true,
        });
        await PlaceAllAsync(openCase, plan.Places, notes);
    }

    /// <summary>
    /// Puts every actor and comment back exactly where it stood before the latest sorting (the toast's
    /// <em>Ångra</em>), stored in one write; nothing when the case changed meanwhile.
    /// </summary>
    /// <returns>A task that completes when the places are stored or could not be.</returns>
    public async Task UndoTidyAsync()
    {
        var undo = _tidyUndo;
        _tidyUndo = null;
        if (undo is null || _session.Current != undo.Case)
        {
            return;
        }

        Say(TidyUndoneText);
        await PlaceAllAsync(undo.Case, undo.Places, undo.Notes);
    }

    /// <summary>Connects the sorting to the graph: the icon follows the case, the layout and the graph.</summary>
    private void WireTidy()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Scene) or nameof(Layout) or nameof(HasCase))
            {
                OnPropertyChanged(nameof(CanTidy));
                if (!CanTidy)
                {
                    CancelTidy();
                }
            }
        };
        CaseChanged += (_, _) =>
        {
            CancelTidy();
            _tidyUndo = null;
        };
    }

    /// <summary>Works out, off the UI thread, where the actors would go and how many would move.</summary>
    /// <param name="scene">The graph shown.</param>
    /// <returns>The sorting.</returns>
    private async Task<TidyPlan> PlanTidyAsync(GraphScene scene)
    {
        var now = new Dictionary<string, GraphPoint>(Places, StringComparer.Ordinal);
        var links = scene.Edges.Select(edge => (edge.Edge.From, edge.Edge.To)).ToList();
        var places = await Task.Run(() => TidyLayout.Arrange(scene.LayoutNodes, links, scene.WidthOf));
        var moving = places.Count(pair => !now.TryGetValue(pair.Key, out var place) || place != pair.Value);
        return new TidyPlan(scene, places, moving);
    }

    /// <summary>
    /// Puts actors and comments at the given places on the free surface, draws again with the whole graph on screen
    /// and stores them: the actors' places in one write, each comment's as when it is dragged.
    /// </summary>
    /// <param name="openCase">The case.</param>
    /// <param name="places">The actors' places.</param>
    /// <param name="notes">The comments' places.</param>
    /// <returns>A task that completes when everything is stored or could not be.</returns>
    private async Task PlaceAllAsync(
        ICase openCase,
        IReadOnlyDictionary<string, GraphPoint> places,
        IReadOnlyDictionary<CommentNote, GraphPoint> notes)
    {
        foreach (var (key, place) in places)
        {
            _moved[(key, GraphLayout.Free)] = place;
        }

        foreach (var (note, place) in notes)
        {
            Comments.Move(note, place);
        }

        Arrange();
        FitWanted?.Invoke(this, EventArgs.Empty);

        try
        {
            await _source.SavePositionsAsync(openCase, GraphLayout.Free, places);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The case may have closed meanwhile; the places still hold in this session.
            Tell(ToastKind.Warning, TidySaveFailedText);
        }

        foreach (var note in notes.Keys)
        {
            await Comments.DropAsync(note);
        }
    }

    /// <summary>Gives where the given actors stand now on the free surface.</summary>
    /// <param name="keys">The actors.</param>
    /// <returns>Their places; an actor not on the surface is left out.</returns>
    private Dictionary<string, GraphPoint> PlacesOf(IEnumerable<string> keys) =>
        keys.Where(Places.ContainsKey).ToDictionary(key => key, key => Places[key], StringComparer.Ordinal);

    /// <summary>Gives where the comments tied to the given actors stand now; a loose comment is not among them.</summary>
    /// <param name="keys">The actors.</param>
    /// <returns>The comments' places.</returns>
    private Dictionary<CommentNote, GraphPoint> NotesTiedTo(IEnumerable<string> keys)
    {
        var actors = keys.ToHashSet(StringComparer.Ordinal);
        return Comments.Notes
            .Where(note => note.AnchorNode is { } anchor && actors.Contains(anchor))
            .ToDictionary(note => note, note => note.Place);
    }

    /// <summary>
    /// Gives where the comments tied to actors go when the actors move: each keeps its distance to its actor. A loose
    /// comment stays where it is.
    /// </summary>
    /// <param name="places">The actors' new places.</param>
    /// <returns>The comments' new places.</returns>
    private Dictionary<CommentNote, GraphPoint> FollowingNotes(IReadOnlyDictionary<string, GraphPoint> places)
    {
        var notes = new Dictionary<CommentNote, GraphPoint>();
        foreach (var (note, place) in NotesTiedTo(places.Keys))
        {
            var anchor = note.AnchorNode!;
            if (Places.TryGetValue(anchor, out var from))
            {
                var to = places[anchor];
                notes[note] = new GraphPoint(place.X + (to.X - from.X), place.Y + (to.Y - from.Y));
            }
        }

        return notes;
    }

    /// <summary>Gives the question: <c>Sortera om grafen? 12 aktörer flyttas från där du har lagt dem.</c></summary>
    /// <param name="moving">How many actors move.</param>
    /// <returns>The question.</returns>
    public static string QuestionText(int moving)
    {
        var actors = moving == 1 ? "aktör" : "aktörer";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Sortera om grafen? {moving} {actors} flyttas från där du har lagt dem.");
    }

    /// <summary>A sorting worked out for a graph, waiting for the question's answer.</summary>
    /// <param name="Scene">The graph it was worked out for.</param>
    /// <param name="Places">Where every actor goes.</param>
    /// <param name="Moving">How many actors move.</param>
    private sealed record TidyPlan(GraphScene Scene, IReadOnlyDictionary<string, GraphPoint> Places, int Moving);

    /// <summary>What the latest sorting moved, to put back.</summary>
    /// <param name="Case">The case it was made in.</param>
    /// <param name="Places">Where the actors stood before.</param>
    /// <param name="Notes">Where the comments tied to them stood before.</param>
    private sealed record TidyUndo(
        ICase Case,
        IReadOnlyDictionary<string, GraphPoint> Places,
        IReadOnlyDictionary<CommentNote, GraphPoint> Notes);
}
