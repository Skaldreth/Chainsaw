using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The graph's comments on the surface (sketch KN1a): one <see cref="CommentNoteView"/> per note, at the note's place
/// in the surface's units. The graph's canvas hosts it and gives it the viewport as its transform, so that the notes
/// pan and zoom with the graph, and the presses, keys and wheel that the notes leave go on to the canvas.
/// </summary>
internal sealed class CommentLayer : Canvas
{
    /// <summary>The views by the notes' keys.</summary>
    private readonly Dictionary<string, CommentNoteView> _views = new(StringComparer.Ordinal);

    /// <summary>The surface the notes stand on, which takes the keyboard back when a note is left.</summary>
    private readonly UIElement _surface;

    /// <summary>The comments shown, once attached.</summary>
    private CommentsViewModel? _comments;

    /// <summary>What a click on a <c>chainsaw:</c> link in a note runs.</summary>
    private ICommand? _linkCommand;

    /// <summary>Creates the layer over a surface.</summary>
    /// <param name="surface">The surface.</param>
    public CommentLayer(UIElement surface)
    {
        _surface = surface;
        ClipToBounds = false;
    }

    /// <summary>Raised when a note's size changes, so that its thread is drawn again.</summary>
    public event EventHandler? NoteResized;

    /// <summary>Shows the comments, and follows them.</summary>
    /// <param name="comments">The comments.</param>
    /// <param name="linkCommand">What a click on a <c>chainsaw:</c> link in a note runs.</param>
    public void Attach(CommentsViewModel comments, ICommand linkCommand)
    {
        _comments = comments;
        _linkCommand = linkCommand;
        comments.Notes.CollectionChanged += OnNotesChanged;
        Sync();
    }

    /// <summary>Gives a note's frame on the surface, as it is drawn; its natural size until it is laid out.</summary>
    /// <param name="note">The note.</param>
    /// <returns>The frame.</returns>
    public GraphRect FrameOf(CommentNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var view = _views.GetValueOrDefault(note.Key);
        var width = view is { ActualWidth: > 0 } ? view.ActualWidth : note.Width;
        var height = view is { ActualHeight: > 0 } ? view.ActualHeight : CommentGeometry.NaturalHeight;
        return new GraphRect(note.Place.X, note.Place.Y, width, height);
    }

    /// <summary>Puts the views in step with the notes.</summary>
    /// <param name="sender">The notes.</param>
    /// <param name="e">What changed.</param>
    private void OnNotesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    /// <summary>Adds a view for each new note and takes away the views of notes that left.</summary>
    private void Sync()
    {
        if (_comments is null || _linkCommand is null)
        {
            return;
        }

        var keys = _comments.Notes.Select(note => note.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _views.Where(pair => !keys.Contains(pair.Key)).ToList())
        {
            gone.Value.Detach();
            gone.Value.Note.PropertyChanged -= OnNotePlaced;
            Children.Remove(gone.Value);
            _views.Remove(gone.Key);
        }

        foreach (var note in _comments.Notes.Where(note => !_views.ContainsKey(note.Key)))
        {
            var view = new CommentNoteView(note, _comments, this, _surface, _linkCommand);
            view.SizeChanged += (_, _) => NoteResized?.Invoke(this, EventArgs.Empty);
            note.PropertyChanged += OnNotePlaced;
            Place(view, note);
            _views[note.Key] = view;
            Children.Add(view);
        }
    }

    /// <summary>Moves a note's view when its place changes.</summary>
    /// <param name="sender">The note.</param>
    /// <param name="e">What changed.</param>
    private void OnNotePlaced(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommentNote.Place) && sender is CommentNote note
            && _views.TryGetValue(note.Key, out var view))
        {
            Place(view, note);
        }
    }

    /// <summary>Puts a view at its note's place.</summary>
    /// <param name="view">The view.</param>
    /// <param name="note">The note.</param>
    private static void Place(UIElement view, CommentNote note)
    {
        SetLeft(view, note.Place.X);
        SetTop(view, note.Place.Y);
    }
}
