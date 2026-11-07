using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>Reads and writes the graph's state in the case's tables (see <see cref="TracingSchema"/>).</summary>
internal static class GraphStateStore
{
    /// <summary>The kind of a start point or expansion at an address.</summary>
    public const string AddressKind = "address";

    /// <summary>The kind of a start point or expansion at a transaction.</summary>
    public const string TransactionKind = "transaction";

    /// <summary>The action of a sealed record that links a coin to another chain.</summary>
    public const string LinkAction = "link";

    /// <summary>The action of a sealed record that takes a link back.</summary>
    public const string UnlinkAction = "unlink";

    /// <summary>Reads the whole state.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The state.</returns>
    public static GraphState Read(ICaseReader reader) =>
        new(ReadStartPoints(reader), ReadExpansions(reader), ReadLinks(reader), ReadPositions(reader));

    /// <summary>Gives a start point's kind and subject as stored.</summary>
    /// <param name="startPoint">The start point.</param>
    /// <returns>The kind and the address or txid.</returns>
    public static (string Kind, string Subject) Key(TraceStartPoint startPoint) => startPoint.Address is { } address
        ? (AddressKind, address.Value)
        : (TransactionKind, startPoint.Txid!.Value);

    /// <summary>Says whether a start point is stored.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="startPoint">The start point.</param>
    /// <returns>Whether it is.</returns>
    public static bool HasStartPoint(ICaseReader reader, TraceStartPoint startPoint)
    {
        var (kind, subject) = Key(startPoint);
        using var command = reader.CreateCommand(
            "SELECT COUNT(*) FROM tracing_start_points WHERE kind = $kind AND subject = $subject");
        command.AddParameter("$kind", kind).AddParameter("$subject", subject);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>Stores a start point.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="startPoint">The start point.</param>
    /// <param name="journal">The journal entry that records it.</param>
    public static void InsertStartPoint(ICaseTransaction transaction, TraceStartPoint startPoint, JournalEntry journal)
    {
        var (kind, subject) = Key(startPoint);
        using var command = transaction.CreateCommand("""
            INSERT INTO tracing_start_points (kind, subject, journal_sequence, added_utc)
            VALUES ($kind, $subject, $journal, $utc)
            """);
        command.AddParameter("$kind", kind).AddParameter("$subject", subject)
            .AddParameter("$journal", journal.Sequence).AddParameter("$utc", Text(journal.Utc));
        command.ExecuteNonQuery();
    }

    /// <summary>Removes a start point.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="startPoint">The start point.</param>
    public static void DeleteStartPoint(ICaseTransaction transaction, TraceStartPoint startPoint)
    {
        var (kind, subject) = Key(startPoint);
        using var command = transaction.CreateCommand(
            "DELETE FROM tracing_start_points WHERE kind = $kind AND subject = $subject");
        command.AddParameter("$kind", kind).AddParameter("$subject", subject);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Stores an expansion. An earlier expansion of the same address or transaction keeps what it brought in: the
    /// graph only grows when the user expands, and expanding again with a lower limit or after newer history never
    /// takes transactions out of it. Whether it is complete is what the newest expansion says.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="expansion">The expansion.</param>
    /// <returns>The expansion as stored, with the earlier transactions after the new.</returns>
    public static Expansion SaveExpansion(ICaseTransaction transaction, Expansion expansion)
    {
        var earlier = ReadExpansions(transaction).FirstOrDefault(stored =>
            stored.Address == expansion.Address && stored.Txid == expansion.Txid);
        if (earlier is not null)
        {
            expansion = expansion with
            {
                Transactions = [.. expansion.Transactions.Concat(earlier.Transactions).Distinct()],
            };
        }

        using var command = transaction.CreateCommand("""
            INSERT INTO tracing_expansions (kind, subject, transactions, complete, expanded_utc)
            VALUES ($kind, $subject, $transactions, $complete, $utc)
            ON CONFLICT (kind, subject) DO UPDATE SET transactions = excluded.transactions,
                complete = excluded.complete, expanded_utc = excluded.expanded_utc
            """);
        command
            .AddParameter("$kind", expansion.Address is null ? TransactionKind : AddressKind)
            .AddParameter("$subject", expansion.Address?.Value ?? expansion.Txid!.Value)
            .AddParameter("$transactions", JsonSerializer.Serialize(expansion.Transactions.Select(txid => txid.Value)))
            .AddParameter("$complete", expansion.IsComplete ? 1L : 0L)
            .AddParameter("$utc", Text(expansion.ExpandedUtc));
        command.ExecuteNonQuery();
        return expansion;
    }

    /// <summary>Stores a node's place in a layout, replacing the earlier one.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="positionKey">The key the place is stored under.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="position">The place.</param>
    /// <param name="utc">When.</param>
    public static void SavePosition(
        ICaseTransaction transaction, string positionKey, GraphLayout layout, NodePosition position, DateTimeOffset utc)
    {
        using var command = transaction.CreateCommand("""
            INSERT INTO tracing_positions (position_key, layout, x, y, updated_utc)
            VALUES ($key, $layout, $x, $y, $utc)
            ON CONFLICT (position_key, layout) DO UPDATE SET x = excluded.x, y = excluded.y,
                updated_utc = excluded.updated_utc
            """);
        command.AddParameter("$key", positionKey).AddParameter("$layout", layout.ToString())
            .AddParameter("$x", position.X).AddParameter("$y", position.Y).AddParameter("$utc", Text(utc));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads how the user left the graph. A stored value this build does not know (an unknown method or layout, or a
    /// part on screen that is not finite) counts as not stored, so the default holds.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The view, or <see langword="null"/> when none is stored.</returns>
    public static GraphViewState? ReadView(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            "SELECT method, layout, amount_labels, offset_x, offset_y, zoom FROM tracing_view WHERE id = 1");
        using var rows = command.ExecuteReader();
        if (!rows.Read()
            || !Enum.TryParse<TracingMethodKind>(rows.GetString(0), out var method) || !Enum.IsDefined(method)
            || !Enum.TryParse<GraphLayout>(rows.GetString(1), out var layout) || !Enum.IsDefined(layout)
            || !Enum.TryParse<GraphAmountLabels>(rows.GetString(2), out var labels) || !Enum.IsDefined(labels))
        {
            return null;
        }

        GraphViewport? viewport = null;
        if (!rows.IsDBNull(3) && !rows.IsDBNull(4) && !rows.IsDBNull(5))
        {
            var candidate = new GraphViewport(rows.GetDouble(3), rows.GetDouble(4), rows.GetDouble(5));
            viewport = IsFinite(candidate) ? candidate : null;
        }

        return new GraphViewState(method, layout, labels, viewport);
    }

    /// <summary>Stores how the user left the graph, replacing what was stored.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="view">The view.</param>
    /// <param name="utc">When.</param>
    public static void SaveView(ICaseTransaction transaction, GraphViewState view, DateTimeOffset utc)
    {
        using var command = transaction.CreateCommand("""
            INSERT INTO tracing_view (id, method, layout, amount_labels, offset_x, offset_y, zoom, updated_utc)
            VALUES (1, $method, $layout, $labels, $x, $y, $zoom, $utc)
            ON CONFLICT (id) DO UPDATE SET method = excluded.method, layout = excluded.layout,
                amount_labels = excluded.amount_labels, offset_x = excluded.offset_x, offset_y = excluded.offset_y,
                zoom = excluded.zoom, updated_utc = excluded.updated_utc
            """);
        command.AddParameter("$method", view.Method.ToString()).AddParameter("$layout", view.Layout.ToString())
            .AddParameter("$labels", view.AmountLabels.ToString())
            .AddParameter("$x", view.Viewport?.OffsetX).AddParameter("$y", view.Viewport?.OffsetY)
            .AddParameter("$zoom", view.Viewport?.Zoom).AddParameter("$utc", Text(utc));
        command.ExecuteNonQuery();
    }

    /// <summary>Says whether a part on screen has finite numbers and a zoom above zero.</summary>
    /// <param name="viewport">The part on screen.</param>
    /// <returns><see langword="true"/> when it can be used.</returns>
    public static bool IsFinite(GraphViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        return double.IsFinite(viewport.OffsetX) && double.IsFinite(viewport.OffsetY)
            && double.IsFinite(viewport.Zoom) && viewport.Zoom > 0;
    }

    /// <summary>Seals a link to another chain.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="from">The coin.</param>
    /// <param name="to">The other chain's transaction.</param>
    /// <param name="reason">Why.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The link.</returns>
    public static CrossChainLink SealLink(
        ICaseTransaction transaction, Outpoint from, ChainTransactionRef to, string reason, JournalEntry journal)
    {
        var record = transaction.AppendSealed(
            TracingSchema.CrossChainChain,
            [LinkAction, null, from.Txid.Value, (long)from.Index, to.Chain.ToString(), to.Id, reason,
                journal.Sequence, Text(journal.Utc)]);
        return new CrossChainLink(record.Sequence, from, to, reason, journal.Sequence, journal.Utc);
    }

    /// <summary>
    /// Seals that a link is taken back, as a new record; the link's own record is never touched. A taking back without
    /// a reason is stored with an empty reason, since the column holds text.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="link">The link.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry that records it.</param>
    public static void SealUnlink(
        ICaseTransaction transaction, CrossChainLink link, string? reason, JournalEntry journal)
    {
        transaction.AppendSealed(
            TracingSchema.CrossChainChain,
            [UnlinkAction, link.Sequence, link.From.Txid.Value, (long)link.From.Index, link.To.Chain.ToString(),
                link.To.Id, reason ?? string.Empty, journal.Sequence, Text(journal.Utc)]);
    }

    /// <summary>Reads the start points.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The start points, in the order they were added.</returns>
    private static List<TraceStartPoint> ReadStartPoints(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            "SELECT kind, subject FROM tracing_start_points ORDER BY journal_sequence, kind, subject");
        using var rows = command.ExecuteReader();
        var startPoints = new List<TraceStartPoint>();
        while (rows.Read())
        {
            startPoints.Add(rows.GetString(0) == AddressKind
                ? TraceStartPoint.ForAddress(BitcoinAddress.Parse(rows.GetString(1)))
                : TraceStartPoint.ForTransaction(Txid.Parse(rows.GetString(1))));
        }

        return startPoints;
    }

    /// <summary>Reads the expansions.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The expansions, in the order they were last expanded.</returns>
    private static List<Expansion> ReadExpansions(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT kind, subject, transactions, complete, expanded_utc FROM tracing_expansions
            ORDER BY expanded_utc, kind, subject
            """);
        using var rows = command.ExecuteReader();
        var expansions = new List<Expansion>();
        while (rows.Read())
        {
            var isAddress = rows.GetString(0) == AddressKind;
            var transactions = JsonSerializer.Deserialize<List<string>>(rows.GetString(2)) ?? [];
            expansions.Add(new Expansion(
                isAddress ? BitcoinAddress.Parse(rows.GetString(1)) : null,
                isAddress ? null : Txid.Parse(rows.GetString(1)),
                [.. transactions.Select(Txid.Parse)],
                rows.GetInt64(3) != 0,
                Time(rows, 4)));
        }

        return expansions;
    }

    /// <summary>Reads the sealed links and leaves out those that have been taken back.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The links in force, oldest first.</returns>
    private static List<CrossChainLink> ReadLinks(ICaseReader reader) =>
        [.. ReadLinkHistory(reader).Where(entry => !entry.IsTakenBack).Select(entry => entry.Link)];

    /// <summary>Reads every sealed link, also those taken back, each with its taking back; the first counts.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The links, oldest first.</returns>
    public static List<CrossChainLinkHistoryEntry> ReadLinkHistory(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, action, link_sequence, from_txid, from_output, to_chain, to_id, reason, journal_sequence,
                   recorded_utc
            FROM tracing_cross_chain ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var links = new List<CrossChainLink>();
        var takenBack = new Dictionary<long, CrossChainUnlink>();
        while (rows.Read())
        {
            if (rows.GetString(1) == UnlinkAction)
            {
                var reason = rows.GetString(7);
                takenBack.TryAdd(rows.GetInt64(2), new CrossChainUnlink(
                    rows.GetInt64(0), reason.Length == 0 ? null : reason, rows.GetInt64(8), Time(rows, 9)));
                continue;
            }

            links.Add(new CrossChainLink(
                rows.GetInt64(0),
                new Outpoint(Txid.Parse(rows.GetString(3)), checked((int)rows.GetInt64(4))),
                new ChainTransactionRef(Enum.Parse<Chain>(rows.GetString(5)), rows.GetString(6)),
                rows.GetString(7),
                rows.GetInt64(8),
                Time(rows, 9)));
        }

        return
        [
            .. links.Select(link => new CrossChainLinkHistoryEntry(link, takenBack.GetValueOrDefault(link.Sequence))),
        ];
    }

    /// <summary>Reads the places.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The places by position key and layout.</returns>
    private static Dictionary<string, IReadOnlyDictionary<GraphLayout, StoredPosition>> ReadPositions(
        ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            "SELECT position_key, layout, x, y, updated_utc FROM tracing_positions");
        using var rows = command.ExecuteReader();
        var positions = new Dictionary<string, Dictionary<GraphLayout, StoredPosition>>(StringComparer.Ordinal);
        while (rows.Read())
        {
            if (!Enum.TryParse<GraphLayout>(rows.GetString(1), out var layout))
            {
                continue;
            }

            if (!positions.TryGetValue(rows.GetString(0), out var byLayout))
            {
                byLayout = [];
                positions.Add(rows.GetString(0), byLayout);
            }

            byLayout[layout] = new StoredPosition(
                new NodePosition(rows.GetDouble(2), rows.GetDouble(3)), Time(rows, 4));
        }

        return positions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<GraphLayout, StoredPosition>)pair.Value,
            StringComparer.Ordinal);
    }

    /// <summary>Writes a time the way the module stores times: round-trip ISO 8601.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>The text.</returns>
    private static string Text(DateTimeOffset utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Reads a stored time.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="column">The column.</param>
    /// <returns>The time.</returns>
    private static DateTimeOffset Time(DbDataReader rows, int column) =>
        DateTimeOffset.Parse(rows.GetString(column), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
