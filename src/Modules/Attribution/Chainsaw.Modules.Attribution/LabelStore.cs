using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Attribution;

/// <summary>Writes and reads the labels sealed in a case (<see cref="AttributionSchema.LabelsChain"/>).</summary>
internal static class LabelStore
{
    /// <summary>The action of a record that sets a label.</summary>
    private const string AddAction = "add";

    /// <summary>The action of a record that takes a label back.</summary>
    private const string RetractAction = "retract";

    /// <summary>Seals a label.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="label">What the label says, with its origin; its sequence and journal sequence are ignored.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The sealed label.</returns>
    public static LabelEntry Seal(ICaseTransaction transaction, LabelEntry label, JournalEntry journal)
    {
        var origin = label.Origin;
        var record = transaction.AppendSealed(
            AttributionSchema.LabelsChain,
            [
                AddAction, null, origin.OriginId, label.Subject.Kind.ToString(), label.Subject.Address.Value,
                label.Kind.ToString(), label.Name, origin.Source.ToString(), origin.OriginalSource.ToString(),
                origin.SourceName, origin.SourceVersion, JsonSerializer.Serialize(origin.SetBy), Text(origin.SetUtc),
                origin.Reason, origin.Note, label.DoNotSpread ? 1L : 0L, journal.Sequence, Text(journal.Utc),
            ]);
        return label with { Sequence = record.Sequence, JournalSequence = journal.Sequence };
    }

    /// <summary>
    /// Seals that a label is taken back: a new record with who and when (the journal entry's operator and time) and
    /// the reason, if any. The label's own record is never touched.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="label">The stored label.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry that records it.</param>
    public static void SealRetract(
        ICaseTransaction transaction, LabelEntry label, string? reason, JournalEntry journal)
    {
        transaction.AppendSealed(
            AttributionSchema.LabelsChain,
            [
                RetractAction, label.Sequence, label.Origin.OriginId, label.Subject.Kind.ToString(),
                label.Subject.Address.Value, label.Kind.ToString(), null, null, null, null, null,
                JsonSerializer.Serialize(journal.Operator), Text(journal.Utc), reason, null, null, journal.Sequence,
                Text(journal.Utc),
            ]);
    }

    /// <summary>Reads every label in the case, also those taken back.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The labels.</returns>
    public static StoredLabels Read(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, action, label_sequence, origin_id, subject_kind, subject, kind, name, source,
                   original_source, source_name, source_version, set_by, set_utc, reason, note, do_not_spread,
                   journal_sequence, recorded_utc
            FROM attribution_labels ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var all = new List<LabelEntry>();
        var retractions = new Dictionary<long, LabelRetraction>();
        while (rows.Read())
        {
            if (rows.GetString(1) == RetractAction)
            {
                // The first taking back counts; a label is never taken back twice, but a record is never ignored
                // silently either: a later one for the same label changes nothing.
                retractions.TryAdd(rows.GetInt64(2), new LabelRetraction(
                    rows.GetInt64(0),
                    rows.IsDBNull(12) ? null : JsonSerializer.Deserialize<OperatorProfile>(rows.GetString(12)),
                    Time(rows.IsDBNull(13) ? rows.GetString(18) : rows.GetString(13)),
                    rows.IsDBNull(14) ? null : rows.GetString(14),
                    rows.GetInt64(17)));
                continue;
            }

            var origin = new LabelOrigin(
                rows.GetString(3),
                Enum.Parse<LabelSource>(rows.GetString(8)),
                Enum.Parse<LabelSource>(rows.GetString(9)),
                rows.GetString(10),
                rows.GetString(11),
                JsonSerializer.Deserialize<OperatorProfile>(rows.GetString(12))
                    ?? throw new InvalidDataException("A label has no operator."),
                Time(rows.GetString(13)),
                rows.IsDBNull(14) ? null : rows.GetString(14),
                rows.IsDBNull(15) ? null : rows.GetString(15));
            all.Add(new LabelEntry(
                rows.GetInt64(0),
                new LabelSubject(
                    Enum.Parse<LabelSubjectKind>(rows.GetString(4)), BitcoinAddress.Parse(rows.GetString(5))),
                Enum.Parse<LabelKind>(rows.GetString(6)),
                rows.IsDBNull(7) ? null : rows.GetString(7),
                origin,
                rows.GetInt64(16) != 0,
                rows.GetInt64(17)));
        }

        return new StoredLabels(all, retractions);
    }

    /// <summary>Gives the identity of a label across cases: its origin and what it says.</summary>
    /// <param name="originId">The origin.</param>
    /// <param name="subject">What it is on.</param>
    /// <param name="kind">What it says.</param>
    /// <param name="name">The name.</param>
    /// <returns>The key.</returns>
    public static LabelKey Key(string originId, LabelSubject subject, LabelKind kind, string? name) =>
        new(originId, subject.Kind, subject.Address.Value, kind, NameKey(name));

    /// <summary>Gives a name as it is compared: trimmed and in lower case, or empty.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The compared form.</returns>
    public static string NameKey(string? name) => name?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>Writes a time as stored.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text, in UTC.</returns>
    public static string Text(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Reads a stored time.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The time.</returns>
    public static DateTimeOffset Time(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

/// <summary>The labels in a case.</summary>
/// <param name="All">Every label, also those taken back, oldest first.</param>
/// <param name="Retractions">The taking back of each label that is taken back, by the label's sequence.</param>
internal sealed record StoredLabels(
    IReadOnlyList<LabelEntry> All, IReadOnlyDictionary<long, LabelRetraction> Retractions)
{
    /// <summary>Gets the sequences of the labels that are taken back.</summary>
    public IReadOnlySet<long> Retracted { get; } = Retractions.Keys.ToHashSet();

    /// <summary>Gets the labels that are not taken back, oldest first.</summary>
    public IReadOnlyList<LabelEntry> Active { get; } =
        [.. All.Where(label => !Retractions.ContainsKey(label.Sequence))];

    /// <summary>Gets the identities of every label, also those taken back, with how the case got them.</summary>
    private HashSet<(LabelKey Key, LabelSource Source)> Keys { get; } =
    [
        .. All.Select(label =>
            (LabelStore.Key(label.Origin.OriginId, label.Subject, label.Kind, label.Name), label.Origin.Source)),
    ];

    /// <summary>Gets the identities of every label, also those taken back, however the case got them.</summary>
    private HashSet<LabelKey> AnyWay => field ??= [.. Keys.Select(known => known.Key)];

    /// <summary>Says whether the case has, or has had, a label.</summary>
    /// <param name="key">The label's identity.</param>
    /// <returns><see langword="true"/> when it has, also when it is taken back.</returns>
    public bool Has(LabelKey key) => AnyWay.Contains(key);

    /// <summary>Says whether the case has, or has had, a label that it got in a particular way.</summary>
    /// <param name="key">The label's identity.</param>
    /// <param name="source">How the case got it.</param>
    /// <returns><see langword="true"/> when it has, also when it is taken back.</returns>
    public bool Has(LabelKey key, LabelSource source) => Keys.Contains((key, source));
}

/// <summary>The identity of a label across cases.</summary>
/// <param name="OriginId">The origin.</param>
/// <param name="SubjectKind">Address or cluster.</param>
/// <param name="Address">The address.</param>
/// <param name="Kind">What it says.</param>
/// <param name="Name">The name as compared.</param>
internal sealed record LabelKey(
    string OriginId, LabelSubjectKind SubjectKind, string Address, LabelKind Kind, string Name);
