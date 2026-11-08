using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The graph's trash (sketch SO1a): throws and puttings back as sealed records in <see cref="TrashSchema"/>, each with a
/// line in the journal in the same transaction. What lies in the trash is read by replaying the records, oldest first.
/// </summary>
internal sealed class GraphTrashService : IGraphTrash
{
    /// <summary>The action of a record that throws.</summary>
    public const string ThrowAction = "throw";

    /// <summary>The action of a record that puts back.</summary>
    public const string RestoreAction = "restore";

    /// <summary>The longest name or sort a member may have, so that a record stays small.</summary>
    private const int MaximumText = 200;

    /// <summary>How the members are written in a record.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public IReadOnlyList<TrashThrow> GetThrows(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(Replay);
    }

    /// <inheritdoc/>
    public TrashThrow Throw(ICase @case, IReadOnlyList<TrashMember> members)
    {
        ArgumentNullException.ThrowIfNull(@case);
        CheckTree(members);
        return @case.Write(transaction =>
        {
            var lying = Replay(transaction).SelectMany(each => each.Members)
                .SelectMany(member => member.FoundBy.Append(member.Key))
                .ToHashSet(StringComparer.Ordinal);
            if (members.FirstOrDefault(member => TrashMember.IsHidden(member.Key, member.Keys, lying)) is { } twice)
            {
                throw new InvalidOperationException($"The node {twice.Key} already lies in the trash.");
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.Trashed,
                Summary("Slängde i soptunnan: {0}", members),
                JsonSerializer.Serialize(new { members = members.Select(member => member.Key) })));
            var record = transaction.AppendSealed(
                TrashSchema.Chain,
                [ThrowAction, null, JsonSerializer.Serialize(members.Select(Stored), Json),
                    JsonSerializer.Serialize(journal.Operator), journal.Sequence, Text(journal.Utc)]);
            return new TrashThrow(record.Sequence, members, journal.Operator, journal.Utc, journal.Sequence);
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrashMember> Restore(ICase @case, long throwSequence, string? key)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Write(transaction =>
        {
            var thrown = Replay(transaction).FirstOrDefault(each => each.Sequence == throwSequence);
            if (thrown is null)
            {
                return [];
            }

            var back = key is null ? thrown.Members : thrown.Restoring(key);
            if (back.Count == 0)
            {
                return back;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.Restored,
                Summary("Lade tillbaka ur soptunnan: {0}", back),
                JsonSerializer.Serialize(new { @throw = throwSequence, members = back.Select(member => member.Key) })));
            transaction.AppendSealed(
                TrashSchema.Chain,
                [RestoreAction, throwSequence, JsonSerializer.Serialize(back.Select(member => member.Key), Json),
                    JsonSerializer.Serialize(journal.Operator), journal.Sequence, Text(journal.Utc)]);
            return back;
        });
    }

    /// <summary>
    /// Checks that members make a throw's tree: at least one, the first a root, each key once, each parent earlier, and
    /// a name and a sort on each.
    /// </summary>
    /// <param name="members">The members.</param>
    /// <exception cref="ArgumentException">They do not.</exception>
    private static void CheckTree(IReadOnlyList<TrashMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count == 0 || !members[0].IsRoot)
        {
            throw new ArgumentException("A throw starts with what the user threw.", nameof(members));
        }

        var earlier = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            ArgumentNullException.ThrowIfNull(member);
            var whole = !string.IsNullOrEmpty(member.Key) && Fits(member.Name) && Fits(member.Sort)
                && member.FoundBy.All(found => !string.IsNullOrEmpty(found));
            if (!whole || (member.Parent is not null && !earlier.Contains(member.Parent)) || !earlier.Add(member.Key))
            {
                throw new ArgumentException(
                    $"The member {member.Key} does not fit in the throw's tree.", nameof(members));
            }
        }
    }

    /// <summary>Says whether a name or sort is given and short enough.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool Fits(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaximumText;

    /// <summary>Writes the journal's line: the first thrown's name, and how many more.</summary>
    /// <param name="format">The line, with <c>{0}</c> for the names.</param>
    /// <param name="members">The members.</param>
    /// <returns>For example <c>Slängde i soptunnan: bc1q…x0wl och 4 till</c>.</returns>
    private static string Summary(string format, IReadOnlyList<TrashMember> members)
    {
        var names = members[0].Name;
        if (members.Count > 1)
        {
            names += string.Create(CultureInfo.InvariantCulture, $" och {members.Count - 1} till");
        }

        return string.Format(CultureInfo.InvariantCulture, format, names);
    }

    /// <summary>Reads every record and gives what lies in the trash now, oldest throw first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The throws with members left.</returns>
    private static List<TrashThrow> Replay(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            "SELECT sequence, action, throw_sequence, members, operator, journal_sequence, recorded_utc "
            + "FROM tracing_trash ORDER BY sequence");
        using var rows = command.ExecuteReader();
        var throws = new List<TrashThrow>();
        while (rows.Read())
        {
            if (rows.GetString(1) == RestoreAction)
            {
                var back = JsonSerializer.Deserialize<List<string>>(rows.GetString(3), Json) ?? [];
                var index = throws.FindIndex(each => each.Sequence == rows.GetInt64(2));
                if (index >= 0)
                {
                    throws[index] = throws[index] with
                    {
                        Members = [.. throws[index].Members.Where(member => !back.Contains(member.Key))],
                    };
                }

                continue;
            }

            var members = JsonSerializer.Deserialize<List<StoredMember>>(rows.GetString(3), Json) ?? [];
            throws.Add(new TrashThrow(
                rows.GetInt64(0),
                [.. members.Select(Member)],
                JsonSerializer.Deserialize<OperatorProfile>(rows.GetString(4))!,
                DateTimeOffset.Parse(rows.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                rows.GetInt64(5)));
        }

        return [.. throws.Where(each => each.Members.Count > 0)];
    }

    /// <summary>Writes a time as the records store it.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>The round-trip text in UTC.</returns>
    private static string Text(DateTimeOffset utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Gives a member as a record stores it.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The stored member.</returns>
    private static StoredMember Stored(TrashMember member) =>
        new(member.Key, member.Parent, member.Name, member.Sort, [.. member.Keys]);

    /// <summary>Gives a stored member back.</summary>
    /// <param name="stored">The stored member.</param>
    /// <returns>The member.</returns>
    private static TrashMember Member(StoredMember stored) =>
        new(stored.Key, stored.Parent, stored.Name, stored.Sort) { Keys = stored.Keys ?? [] };

    /// <summary>A member as a record stores it.</summary>
    /// <param name="Key">The node's key.</param>
    /// <param name="Parent">The member it followed with.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Sort">Its sort.</param>
    /// <param name="Keys">The keys it is known by.</param>
    private sealed record StoredMember(string Key, string? Parent, string Name, string Sort, List<string>? Keys);
}
