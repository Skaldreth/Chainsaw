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

    /// <summary>The kind of a start point or expansion at an Ethereum address.</summary>
    public const string EthereumAddressKind = "ethereum-address";

    /// <summary>The kind of a start point or expansion at an Ethereum transaction.</summary>
    public const string EthereumTransactionKind = "ethereum-transaction";

    /// <summary>The kind of a start point or expansion at a TRON address.</summary>
    public const string TronAddressKind = "tron-address";

    /// <summary>The kind of a start point or expansion at a TRON transaction.</summary>
    public const string TronTransactionKind = "tron-transaction";

    /// <summary>The kind of a start point or expansion at an address on BSC.</summary>
    public const string BscAddressKind = "bsc-address";

    /// <summary>The kind of a start point or expansion at a transaction on BSC.</summary>
    public const string BscTransactionKind = "bsc-transaction";

    /// <summary>The kind of a start point or expansion at a Solana address.</summary>
    public const string SolanaAddressKind = "solana-address";

    /// <summary>The kind of a start point or expansion at a Solana transaction.</summary>
    public const string SolanaTransactionKind = "solana-transaction";

    /// <summary>The action of a sealed record that links a coin to another chain.</summary>
    public const string LinkAction = "link";

    /// <summary>The action of a sealed record that takes a link back.</summary>
    public const string UnlinkAction = "unlink";

    /// <summary>Reads the whole state.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The state.</returns>
    public static GraphState Read(ICaseReader reader) =>
        new(ReadStartPoints(reader), ReadExpansions(reader), ReadLinks(reader), ReadPositions(reader))
        {
            Picked = ReadPicked(reader),
            SwapStops = SwapStopStore.ReadInForce(reader),
            Replacements = ReplacementFollowStore.ReadInForce(reader),
        };

    /// <summary>Remembers that actors were picked out of their piles; one picked already stays as it was.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="memberKeys">The actors' member keys (see <see cref="NodeKeys.Members"/>).</param>
    /// <param name="utc">When.</param>
    public static void SavePicked(ICaseTransaction transaction, IEnumerable<string> memberKeys, DateTimeOffset utc)
    {
        foreach (var key in memberKeys)
        {
            using var command = transaction.CreateCommand("""
                INSERT INTO tracing_picked (member_key, picked_utc) VALUES ($key, $utc)
                ON CONFLICT (member_key) DO NOTHING
                """);
            command.AddParameter("$key", key).AddParameter("$utc", Text(utc));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Forgets that actors were picked out of their piles, so that they lie in them again.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="memberKeys">The actors' member keys.</param>
    public static void DeletePicked(ICaseTransaction transaction, IEnumerable<string> memberKeys)
    {
        foreach (var key in memberKeys)
        {
            using var command = transaction.CreateCommand("DELETE FROM tracing_picked WHERE member_key = $key");
            command.AddParameter("$key", key);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Reads which actors the user picked out of the piles.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>Their member keys.</returns>
    private static HashSet<string> ReadPicked(ICaseReader reader)
    {
        using var command = reader.CreateCommand("SELECT member_key FROM tracing_picked");
        using var rows = command.ExecuteReader();
        var picked = new HashSet<string>(StringComparer.Ordinal);
        while (rows.Read())
        {
            picked.Add(rows.GetString(0));
        }

        return picked;
    }

    /// <summary>Gives a start point's kind and subject as stored.</summary>
    /// <param name="startPoint">The start point.</param>
    /// <returns>The kind and the address or txid.</returns>
    public static (string Kind, string Subject) Key(TraceStartPoint startPoint)
    {
        if (startPoint.Address is { } address)
        {
            return (AddressKind, address.Value);
        }

        if (startPoint.Txid is { } txid)
        {
            return (TransactionKind, txid.Value);
        }

        if (startPoint.EthereumAddress is { } account)
        {
            return (AccountKind(account.Chain, address: true), account.Value);
        }

        var hash = startPoint.EthereumTransaction!;
        return (AccountKind(hash.Chain, address: false), hash.Value);
    }

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
    /// takes transactions out of it. Whether it is complete is what the newest expansion says. What was held back
    /// stays when the newest expansion did not read the whole history (a lookup failed midway, or the user only picked
    /// some transactions out), so that the clue never goes for good; the picked transactions add up, and the first
    /// expansion's time keeps the expansion's place in the order.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="expansion">The expansion.</param>
    /// <returns>The expansion as stored, with the earlier transactions after the new.</returns>
    public static Expansion SaveExpansion(ICaseTransaction transaction, Expansion expansion)
    {
        var kind = KindOf(expansion);
        var earlier = ReadExpansions(transaction).FirstOrDefault(stored =>
            KindOf(stored) == kind && stored.Subject == expansion.Subject);
        if (earlier is not null)
        {
            var readAll = expansion.IsComplete && expansion.Chosen.Count == 0;
            expansion = expansion with
            {
                Transactions = [.. expansion.Transactions.Concat(earlier.Transactions).Distinct()],
                EthereumTransactions =
                    [.. expansion.EthereumTransactions.Concat(earlier.EthereumTransactions).Distinct()],
                Chosen = [.. expansion.Chosen.Concat(earlier.Chosen).Distinct()],
                HeldBack = expansion.HeldBack ?? (readAll ? null : earlier.HeldBack),
                FirstExpandedUtc = earlier.FirstExpandedUtc ?? earlier.ExpandedUtc,
            };
        }

        expansion = expansion with { FirstExpandedUtc = expansion.FirstExpandedUtc ?? expansion.ExpandedUtc };

        IEnumerable<string> brought = AccountChainOf(kind) is not null
            ? expansion.EthereumTransactions.Select(hash => hash.Value)
            : expansion.Transactions.Select(txid => txid.Value);

        using var command = transaction.CreateCommand("""
            INSERT INTO tracing_expansions (kind, subject, transactions, complete, expanded_utc, held_back,
                held_back_taken, held_back_available, chosen, first_expanded_utc)
            VALUES ($kind, $subject, $transactions, $complete, $utc, $held, $taken, $available, $chosen, $first)
            ON CONFLICT (kind, subject) DO UPDATE SET transactions = excluded.transactions,
                complete = excluded.complete, expanded_utc = excluded.expanded_utc, held_back = excluded.held_back,
                held_back_taken = excluded.held_back_taken, held_back_available = excluded.held_back_available,
                chosen = excluded.chosen, first_expanded_utc = excluded.first_expanded_utc
            """);
        command
            .AddParameter("$kind", kind)
            .AddParameter("$subject", expansion.Subject)
            .AddParameter("$transactions", JsonSerializer.Serialize(brought))
            .AddParameter("$complete", expansion.IsComplete ? 1L : 0L)
            .AddParameter("$utc", Text(expansion.ExpandedUtc))
            .AddParameter("$held", expansion.HeldBack is null ? 0L : 1L)
            .AddParameter("$taken", (long?)expansion.HeldBack?.Taken)
            .AddParameter("$available", expansion.HeldBack?.Available)
            .AddParameter("$chosen", JsonSerializer.Serialize(expansion.Chosen.Select(txid => txid.Value)))
            .AddParameter("$first", Text(expansion.FirstExpandedUtc!.Value));
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
            """
            SELECT method, layout, amount_labels, offset_x, offset_y, zoom, show_unspendable FROM tracing_view
            WHERE id = 1
            """);
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

        return new GraphViewState(method, layout, labels, viewport) { ShowUnspendable = rows.GetInt64(6) != 0 };
    }

    /// <summary>Stores how the user left the graph, replacing what was stored.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="view">The view.</param>
    /// <param name="utc">When.</param>
    public static void SaveView(ICaseTransaction transaction, GraphViewState view, DateTimeOffset utc)
    {
        using var command = transaction.CreateCommand("""
            INSERT INTO tracing_view (id, method, layout, amount_labels, offset_x, offset_y, zoom, updated_utc,
                show_unspendable)
            VALUES (1, $method, $layout, $labels, $x, $y, $zoom, $utc, $unspendable)
            ON CONFLICT (id) DO UPDATE SET method = excluded.method, layout = excluded.layout,
                amount_labels = excluded.amount_labels, offset_x = excluded.offset_x, offset_y = excluded.offset_y,
                zoom = excluded.zoom, updated_utc = excluded.updated_utc, show_unspendable = excluded.show_unspendable
            """);
        command.AddParameter("$method", view.Method.ToString()).AddParameter("$layout", view.Layout.ToString())
            .AddParameter("$labels", view.AmountLabels.ToString())
            .AddParameter("$x", view.Viewport?.OffsetX).AddParameter("$y", view.Viewport?.OffsetY)
            .AddParameter("$zoom", view.Viewport?.Zoom).AddParameter("$utc", Text(utc))
            .AddParameter("$unspendable", view.ShowUnspendable ? 1L : 0L);
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
    /// <param name="origin">The approved suggestion it comes from, or <see langword="null"/> when made by hand.</param>
    /// <returns>The link.</returns>
    public static CrossChainLink SealLink(
        ICaseTransaction transaction,
        Outpoint from,
        ChainTransactionRef to,
        string reason,
        JournalEntry journal,
        CrossChainOrigin? origin = null)
    {
        var record = transaction.AppendSealed(
            TracingSchema.CrossChainChain,
            [LinkAction, null, from.Txid.Value, (long)from.Index, to.Chain.ToString(), to.Id, reason,
                journal.Sequence, Text(journal.Utc), origin?.ToString()]);
        return new CrossChainLink(record.Sequence, from, to, reason, journal.Sequence, journal.Utc)
        {
            Origin = origin,
        };
    }

    /// <summary>
    /// Seals a link from an Ethereum movement to another chain. The movement is stored in the same columns as a coin:
    /// its kind and transaction as <c>token:0x…</c> where the coin's txid stands, and its position where the
    /// output's index stands; a Bitcoin txid never holds a colon, so the two never mix.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="from">The movement.</param>
    /// <param name="to">The other chain's transaction.</param>
    /// <param name="reason">Why.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <param name="origin">The approved suggestion it comes from, or <see langword="null"/> when made by hand.</param>
    /// <returns>The link.</returns>
    public static CrossChainLink SealLink(
        ICaseTransaction transaction,
        AccountSeed from,
        ChainTransactionRef to,
        string reason,
        JournalEntry journal,
        CrossChainOrigin? origin = null)
    {
        var record = transaction.AppendSealed(
            TracingSchema.CrossChainChain,
            [LinkAction, null, MovementText(from), from.Position, to.Chain.ToString(), to.Id, reason,
                journal.Sequence, Text(journal.Utc), origin?.ToString()]);
        return new CrossChainLink(record.Sequence, null, to, reason, journal.Sequence, journal.Utc)
        {
            FromTransfer = from,
            Origin = origin,
        };
    }

    /// <summary>
    /// Writes an account movement's kind and transaction as the link's record stores them. Ethereum's hash begins
    /// with <c>0x</c> and TRON's does not; BSC's is written as Ethereum's, so it carries its chain before it; Solana's
    /// is a signature in Base58, which neither is.
    /// </summary>
    /// <param name="from">The movement.</param>
    /// <returns>For example <c>token:0x…</c>, or <c>token:bsc:0x…</c> on BSC.</returns>
    private static string MovementText(AccountSeed from) =>
        from.Kind.ToString().ToLowerInvariant() + ":"
        + (from.Transaction.Chain == Chain.Bsc ? BscMovementPrefix : string.Empty) + from.Transaction.Value;

    /// <summary>What a stored movement's hash on BSC begins with.</summary>
    private const string BscMovementPrefix = "bsc:";

    /// <summary>Reads where a stored link's money left: a coin, or an Ethereum movement.</summary>
    /// <param name="text">The stored txid or movement.</param>
    /// <param name="index">The stored output's index or position.</param>
    /// <returns>The coin or the movement.</returns>
    private static (Outpoint? Coin, AccountSeed? Movement) ReadFrom(string text, long index)
    {
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return (new Outpoint(Txid.Parse(text), checked((int)index)), null);
        }

        var kind = Enum.Parse<EthereumTransferKind>(text[..colon], ignoreCase: true);
        // Ethereum's hashes begin with 0x and TRON's do not, and BSC's carry their chain, so the text says its chain.
        var hash = text[(colon + 1)..];
        return (null, new AccountSeed(MovementHash(hash), kind, index));
    }

    /// <summary>Reads a stored movement's transaction on its chain.</summary>
    /// <param name="hash">The stored hash, after the kind.</param>
    /// <returns>The transaction.</returns>
    private static EthereumTxHash MovementHash(string hash)
    {
        if (hash.StartsWith(BscMovementPrefix, StringComparison.Ordinal))
        {
            return EthereumTxHash.Parse(Chain.Bsc, hash[BscMovementPrefix.Length..]);
        }

        if (EthereumTxHash.TryParse(hash, out var ethereum))
        {
            return ethereum;
        }

        // A Solana signature is Base58 (up to 88 characters), which no TRON id (64 hex digits) is.
        return EthereumTxHash.TryParse(Chain.Solana, hash, out var solana)
            ? solana
            : EthereumTxHash.Parse(Chain.Tron, hash);
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
            [UnlinkAction, link.Sequence,
                link.From is { } coin ? coin.Txid.Value : MovementText(link.FromTransfer!),
                link.From is { } output ? output.Index : link.FromTransfer!.Position,
                link.To.Chain.ToString(), link.To.Id, reason ?? string.Empty, journal.Sequence, Text(journal.Utc),
                null]);
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
            var (kind, subject) = (rows.GetString(0), rows.GetString(1));
            startPoints.Add(kind switch
            {
                AddressKind => TraceStartPoint.ForAddress(BitcoinAddress.Parse(subject)),
                EthereumAddressKind or TronAddressKind or BscAddressKind or SolanaAddressKind =>
                    TraceStartPoint.ForEthereumAddress(EthereumAddress.Parse(AccountChainOf(kind)!.Value, subject)),
                EthereumTransactionKind or TronTransactionKind or BscTransactionKind or SolanaTransactionKind =>
                    TraceStartPoint.ForEthereumTransaction(EthereumTxHash.Parse(AccountChainOf(kind)!.Value, subject)),
                _ => TraceStartPoint.ForTransaction(Txid.Parse(subject)),
            });
        }

        return startPoints;
    }

    /// <summary>Reads the expansions.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The expansions, in the order they were first expanded.</returns>
    private static List<Expansion> ReadExpansions(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT kind, subject, transactions, complete, expanded_utc, held_back, held_back_taken,
                   held_back_available, chosen, first_expanded_utc
            FROM tracing_expansions
            ORDER BY COALESCE(first_expanded_utc, expanded_utc), kind, subject
            """);
        using var rows = command.ExecuteReader();
        var expansions = new List<Expansion>();
        while (rows.Read())
        {
            var kind = rows.GetString(0);
            var subject = rows.GetString(1);
            var transactions = JsonSerializer.Deserialize<List<string>>(rows.GetString(2)) ?? [];
            var complete = rows.GetInt64(3) != 0;
            var utc = Time(rows, 4);
            var heldBack = rows.GetInt64(5) == 0
                ? null
                : new ExpansionHoldBack(
                    rows.IsDBNull(6) ? transactions.Count : checked((int)rows.GetInt64(6)),
                    rows.IsDBNull(7) ? null : rows.GetInt64(7));
            expansions.Add((kind switch
            {
                AddressKind => new Expansion(
                    BitcoinAddress.Parse(subject), null, [.. transactions.Select(Txid.Parse)], complete, utc),
                EthereumAddressKind or TronAddressKind or BscAddressKind or SolanaAddressKind =>
                    new Expansion(null, null, [], complete, utc)
                {
                    EthereumAddress = EthereumAddress.Parse(AccountChainOf(kind)!.Value, subject),
                    EthereumTransactions = AccountHashes(kind, transactions),
                },
                EthereumTransactionKind or TronTransactionKind or BscTransactionKind or SolanaTransactionKind =>
                    new Expansion(null, null, [], complete, utc)
                    {
                        EthereumTransaction = EthereumTxHash.Parse(AccountChainOf(kind)!.Value, subject),
                        EthereumTransactions = AccountHashes(kind, transactions),
                    },
                _ => new Expansion(null, Txid.Parse(subject), [.. transactions.Select(Txid.Parse)], complete, utc),
            }) with
            {
                HeldBack = heldBack,
                Chosen = [.. (JsonSerializer.Deserialize<List<string>>(rows.GetString(8)) ?? []).Select(Txid.Parse)],
                FirstExpandedUtc = rows.IsDBNull(9) ? utc : Time(rows, 9),
            });
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
                   recorded_utc, origin
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

            var (coin, movement) = ReadFrom(rows.GetString(3), rows.GetInt64(4));
            links.Add(new CrossChainLink(
                rows.GetInt64(0),
                coin,
                new ChainTransactionRef(Enum.Parse<Chain>(rows.GetString(5)), rows.GetString(6)),
                rows.GetString(7),
                rows.GetInt64(8),
                Time(rows, 9))
            {
                FromTransfer = movement,
                Origin = CrossChainOrigin.Parse(rows.IsDBNull(10) ? null : rows.GetString(10)),
            });
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

    /// <summary>Gives the kind an expansion is stored under.</summary>
    /// <param name="expansion">The expansion.</param>
    /// <returns>The kind.</returns>
    private static string KindOf(Expansion expansion)
    {
        if (expansion.Address is not null)
        {
            return AddressKind;
        }

        if (expansion.Txid is not null)
        {
            return TransactionKind;
        }

        return expansion.EthereumAddress is { } account
            ? AccountKind(account.Chain, address: true)
            : AccountKind(expansion.EthereumTransaction!.Chain, address: false);
    }

    /// <summary>Gives the kind of a start point or expansion on an account chain.</summary>
    /// <param name="chain">The chain: Ethereum or TRON.</param>
    /// <param name="address">Whether it is at an address; otherwise at a transaction.</param>
    /// <returns>The kind.</returns>
    /// <exception cref="ArgumentException">The chain is not an account chain Chainsaw reads.</exception>
    private static string AccountKind(Chain chain, bool address) => (chain, address) switch
    {
        (Chain.Ethereum, true) => EthereumAddressKind,
        (Chain.Ethereum, false) => EthereumTransactionKind,
        (Chain.Tron, true) => TronAddressKind,
        (Chain.Tron, false) => TronTransactionKind,
        (Chain.Bsc, true) => BscAddressKind,
        (Chain.Bsc, false) => BscTransactionKind,
        (Chain.Solana, true) => SolanaAddressKind,
        (Chain.Solana, false) => SolanaTransactionKind,
        _ => throw new ArgumentException("Not an account chain Chainsaw reads.", nameof(chain)),
    };

    /// <summary>Gives the account chain a stored kind is on.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The chain, or <see langword="null"/> for Bitcoin's kinds.</returns>
    private static Chain? AccountChainOf(string kind) => kind switch
    {
        EthereumAddressKind or EthereumTransactionKind => Chain.Ethereum,
        TronAddressKind or TronTransactionKind => Chain.Tron,
        BscAddressKind or BscTransactionKind => Chain.Bsc,
        SolanaAddressKind or SolanaTransactionKind => Chain.Solana,
        _ => null,
    };

    /// <summary>Reads the stored transactions of an expansion on an account chain.</summary>
    /// <param name="kind">The expansion's kind.</param>
    /// <param name="transactions">The stored transactions.</param>
    /// <returns>The transactions on the kind's chain.</returns>
    private static List<EthereumTxHash> AccountHashes(string kind, IEnumerable<string> transactions)
    {
        var chain = AccountChainOf(kind)!.Value;
        return [.. transactions.Select(hash => EthereumTxHash.Parse(chain, hash))];
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
