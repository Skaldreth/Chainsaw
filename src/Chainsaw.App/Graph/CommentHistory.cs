using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>What the inspector reads to write a comment's history: its sealed records and its records in the trash.</summary>
/// <param name="Records">Every sealed record about the comment, oldest first (Tracing's <c>tracing.comments</c>).</param>
/// <param name="Trash">Every throw and putting back of it, oldest first (Tracing's trash).</param>
public sealed record CommentHistoryData(IReadOnlyList<GraphCommentRecord> Records, IReadOnlyList<TrashRecord> Trash);

/// <summary>Reads a comment's history off the UI thread, so that the tests can stand in for the case.</summary>
public interface ICommentHistorySource
{
    /// <summary>Reads what a comment's history is written from.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <param name="cancellationToken">Stops the work before it starts.</param>
    /// <returns>Its records and its records in the trash.</returns>
    Task<CommentHistoryData> ReadAsync(ICase @case, string id, CancellationToken cancellationToken);
}

/// <summary>A comment's history from Tracing: its own records and the trash's, read one after the other.</summary>
/// <param name="comments">The comments.</param>
/// <param name="trash">The trash, or none where it is not wired; then the history has no rows from it.</param>
public sealed class CommentHistorySource(IGraphComments comments, Chainsaw.App.ViewModels.ITrashSource? trash)
    : ICommentHistorySource
{
    /// <inheritdoc/>
    public async Task<CommentHistoryData> ReadAsync(ICase @case, string id, CancellationToken cancellationToken)
    {
        var records = await comments.HistoryAsync(@case, id, cancellationToken);
        IReadOnlyList<TrashRecord> thrown = trash is null
            ? []
            : await trash.HistoryAsync(@case, TrashKeys.OfComment(id), cancellationToken);
        return new CommentHistoryData(records, thrown);
    }
}

/// <summary>
/// Writes a comment's history (sketches AN1b and KN1a) from its sealed records: when it was written, each change of
/// its text and its thread, and each time it was thrown in the trash and put back, in time order. The rows are read
/// only; a comment is put back from the trash's view, where it lies. Knows nothing of WPF.
/// </summary>
public static class CommentHistory
{
    /// <summary>The row of a comment written.</summary>
    public const string WrittenText = "Kommentar skriven";

    /// <summary>The row of a comment's text changed.</summary>
    public const string EditedText = "Texten ändrad";

    /// <summary>The row of a thread untied.</summary>
    public const string UntiedText = "Tråden lossad";

    /// <summary>The start of the row of a thread tied to a node.</summary>
    public const string TiedText = "Tråd till ";

    /// <summary>What a thread's node is called when it is not in the graph shown.</summary>
    public const string NotInGraphText = "en nod utanför grafen";

    /// <summary>The row of a comment removed, in a case written before Delete threw comments in the trash.</summary>
    public const string RemovedText = "Borttagen";

    /// <summary>The row of a comment thrown in the trash.</summary>
    public const string ThrownText = "Slängd i soptunnan";

    /// <summary>The row of a comment put back from the trash.</summary>
    public const string RestoredText = "Lagd tillbaka ur soptunnan";

    /// <summary>How many characters of a changed text the row's quiet line shows.</summary>
    private const int TextShown = 40;

    /// <summary>Writes the comment's history, oldest first.</summary>
    /// <param name="data">What was read.</param>
    /// <param name="threadName">Gives the name of the node a thread's key goes to, or none when it is not shown.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<HistoryEvent> Build(CommentHistoryData data, Func<string, string?> threadName)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(threadName);
        var rows = new List<HistoryEvent>();
        rows.AddRange(data.Records.Select(record => RowOf(record, threadName)));
        rows.AddRange(data.Trash.Select(RowOf));

        // In time order; what happened at the same moment keeps the comment's own records first.
        return [.. rows.Select((row, index) => (row, index)).OrderBy(pair => pair.row.Utc).ThenBy(pair => pair.index)
            .Select(pair => pair.row)];
    }

    /// <summary>Gives the row of one of the comment's own records.</summary>
    /// <param name="record">The record.</param>
    /// <param name="threadName">Gives the name of a thread's node.</param>
    /// <returns>The row.</returns>
    private static HistoryEvent RowOf(GraphCommentRecord record, Func<string, string?> threadName)
    {
        var key = "comment/" + record.Sequence.ToString(CultureInfo.InvariantCulture);
        var who = record.By.Name;
        return record.Action switch
        {
            CommentAction.Added => Row(key, record.Utc, WrittenText, Quoted(who, record.Text), "Icon.Note"),
            CommentAction.Edited => Row(key, record.Utc, EditedText, Quoted(who, record.Text), "Icon.Pen"),
            CommentAction.Anchored when record.Anchor is null => Row(key, record.Utc, UntiedText, who, "Icon.Link"),
            CommentAction.Anchored => Row(
                key, record.Utc, TiedText + (threadName(record.Anchor) ?? NotInGraphText), who, "Icon.Link"),
            _ => Row(key, record.Utc, RemovedText, who, "Icon.Trash"),
        };
    }

    /// <summary>Gives the row of a throw or a putting back.</summary>
    /// <param name="record">The trash's record.</param>
    /// <returns>The row.</returns>
    private static HistoryEvent RowOf(TrashRecord record)
    {
        var key = "trash/" + record.Sequence.ToString(CultureInfo.InvariantCulture);
        return record.Action == TrashAction.Thrown
            ? Row(key, record.Utc, ThrownText, record.By.Name, "Icon.Trash")
            : Row(key, record.Utc, RestoredText, record.By.Name, "Icon.Undo");
    }

    /// <summary>Gives a row: the user's own record, never a fact, without a button.</summary>
    /// <param name="key">The row's key.</param>
    /// <param name="utc">When.</param>
    /// <param name="text">The row's words.</param>
    /// <param name="sub">The quiet line under.</param>
    /// <param name="icon">The icon's resource key.</param>
    /// <returns>The row.</returns>
    private static HistoryEvent Row(string key, DateTimeOffset utc, string text, string sub, string icon) =>
        new(key, HistoryKind.Comment, utc, text, sub, icon, GraphInk.Unknown, IsDashed: false, IsFact: false,
            IsWithdrawn: false, HistoryUndo.None);

    /// <summary>Writes who, and the first line of what the text said then.</summary>
    /// <param name="who">Who.</param>
    /// <param name="text">The text.</param>
    /// <returns>For example <c>Testa Testsson · ”Uttag via Kraken?”</c>.</returns>
    private static string Quoted(string who, string text)
    {
        var title = CommentRules.Title(text, TextShown);
        return title.Length == 0 ? who : $"{who} · ”{title}”";
    }
}
