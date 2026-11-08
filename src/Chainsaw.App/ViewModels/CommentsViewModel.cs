using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A note thrown in the trash (sketch SO1a), for the graph to read the trash again and say it with <em>Ångra</em>.
/// </summary>
/// <param name="Case">The case the note was thrown in.</param>
/// <param name="Throw">The throw.</param>
public sealed record CommentThrow(ICase Case, TrashThrow Throw);

/// <summary>
/// The graph's comments (sketch KN1a): notes on the surface, written in Markdown directly in the note, with a thin
/// dotted thread to the node they are about. A note is sealed in the case only when it is left with something written;
/// every later change of its text and its thread is sealed anew by Tracing, while a move or a new size is ordinary data
/// like the nodes' places. A note is never removed: Delete, a drag onto the bin or a note left empty throws it in the
/// trash (sketch SO1a), which only hides it, sealed, so that it can be put back with what it said. Without WPF, so that
/// it is tested.
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

    /// <summary>What the graph says when a comment could not be thrown in the trash.</summary>
    public const string ThrowFailedText = "Kommentaren kunde inte slängas: ärendet gick inte att skriva.";

    /// <summary>The prefix of a draft's key, which never looks like a comment's id.</summary>
    private const string DraftPrefix = "draft:";

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>Where the comments are read and written.</summary>
    private readonly IGraphComments _source;

    /// <summary>The trash a note is thrown in, or none where it is not wired and a note cannot leave the surface.</summary>
    private readonly ITrashSource? _trash;

    /// <summary>Counts the readings, so that an answer to an older one is dropped.</summary>
    private int _version;

    /// <summary>Counts the drafts, for their keys.</summary>
    private int _drafts;

    /// <summary>The graph shown, which the threads are tied into.</summary>
    private GraphScene? _scene;

    /// <summary>Creates the comments for the window's open case.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="source">Where the comments are read and written.</param>
    /// <param name="trash">The trash a note is thrown in; none where it is not wired.</param>
    public CommentsViewModel(CaseSession session, IGraphComments source, ITrashSource? trash = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        _session = session;
        _source = source;
        _trash = trash;
        session.Closing += (_, closing) => SealBeforeClose(closing);
    }

    /// <summary>
    /// Raised when a note was thrown in the trash, with the case and the throw, so that the graph reads the trash again
    /// and says it with <em>Ångra</em>.
    /// </summary>
    public event EventHandler<CommentThrow>? Thrown;

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
        foreach (var note in Notes)
        {
            note.IsEditing = false;
            note.IsSelected = false;
        }

        Notes.Clear();
        Selected = null;
        Editing = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ties a note's thread into the graph shown, and hides the note while its node lies in the trash (sketch SO1a), so
    /// that it never hangs loose and comes back with its node.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    /// <param name="anchor">The thread's stored key, or <see langword="null"/>.</param>
    private static void Tie(CommentNote note, GraphScene? scene, string? anchor)
    {
        note.AnchorNode = CommentGeometry.Resolve(scene, anchor);
        note.IsInTrash = IsThrown(note, scene)
            || (note.AnchorNode is null && CommentGeometry.IsThrown(scene, anchor));
    }

    /// <summary>Says whether a stored note lies in the trash itself.</summary>
    /// <param name="note">The note.</param>
    /// <param name="scene">The graph, whose trash is read, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool IsThrown(CommentNote note, GraphScene? scene) =>
        note.Id is { } id && scene?.Trash.Hides(TrashKeys.OfComment(id)) == true;

    /// <summary>Ties the threads into the graph shown: each note's node, found again after a cluster changed.</summary>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    public void Follow(GraphScene? scene)
    {
        _scene = scene;
        foreach (var note in Notes)
        {
            Tie(note, scene, note.Anchor);
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

    /// <summary>
    /// Starts writing in a note (a click on it); a note written in before is left and stored first. A note whose
    /// write is still under way opens with what was written last, not with what the case had before.
    /// </summary>
    /// <param name="note">The note.</param>
    public void Edit(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (Editing == note)
        {
            return;
        }

        Select(note);
        if (!note.IsStoring)
        {
            note.Writing = note.Text;
        }

        Editing = note;
        note.IsEditing = true;
    }

    /// <summary>
    /// Leaves a note being written in (Esc, a click elsewhere): a draft with something written is sealed, an empty one
    /// is dropped; a stored note's new text is sealed, the same text writes nothing, and an emptied note is removed,
    /// its text kept in its sealed history. The write waits for the note's writes before it, so they never race.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <returns>A task that completes when it is stored.</returns>
    public Task LeaveAsync(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (!note.IsEditing)
        {
            return Task.CompletedTask;
        }

        StopEditing(note);
        var written = CommentRules.Normalize(note.Writing);
        if (written.Length > CommentRules.MaximumTextLength)
        {
            Failed?.Invoke(this, TooLongText);
            KeepWriting(note, written);
            return Task.CompletedTask;
        }

        return Enqueue(note, () => CommitAsync(note, written));
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

    /// <summary>
    /// Stores where a note was dragged to and its size (ordinary data, no journal), after the note's writes before it;
    /// a draft's place goes with it when it is sealed.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <returns>A task that completes when it is stored.</returns>
    public Task DropAsync(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return Enqueue(note, () => SavePlaceAsync(note));
    }

    /// <summary>
    /// Throws a note in the trash (Delete, or dragged onto the bin): sealed, with what it said kept, and put back from
    /// the trash's view or with <em>Ångra</em>. A draft that was never stored is only dropped.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <returns>A task that completes when it is thrown.</returns>
    public Task ThrowAsync(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        StopEditing(note);
        return Enqueue(note, () => ThrowNowAsync(note));
    }

    /// <summary>Ties a note's thread to another node, or unties it (its end dragged to empty surface).</summary>
    /// <param name="note">The note.</param>
    /// <param name="node">The node, or <see langword="null"/>.</param>
    /// <returns>A task that completes when it is stored.</returns>
    public Task TieAsync(CommentNote note, GraphNode? node)
    {
        ArgumentNullException.ThrowIfNull(note);
        var anchor = node is null ? null : CommentGeometry.AnchorOf(node);
        if (anchor == note.Anchor)
        {
            return Task.CompletedTask;
        }

        if (note.IsDraft && !note.IsStoring)
        {
            note.Tie(anchor);
            note.AnchorNode = node?.Key;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        return Enqueue(note, () => note.Id is { } id
            ? StoreAsync(note, null, openCase => _source.AnchorAsync(openCase, id, anchor))
            : Task.CompletedTask);
    }

    /// <summary>Gives the note with a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The note, or <see langword="null"/>.</returns>
    public CommentNote? Find(string? key) => key is null ? null : Notes.FirstOrDefault(note => note.Key == key);

    /// <summary>
    /// Seals the note being written in before its case is let go (a lock, Ctrl+W, another case), while the case is
    /// still open, so that nothing written is lost. It waits for the write here, on the UI thread: the module's write
    /// runs on the thread pool and never comes back to this thread, so the wait cannot deadlock.
    /// </summary>
    /// <param name="closing">The case being let go.</param>
    private void SealBeforeClose(ICase closing)
    {
        if (Editing is not { } note)
        {
            return;
        }

        StopEditing(note);
        var written = CommentRules.Normalize(note.Writing);
        if (written.Length > CommentRules.MaximumTextLength)
        {
            Failed?.Invoke(this, TooLongText);
            return;
        }

        try
        {
            WriteBeforeClose(closing, note, written);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, SaveFailedText);
        }
    }

    /// <summary>Writes what a note says now, waiting for it, while its case closes.</summary>
    /// <param name="closing">The case.</param>
    /// <param name="note">The note.</param>
    /// <param name="written">What it says.</param>
    private void WriteBeforeClose(ICase closing, CommentNote note, string written)
    {
        if (note.Comment is not { } stored)
        {
            if (written.Length > 0)
            {
                var draft = new GraphCommentDraft(written, note.Anchor, new NodePosition(note.Place.X, note.Place.Y));
                _source.AddAsync(closing, draft).GetAwaiter().GetResult();
            }

            return;
        }

        if (written == stored.Text)
        {
            return;
        }

        if (written.Length > 0)
        {
            _source.EditAsync(closing, stored.Id, written).GetAwaiter().GetResult();
        }
        else if (_trash is not null)
        {
            // An emptied note is thrown, so that what it said can be put back; the throw runs on the thread pool.
            _trash.ThrowAsync(closing, [TrashKeys.Comment(stored.Id, stored.Text)]).GetAwaiter().GetResult();
        }
    }

    /// <summary>Stores what a left note says: seals a draft, changes or removes a stored note, or nothing.</summary>
    /// <param name="note">The note.</param>
    /// <param name="written">What it said when it was left.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private Task CommitAsync(CommentNote note, string written)
    {
        if (note.Comment is not { } stored)
        {
            return StoreDraftAsync(note, written);
        }

        if (written == stored.Text)
        {
            return Task.CompletedTask;
        }

        return written.Length == 0
            ? ThrowNowAsync(note)
            : StoreAsync(note, written, openCase => _source.EditAsync(openCase, stored.Id, written));
    }

    /// <summary>
    /// Seals a draft that was left with something written, or drops an empty one. The draft becomes the stored note
    /// as soon as it is sealed, so that a later failure (of its size) never seals it twice.
    /// </summary>
    /// <param name="note">The draft.</param>
    /// <param name="written">What it says.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task StoreDraftAsync(CommentNote note, string written)
    {
        if (_session.Current is not { } openCase)
        {
            return;
        }

        if (written.Length == 0)
        {
            if (!note.IsEditing)
            {
                Forget(note);
            }

            return;
        }

        var draft = new GraphCommentDraft(written, note.Anchor, new NodePosition(note.Place.X, note.Place.Y));
        GraphComment comment;
        try
        {
            comment = await _source.AddAsync(openCase, draft);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, SaveFailedText);
            KeepWriting(note, written);
            return;
        }

        if (_session.Current != openCase)
        {
            return;
        }

        note.ShowText(comment);
        Changed?.Invoke(this, EventArgs.Empty);
        if (note.Size is not null || note.Place != new GraphPoint(comment.Place.X, comment.Place.Y))
        {
            await SavePlaceAsync(note);
        }
    }

    /// <summary>Stores where a stored note stands and its size; a failure is said and loses nothing else.</summary>
    /// <param name="note">The note.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task SavePlaceAsync(CommentNote note)
    {
        if (note.Id is not { } id || _session.Current is not { } openCase)
        {
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

    /// <summary>
    /// Throws a stored note in the trash and hides it, or drops a draft that was never stored. What it showed while it
    /// was written goes back to what is stored, which is what comes back when it is put back. A note already in the
    /// trash (thrown from another window) is only hidden.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <returns>A task that completes when it is thrown.</returns>
    private async Task ThrowNowAsync(CommentNote note)
    {
        if (note.Comment is not { } stored || _session.Current is not { } openCase)
        {
            Forget(note);
            return;
        }

        if (_trash is null)
        {
            Failed?.Invoke(this, ThrowFailedText);
            return;
        }

        TrashThrow thrown;
        try
        {
            thrown = await _trash.ThrowAsync(openCase, [TrashKeys.Comment(stored.Id, stored.Text)]);
        }
        catch (InvalidOperationException)
        {
            if (_session.Current == openCase)
            {
                Hide(note);
            }

            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, ThrowFailedText);
            return;
        }

        if (_session.Current != openCase)
        {
            return;
        }

        Hide(note);
        Thrown?.Invoke(this, new CommentThrow(openCase, thrown));
    }

    /// <summary>
    /// Hides a thrown note at once, before the trash is read again, and lets go of it; it shows what is stored again.
    /// </summary>
    /// <param name="note">The note.</param>
    private void Hide(CommentNote note)
    {
        note.Writing = note.Text;
        note.IsInTrash = true;
        if (Selected == note)
        {
            Select(null);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stores a change of a stored note and shows what the case says about its text and thread then.</summary>
    /// <param name="note">The note.</param>
    /// <param name="written">The text written, given back to the note if the store fails; none for a thread.</param>
    /// <param name="store">The change.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task StoreAsync(CommentNote note, string? written, Func<ICase, Task<GraphComment?>> store)
    {
        if (_session.Current is not { } openCase)
        {
            return;
        }

        GraphComment? comment;
        try
        {
            comment = await store(openCase);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed?.Invoke(this, SaveFailedText);
            if (written is not null)
            {
                KeepWriting(note, written);
            }

            return;
        }

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

        note.ShowText(comment);
        Tie(note, _scene, comment.Anchor);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Queues a write of a note after its writes before it, counted while it is queued or runs.</summary>
    /// <param name="note">The note.</param>
    /// <param name="write">The write.</param>
    /// <returns>A task that completes when the write is done.</returns>
    private static Task Enqueue(CommentNote note, Func<Task> write)
    {
        note.Storing++;
        var next = RunAfterAsync(note, note.Pending, write);
        note.Pending = next;
        return next;
    }

    /// <summary>Runs a write when the one before it is done; each write says its own failures.</summary>
    /// <param name="note">The note.</param>
    /// <param name="before">The write before it.</param>
    /// <param name="write">The write.</param>
    /// <returns>A task that completes when the write is done.</returns>
    private static async Task RunAfterAsync(CommentNote note, Task before, Func<Task> write)
    {
        try
        {
            await before;
            await write();
        }
        finally
        {
            note.Storing--;
        }
    }

    /// <summary>Ends writing in a note, without storing anything.</summary>
    /// <param name="note">The note.</param>
    private void StopEditing(CommentNote note)
    {
        note.IsEditing = false;
        if (Editing == note)
        {
            Editing = null;
        }
    }

    /// <summary>
    /// Puts a note back into writing, with what was written, after it could not be stored, so that nothing written is
    /// lost; a note written in again meanwhile keeps what is in it now.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <param name="written">What was written.</param>
    private void KeepWriting(CommentNote note, string written)
    {
        if (!Notes.Contains(note) || note.IsEditing)
        {
            return;
        }

        Edit(note);
        note.Writing = written;
    }

    /// <summary>Puts the stored comments on the surface, keeping a draft being written or stored.</summary>
    /// <param name="comments">The comments.</param>
    private void Take(IReadOnlyList<GraphComment> comments)
    {
        var drafts = Notes.Where(note => note.IsDraft).ToList();
        var known = Notes.Where(note => note.Id is not null)
            .ToDictionary(note => note.Id!, StringComparer.Ordinal);
        Notes.Clear();
        foreach (var comment in comments)
        {
            var note = known.GetValueOrDefault(comment.Id) ?? new CommentNote(comment);
            if (!note.IsEditing && !note.IsStoring)
            {
                note.Show(comment);
            }

            Tie(note, _scene, comment.Anchor);
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

    /// <summary>Drops a draft that never had text and is not being stored, when another note is begun.</summary>
    private void DropDraft()
    {
        var untouched = Notes
            .Where(note => note.IsDraft && !note.IsEditing && !note.IsStoring && note.Writing.Length == 0)
            .ToList();
        foreach (var draft in untouched)
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
