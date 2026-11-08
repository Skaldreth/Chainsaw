using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// One comment on the graph (sketch KN1a): a note on the surface with its Markdown, its place, its size and its thread.
/// A new note is a draft until it is left with something written; only then is it sealed in the case.
/// </summary>
public sealed partial class CommentNote : ObservableObject
{
    /// <summary>What a note's foot says while it is written.</summary>
    public const string WritingFoot = "Markdown · Esc lämnar";

    /// <summary>The Swedish culture, for the time in the foot.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Creates a note for a stored comment.</summary>
    /// <param name="comment">The comment.</param>
    public CommentNote(GraphComment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        Key = comment.Id;
        Show(comment);
    }

    /// <summary>Creates a draft, which is not in the case until it is left with something written.</summary>
    /// <param name="key">The draft's key on the surface.</param>
    /// <param name="place">Where it stands.</param>
    /// <param name="anchor">The thread's stored key, or <see langword="null"/>.</param>
    public CommentNote(string key, GraphPoint place, string? anchor)
    {
        Key = key;
        Place = place;
        Anchor = anchor;
        Text = string.Empty;
    }

    /// <summary>Gets the note's key on the surface: the comment's id, or the draft's.</summary>
    public string Key { get; }

    /// <summary>Gets the stored comment, or <see langword="null"/> while the note is a draft.</summary>
    public GraphComment? Comment { get; private set; }

    /// <summary>Gets the comment's id, or <see langword="null"/> while the note is a draft.</summary>
    public string? Id => Comment?.Id;

    /// <summary>Gets whether the note is a draft, not yet in the case.</summary>
    public bool IsDraft => Comment is null;

    /// <summary>Gets the note's Markdown as stored, or empty for a draft.</summary>
    [ObservableProperty]
    public partial string Text { get; private set; }

    /// <summary>
    /// Gets or sets what the note says while it is written, bound to its editor; stored when the note is left.
    /// </summary>
    [ObservableProperty]
    public partial string Writing { get; set; } = string.Empty;

    /// <summary>Gets the thread's stored key, or <see langword="null"/> for a note on its own.</summary>
    [ObservableProperty]
    public partial string? Anchor { get; private set; }

    /// <summary>Gets the node the thread goes to in the graph shown, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? AnchorNode { get; internal set; }

    /// <summary>Gets whether the node the thread goes to lies in the trash, so that the note is hidden with it.</summary>
    [ObservableProperty]
    public partial bool IsInTrash { get; internal set; }

    /// <summary>Gets the note's top left corner on the surface.</summary>
    [ObservableProperty]
    public partial GraphPoint Place { get; internal set; }

    /// <summary>Gets the size the note was dragged to, or <see langword="null"/> while it grows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Width), nameof(MinimumHeight))]
    public partial CommentSize? Size { get; internal set; }

    /// <summary>Gets the note's width: the dragged one, or its natural width.</summary>
    public double Width => Size?.Width ?? CommentGeometry.NaturalWidth;

    /// <summary>Gets the least height the note is drawn at; its text may make it higher.</summary>
    public double MinimumHeight => Size?.Height ?? 0;

    /// <summary>Gets whether the note is chosen; its frame is lighter and its grip shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Foot))]
    public partial bool IsSelected { get; internal set; }

    /// <summary>Gets whether the note is being written in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Foot))]
    public partial bool IsEditing { get; internal set; }

    /// <summary>Gets who wrote the note last and when, for its foot; empty for a draft.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Foot))]
    public partial string Written { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what the note's foot says, on hovering or when chosen: how to leave it while it is written, otherwise who
    /// wrote it last and when.
    /// </summary>
    public string Foot => IsEditing ? WritingFoot : Written;

    /// <summary>Gets whether a write of the note is queued or running, so that it is not dropped meanwhile.</summary>
    public bool IsStoring => Storing > 0;

    /// <summary>Gets or sets how many writes of the note are queued or running.</summary>
    internal int Storing { get; set; }

    /// <summary>Gets or sets the last write queued for the note; the next waits for it, so writes never race.</summary>
    internal Task Pending { get; set; } = Task.CompletedTask;

    /// <summary>Takes in what the case says about the comment, also where it stands, when the case is read.</summary>
    /// <param name="comment">The comment.</param>
    internal void Show(GraphComment comment)
    {
        ShowText(comment);
        Place = new GraphPoint(comment.Place.X, comment.Place.Y);
        Size = comment.Size;
    }

    /// <summary>
    /// Takes in what a sealed write says about the comment: its text, thread and who wrote it. Its place and size are
    /// kept, since they may be moving while the write ran.
    /// </summary>
    /// <param name="comment">The comment.</param>
    internal void ShowText(GraphComment comment)
    {
        Comment = comment;
        Text = comment.Text;
        Anchor = comment.Anchor;
        Written = WrittenBy(comment.ChangedBy.Name, comment.ChangedUtc);
        OnPropertyChanged(nameof(IsDraft));
        OnPropertyChanged(nameof(Id));
    }

    /// <summary>Takes in the thread's new stored key.</summary>
    /// <param name="anchor">The key, or <see langword="null"/>.</param>
    internal void Tie(string? anchor) => Anchor = anchor;

    /// <summary>Gives the foot's text: who and when, in the computer's local time.</summary>
    /// <param name="name">Who.</param>
    /// <param name="utc">When.</param>
    /// <returns>For example <c>Testa Testsson · 14:32 7 nov</c>.</returns>
    public static string WrittenBy(string name, DateTimeOffset utc) =>
        $"{name} · {utc.ToLocalTime().ToString("HH:mm d MMM", Swedish)}";
}
