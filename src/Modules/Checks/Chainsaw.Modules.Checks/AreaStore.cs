using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>Reads and writes the own areas sealed in a case (<see cref="CheckSchema.AreasChain"/>).</summary>
internal static class AreaStore
{
    /// <summary>The action of a record that draws an area.</summary>
    public const string AddAction = "add";

    /// <summary>The action of a record that renames an area.</summary>
    public const string RenameAction = "rename";

    /// <summary>The action of a record that removes an area.</summary>
    public const string RemoveAction = "remove";

    /// <summary>Reads the areas in force, oldest first, with their latest names.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The areas.</returns>
    public static IReadOnlyList<OwnArea> Read(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, action, area_id, name, sort, tint, same_actor, note, members, set_by, recorded_utc
            FROM checks_areas ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var areas = new Dictionary<string, OwnArea>(StringComparer.Ordinal);
        var order = new List<string>();
        while (rows.Read())
        {
            var id = rows.GetString(2);
            switch (rows.GetString(1))
            {
                case AddAction:
                    areas[id] = new OwnArea(
                        id,
                        rows.GetString(3),
                        Enum.Parse<AreaSort>(rows.GetString(4)),
                        Enum.Parse<AreaTint>(rows.GetString(5)),
                        rows.GetInt64(6) != 0,
                        rows.IsDBNull(7) ? null : rows.GetString(7),
                        JsonSerializer.Deserialize<List<string>>(rows.GetString(8)) ?? [],
                        CheckStore.Operator(rows.GetString(9)),
                        FindingCodec.Time(rows.GetString(10)),
                        rows.GetInt64(0));
                    order.Add(id);
                    break;
                case RenameAction when areas.TryGetValue(id, out var renamed):
                    areas[id] = renamed with { Name = rows.GetString(3) };
                    break;
                case RemoveAction:
                    areas.Remove(id);
                    break;
                default:
                    throw new InvalidDataException("An own area's record has an unknown action.");
            }
        }

        return [.. order.Where(areas.ContainsKey).Select(id => areas[id])];
    }

    /// <summary>Seals a record about an area.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="action">What the record does.</param>
    /// <param name="area">The area as it is after the record; for a removal, as it was.</param>
    /// <param name="reason">Why, for a removal; otherwise <see langword="null"/>.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The sealed record.</returns>
    public static SealedRecord Seal(
        ICaseTransaction transaction, string action, OwnArea area, string? reason, JournalEntry journal)
    {
        var full = action == AddAction;
        return transaction.AppendSealed(
            CheckSchema.AreasChain,
            [
                action, area.Id, action == RemoveAction ? null : area.Name, full ? area.Sort.ToString() : null,
                full ? area.Tint.ToString() : null, full ? (area.SameActor ? 1L : 0L) : null,
                full ? area.Note : null, full ? JsonSerializer.Serialize(area.Members) : null, reason,
                JsonSerializer.Serialize(journal.Operator), journal.Sequence, FindingCodec.Text(journal.Utc),
            ]);
    }
}
