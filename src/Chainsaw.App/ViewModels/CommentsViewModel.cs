using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's comments (sketch KN1a): notes on the surface, written in Markdown directly in the note, with a thin
/// dotted thread to the node they are about. A note is sealed in the case only when it is left with something written;
/// every later change of its text, its thread and its removal is sealed anew by Tracing, while a move or a new size is
/// ordinary data like the nodes' places. Without WPF, so that it is tested.
/// </summary>
public sealed partial class CommentsViewModel : ObservableObject
{
    /// <summary>What the graph says when a comment could not be stored.</summary>
    public const string SaveFailedText = "Kommentaren kunde inte sparas i ärendet.";

    /// <summary>What the graph says when the comments could not be read.</summary>
    public const string LoadFailedText = "Kommentarerna kunde inte läsas ur ärendet.";

    /// <summary>What the graph says when a comment's place could not be stored.</summary>
    public const string PlaceFailedText = "Kommentarens plats kunde inte sparas; den gäller tills ärendet stängs.";

    /// <summary>What the graph says when a comment is too long to store.</summary>
    public const string TooLongText = "Kommentaren är för lång för att sparas.";

    /// <summary>The prefix of a draft's key, which never looks like a comment's id.</summary>
    private const string DraftPrefix = "draft:";

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>Where the comments are read and written.</summary>
    private readonly IGraphComments _source;

    /// <summary>Counts the readings, so that an answer to an older one is dropped.</summary>
    private int _version;

    /// <summary>Counts the drafts, for their keys.</summary>
    private int _drafts;

    /// <summary>The graph shown, which the threads are tied into.</summary>
    private GraphScene? _scene;

