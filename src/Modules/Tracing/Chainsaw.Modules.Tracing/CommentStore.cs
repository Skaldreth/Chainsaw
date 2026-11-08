using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Reads and writes the graph's comments in a case: the sealed records in <see cref="TracingSchema.CommentsChain"/>
/// and the ordinary places in <c>tracing_comment_places</c>.
/// </summary>
internal static class CommentStore
{
    /// <summary>The action of a record that writes a comment.</summary>
    public const string AddAction = "add";

    /// <summary>The action of a record that changes a comment's text.</summary>
    public const string EditAction = "edit";

    /// <summary>The action of a record that ties a comment's thread anew.</summary>
    public const string AnchorAction = "anchor";

    /// <summary>The action of a record that removes a comment.</summary>
    public const string RemoveAction = "remove";

    /// <summary>Reads every sealed record, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The records with the comment each is about, and its first place for a record that wrote one.</returns>
    public static IReadOnlyList<StoredRecord> ReadRecords(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, action, comment_id, body, anchor, x, y, set_by, journal_sequence, recorded_utc
            FROM tracing_comments ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var records = new List<StoredRecord>();
        while (rows.Read())
        {
            var record = new GraphCommentRecord(
                rows.GetInt64(0),
                ActionOf(rows.GetString(1)),
                rows.GetString(3),
                rows.IsDBNull(4) ? null : rows.GetString(4),
                Operator(rows.GetString(7)),
                Time(rows, 9),
                rows.GetInt64(8));
            NodePosition? place = rows.IsDBNull(5) || rows.IsDBNull(6)
                ? null
                : new NodePosition(rows.GetDouble(5), rows.GetDouble(6));
            records.Add(new StoredRecord(rows.GetString(2), record, place));
        }

        return records;
    }

    /// <summary>
    /// Reads the comments in force, oldest first: each from its records, at the place it was last moved to and with
    /// the size it was dragged to.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The comments.</returns>
    public static IReadOnlyList<GraphComment> Read(ICaseReader reader)
    {
        var comments = new Dictionary<string, GraphComment>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var (id, record, place) in ReadRecords(reader))
        {
            switch (record.Action)
            {
                case CommentAction.Added:
                    comments[id] = new GraphComment(
                        id, record.Text, record.Anchor, place ?? default, null, record.By, record.Utc, record.By,
                        record.Utc, record.Sequence);
                    order.Add(id);
                    break;
                case CommentAction.Edited when comments.TryGetValue(id, out var edited):
                    comments[id] = edited with { Text = record.Text, ChangedBy = record.By, ChangedUtc = record.Utc };
                    break;
                case CommentAction.Anchored when comments.TryGetValue(id, out var anchored):
                    comments[id] = anchored with { Anchor = record.Anchor };
                    break;
                case CommentAction.Removed:
                    comments.Remove(id);
                    break;
            }
        }

        var places = ReadPlaces(reader);
        return
        [
            .. order.Where(comments.ContainsKey).Select(id => places.TryGetValue(id, out var stored)
                ? comments[id] with { Place = stored.Place, Size = stored.Size }
                : comments[id]),
        ];
    }

    /// <summary>Seals a record about a comment.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="action">What the record does.</param>
    /// <param name="id">The comment.</param>
    /// <param name="text">The text after the record; for a removal, the text that was removed.</param>
    /// <param name="anchor">The thread after the record; for a removal, the thread it had.</param>
    /// <param name="place">Where a comment is first written; <see langword="null"/> for the other records.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The sealed record.</returns>
    public static SealedRecord Seal(
        ICaseTransaction transaction,
        string action,
        string id,
        string text,
        string? anchor,
        NodePosition? place,
        JournalEntry journal) =>
        transaction.AppendSealed(
            TracingSchema.CommentsChain,
            [
                action, id, text, anchor, place?.X, place?.Y, JsonSerializer.Serialize(journal.Operator),
                journal.Sequence, Text(journal.Utc),
            ]);

    /// <summary>Stores where a comment stands and its size, over what was stored before.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="id">The comment.</param>
    /// <param name="place">Its top left corner.</param>
    /// <param name="size">Its size, or <see langword="null"/>.</param>
    /// <param name="utc">When.</param>
    public static void SavePlace(
        ICaseTransaction transaction, string id, NodePosition place, CommentSize? size, DateTimeOffset utc)
    {
        using var command = transaction.CreateCommand("""
            INSERT INTO tracing_comment_places (comment_id, x, y, width, height, updated_utc)
            VALUES ($id, $x, $y, $width, $height, $utc)
            ON CONFLICT (comment_id) DO UPDATE SET
                x = excluded.x, y = excluded.y, width = excluded.width, height = excluded.height,
                updated_utc = excluded.updated_utc
            """);
        command.AddParameter("$id", id)
            .AddParameter("$x", place.X)
            .AddParameter("$y", place.Y)
            .AddParameter("$width", size?.Width)
            .AddParameter("$height", size?.Height)
            .AddParameter("$utc", Text(utc));
        command.ExecuteNonQuery();
    }

    /// <summary>Reads where each comment was moved to and its size.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The places by the comments' ids.</returns>
    private static Dictionary<string, (NodePosition Place, CommentSize? Size)> ReadPlaces(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            "SELECT comment_id, x, y, width, height FROM tracing_comment_places");
        using var rows = command.ExecuteReader();
        var places = new Dictionary<string, (NodePosition, CommentSize?)>(StringComparer.Ordinal);
        while (rows.Read())
        {
            CommentSize? size = rows.IsDBNull(3) || rows.IsDBNull(4)
                ? null
                : new CommentSize(rows.GetDouble(3), rows.GetDouble(4));
            places[rows.GetString(0)] = (new NodePosition(rows.GetDouble(1), rows.GetDouble(2)), size);
        }

        return places;
    }

    /// <summary>Reads a stored action.</summary>
    /// <param name="action">The stored text.</param>
    /// <returns>The action.</returns>
    /// <exception cref="InvalidDataException">The action is unknown.</exception>
    private static CommentAction ActionOf(string action) => action switch
    {
        AddAction => CommentAction.Added,
        EditAction => CommentAction.Edited,
        AnchorAction => CommentAction.Anchored,
        RemoveAction => CommentAction.Removed,
        _ => throw new InvalidDataException("A comment's record has an unknown action."),
    };

    /// <summary>Reads the operator a record was written by.</summary>
    /// <param name="json">The stored profile.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="InvalidDataException">The record has no operator.</exception>
    private static OperatorProfile Operator(string json) =>
        JsonSerializer.Deserialize<OperatorProfile>(json)
        ?? throw new InvalidDataException("A comment's record has no operator.");

    /// <summary>Writes a time as it is stored.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>The time in UTC, round-trip format.</returns>
    private static string Text(DateTimeOffset utc) =>
        utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Reads a stored time from a column.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="column">The column.</param>
    /// <returns>The time.</returns>
    private static DateTimeOffset Time(DbDataReader rows, int column) => DateTimeOffset.Parse(
        rows.GetString(column), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>A sealed record with the comment it is about.</summary>
    /// <param name="Id">The comment.</param>
    /// <param name="Record">The record.</param>
    /// <param name="Place">The first place, for a record that wrote the comment.</param>
    public sealed record StoredRecord(string Id, GraphCommentRecord Record, NodePosition? Place);
}