    /// <summary>Creates the comments for the window's open case.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="source">Where the comments are read and written.</param>
    public CommentsViewModel(CaseSession session, IGraphComments source)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        _session = session;
        _source = source;
    }

    /// <summary>Raised when the notes change in a way that is drawn (a place, a thread, a note more or less).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with a Swedish text when something could not be read or stored, for the toaster.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Gets the notes on the surface, the stored ones oldest first and a draft last.</summary>
    public ObservableCollection<CommentNote> Notes { get; } = [];

    /// <summary>Gets the chosen note, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial CommentNote? Selected { get; private set; }

    /// <summary>Gets the note being written in, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    public partial CommentNote? Editing { get; private set; }

    /// <summary>Gets whether a note is being written in, so that the graph's keys leave the keyboard to it.</summary>
    public bool IsEditing => Editing is not null;

    /// <summary>Reads the open case's comments anew; with no case open, the surface has none.</summary>
    /// <returns>A task that completes when they are read.</returns>
    public async Task LoadAsync()
    {
        var version = ++_version;
        if (_session.Current is not { } openCase)
        {
            Clear();
            return;
        }

        try
        {
            var comments = await _source.GetAsync(openCase, CancellationToken.None);
            if (version == _version && _session.Current == openCase)
            {
                Take(comments);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == _version)
            {
                Failed?.Invoke(this, LoadFailedText);
            }
        }
    }

    /// <summary>Forgets the notes, when the case closes or another opens.</summary>
    public void Clear()
    {
        _version++;
        Notes.Clear();
        Selected = null;
        Editing = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ties the threads into the graph shown: each note's node, found again after a cluster changed.</summary>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    public void Follow(GraphScene? scene)
    {
        _scene = scene;
        foreach (var note in Notes)
        {
            note.AnchorNode = CommentGeometry.Resolve(scene, note.Anchor);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts a new note and writes in it (N, a double click on the surface or the icon over a node). It is a draft
    /// until it is left with something written.
    /// </summary>
    /// <param name="place">Its top left corner.</param>
    /// <param name="node">The node it is about, or <see langword="null"/>.</param>
    /// <returns>The draft, or <see langword="null"/> with no case open.</returns>
    public CommentNote? Begin(GraphPoint place, GraphNode? node)
    {
        if (_session.Current is null)
        {
            return null;
        }

        if (Editing is { } writing)
        {
            _ = LeaveAsync(writing);
        }

        DropDraft();
        var key = DraftPrefix + (++_drafts).ToString(CultureInfo.InvariantCulture);
        var anchor = node is null ? null : CommentGeometry.AnchorOf(node);
        var draft = new CommentNote(key, place, anchor) { AnchorNode = node?.Key };
        Notes.Add(draft);
        Select(draft);
        Edit(draft);
        Changed?.Invoke(this, EventArgs.Empty);
        return draft;
    }

    /// <summary>Chooses a note, or none; a note being written in is left first, and what it says is stored.</summary>
    /// <param name="note">The note, or <see langword="null"/>.</param>
    public void Select(CommentNote? note)
    {
        if (Editing is { } writing && writing != note)
        {
            _ = LeaveAsync(writing);
        }

        if (Selected == note)
        {
            return;
        }

        if (Selected is { } former)
        {
            former.IsSelected = false;
        }

        Selected = note;
        if (note is not null)
        {
            note.IsSelected = true;
        }
    }

    /// <summary>Starts writing in a note (a click on it); a note written in before is left and stored first.</summary>
    /// <param name="note">The note.</param>
    public void Edit(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (Editing == note)
        {
            return;
        }

        Select(note);
        note.Writing = note.Text;
        Editing = note;
        note.IsEditing = true;
    }

    /// <summary>
    /// Leaves a note being written in (Esc, a click elsewhere): a draft with something written is sealed, an empty one
    /// is dropped; a stored note's new text is sealed, the same text writes nothing, and an emptied note is removed,
    /// its text kept in its sealed history.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <returns>A task that completes when it is stored.</returns>
    public async Task LeaveAsync(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (!note.IsEditing)
        {
            return;
        }

        note.IsEditing = false;
        if (Editing == note)
        {
            Editing = null;
        }

        var written = CommentRules.Normalize(note.Writing);
        if (note.Comment is not { } stored)
        {
            await StoreDraftAsync(note, written);
            return;
        }

        if (written == stored.Text)
        {
            return;
        }

        if (written.Length == 0)
        {
            await RemoveAsync(note);
            return;
        }

        await StoreAsync(note, openCase => _source.EditAsync(openCase, stored.Id, written));
    }

    /// <summary>Moves a note while it is dragged; nothing is stored until <see cref="DropAsync"/>.</summary>
    /// <param name="note">The note.</param>
    /// <param name="place">Its new top left corner.</param>
    public void Move(CommentNote note, GraphPoint place)
    {
        ArgumentNullException.ThrowIfNull(note);
        note.Place = place;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sizes a note while its grip is dragged, within the sketch's bounds.</summary>
    /// <param name="note">The note.</param>
    /// <param name="width">The wanted width.</param>
    /// <param name="height">The wanted height.</param>
    public void Resize(CommentNote note, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(note);
        note.Size = CommentRules.Clamp(width, height);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stores where a note was dragged to and its size (ordinary data, no journal).</summary>
    /// <param name="note">The note.</param>
    /// <returns>A task that completes when it is stored.</returns>
    public async Task DropAsync(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.Id is not { } id || _session.Current is not { } openCase)
        {
            // A draft's place goes with it when it is sealed.
            return;
        }

        try
        {
            await _source.SavePlaceAsync(openCase, id, new NodePosition(note.Place.X, note.Place.Y), note.Size);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, PlaceFailedText);
        }
    }

    /// <summary>Removes a note (Delete): sealed as removed, its text kept in its history; a draft is dropped.</summary>
    /// <param name="note">The note.</param>
    /// <returns>A task that completes when it is stored.</returns>
    public async Task RemoveAsync(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.Id is not { } id || _session.Current is not { } openCase)
        {
            Forget(note);
            return;
        }

        try
        {
            await _source.RemoveAsync(openCase, id);
            if (_session.Current == openCase)
            {
                Forget(note);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, SaveFailedText);
        }
    }

    /// <summary>Ties a note's thread to another node, or unties it (its end dragged to empty surface).</summary>
    /// <param name="note">The note.</param>
    /// <param name="node">The node, or <see langword="null"/>.</param>
    /// <returns>A task that completes when it is stored.</returns>
    public async Task TieAsync(CommentNote note, GraphNode? node)
    {
        ArgumentNullException.ThrowIfNull(note);
        var anchor = node is null ? null : CommentGeometry.AnchorOf(node);
        if (anchor == note.Anchor)
        {
            return;
        }

        if (note.Id is not { } id)
        {
            note.Tie(anchor);
            note.AnchorNode = node?.Key;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        await StoreAsync(note, openCase => _source.AnchorAsync(openCase, id, anchor));
    }

    /// <summary>Gives the note with a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The note, or <see langword="null"/>.</returns>
    public CommentNote? Find(string? key) => key is null ? null : Notes.FirstOrDefault(note => note.Key == key);

    /// <summary>Seals a draft that was left with something written, or drops an empty one.</summary>
    /// <param name="note">The draft.</param>
    /// <param name="written">What it says.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task StoreDraftAsync(CommentNote note, string written)
    {
        if (written.Length == 0 || _session.Current is not { } openCase)
        {
            Forget(note);
            return;
        }

        var draft = new GraphCommentDraft(written, note.Anchor, new NodePosition(note.Place.X, note.Place.Y));
        try
        {
            var comment = await _source.AddAsync(openCase, draft);
            if (_session.Current != openCase)
            {
                return;
            }

            var index = Notes.IndexOf(note);
            var stored = new CommentNote(comment)
            {
                AnchorNode = note.AnchorNode,
            };
            if (note.Size is { } size)
            {
                stored.Size = size;
                await _source.SavePlaceAsync(openCase, comment.Id, comment.Place, size);
            }

            Replace(index, note, stored);
        }
        catch (ArgumentException)
        {
            Failed?.Invoke(this, TooLongText);
            KeepWriting(note);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, SaveFailedText);
            KeepWriting(note);
        }
    }

    /// <summary>Stores a change of a stored note and shows what the case says about it then.</summary>
    /// <param name="note">The note.</param>
    /// <param name="store">The change.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task StoreAsync(CommentNote note, Func<ICase, Task<GraphComment?>> store)
    {
        if (_session.Current is not { } openCase)
        {
            return;
        }

        try
        {
            var comment = await store(openCase);
            if (_session.Current != openCase)
            {
                return;
            }

            if (comment is null)
            {
                // Removed meanwhile, for example in another window.
                Forget(note);
                return;
            }

            note.Show(comment);
            note.AnchorNode = CommentGeometry.Resolve(_scene, comment.Anchor);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException)
        {
            Failed?.Invoke(this, TooLongText);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, SaveFailedText);
        }
    }

    /// <summary>Puts a note back into writing after it could not be stored, so that nothing written is lost.</summary>
    /// <param name="note">The note.</param>
    private void KeepWriting(CommentNote note)
    {
        if (Notes.Contains(note))
        {
            var writing = note.Writing;
            Edit(note);
            note.Writing = writing;
        }
    }

    /// <summary>Puts the stored comments on the surface, keeping a draft being written.</summary>
    /// <param name="comments">The comments.</param>
    private void Take(IReadOnlyList<GraphComment> comments)
    {
        var drafts = Notes.Where(note => note.IsDraft).ToList();
        var known = Notes.Where(note => !note.IsDraft).ToDictionary(note => note.Key, StringComparer.Ordinal);
        Notes.Clear();
        foreach (var comment in comments)
        {
            var note = known.GetValueOrDefault(comment.Id) ?? new CommentNote(comment);
            if (!note.IsEditing)
            {
                note.Show(comment);
            }

            note.AnchorNode = CommentGeometry.Resolve(_scene, comment.Anchor);
            Notes.Add(note);
        }

        foreach (var draft in drafts)
        {
            Notes.Add(draft);
        }

        if (Selected is { } chosen && !Notes.Contains(chosen))
        {
            Select(null);
        }

        if (Editing is { } editing && !Notes.Contains(editing))
        {
            Editing = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts a stored note in its draft's place.</summary>
    /// <param name="index">Where the draft stood.</param>
    /// <param name="draft">The draft.</param>
    /// <param name="stored">The stored note.</param>
    private void Replace(int index, CommentNote draft, CommentNote stored)
    {
        var wasSelected = Selected == draft;
        Forget(draft);
        Notes.Insert(index < 0 ? Notes.Count : Math.Min(index, Notes.Count), stored);
        if (wasSelected)
        {
            Select(stored);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops a draft that was never written in, when another note is begun.</summary>
    private void DropDraft()
    {
        foreach (var draft in Notes.Where(note => note.IsDraft && note.Text.Length == 0 && !note.IsEditing).ToList())
        {
            Forget(draft);
        }
    }

    /// <summary>Takes a note off the surface.</summary>
    /// <param name="note">The note.</param>
    private void Forget(CommentNote note)
    {
        Notes.Remove(note);
        if (Selected == note)
        {
            Select(null);
        }

        if (Editing == note)
        {
            Editing = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
