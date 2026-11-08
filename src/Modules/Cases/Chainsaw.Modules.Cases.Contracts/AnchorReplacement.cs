using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// What a case did not match when it was opened, as the journal line <see cref="CaseJournalKinds.AnchorsReplaced"/>
/// tells it (sketch AK1a).
/// </summary>
public enum AnchorReplacementProblem
{
    /// <summary>The case no longer reached what this computer saved last: an older copy, or changed elsewhere.</summary>
    Diverged,

    /// <summary>This computer's anchors for the case could not be read.</summary>
    Unreadable,

    /// <summary>This computer had no anchors for the case although its vault had saved it before.</summary>
    Missing,

    /// <summary>The anchor file was older than the last one this vault wrote: it was put back from a copy.</summary>
    RolledBack,

    /// <summary>A problem this build does not know.</summary>
    Unknown,
}

/// <summary>A chain's anchor or head as the journal line gives it.</summary>
/// <param name="Chain">The chain's name, for example <see cref="CaseChains.Journal"/>.</param>
/// <param name="Sequence">The record's sequence number.</param>
/// <param name="Hash">The record's hash in lower-case hex.</param>
public sealed record AnchorPoint(
    [property: JsonPropertyName("chain")] string Chain,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("hash")] string Hash);

/// <summary>
/// A replacement of this computer's anchors, read from its journal line: what was wrong, the anchors the case no
/// longer reached (before) and each chain's head in the case when it was found (after). Nothing in it says why the
/// two differ: that is an assessment, which the user interface makes and shows as one (principle 2).
/// </summary>
/// <param name="Sequence">The journal line's sequence number.</param>
/// <param name="Utc">When it was found.</param>
/// <param name="Problem">What was wrong.</param>
/// <param name="Before">The anchors the case no longer reached; none when the anchors were missing or unreadable.</param>
/// <param name="After">Each chain's head in the case when it was found.</param>
public sealed record AnchorReplacement(
    long Sequence,
    DateTimeOffset Utc,
    AnchorReplacementProblem Problem,
    IReadOnlyList<AnchorPoint> Before,
    IReadOnlyList<AnchorPoint> After)
{
    /// <summary>Reads a replacement from its journal line.</summary>
    /// <param name="entry">A journal line.</param>
    /// <returns>
    /// The replacement, or <see langword="null"/> when the line is of another kind or its details do not read.
    /// </returns>
    public static AnchorReplacement? FromJournal(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != CaseJournalKinds.AnchorsReplaced || entry.Details is not { } details)
        {
            return null;
        }

        try
        {
            var read = JsonSerializer.Deserialize<Details>(details);
            if (read is null)
            {
                return null;
            }

            var problem = Enum.TryParse<AnchorReplacementProblem>(read.Problem, out var known)
                ? known
                : AnchorReplacementProblem.Unknown;
            return new AnchorReplacement(entry.Sequence, entry.Utc, problem, read.Anchors ?? [], read.Heads ?? []);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets whether the cause lies in the case file rather than in this computer: the case no longer reached the anchor
    /// (an older copy, or changed elsewhere), the anchor file was put back from a copy, or the problem is unknown. A
    /// missing or unreadable anchor says nothing about the file.
    /// </summary>
    public bool ConcernsTheFile =>
        Problem is not (AnchorReplacementProblem.Missing or AnchorReplacementProblem.Unreadable);

    /// <summary>Reads the latest replacement of the anchors from a case's journal; off the UI thread.</summary>
    /// <param name="openCase">The case.</param>
    /// <returns>The replacement, or <see langword="null"/> when the anchors were never replaced.</returns>
    public static AnchorReplacement? LatestIn(ICase openCase)
    {
        ArgumentNullException.ThrowIfNull(openCase);
        var entry = openCase.ReadJournal().LastOrDefault(item => item.Kind == CaseJournalKinds.AnchorsReplaced);
        return entry is null ? null : FromJournal(entry);
    }

    /// <summary>Gives a chain's anchor before, or <see langword="null"/>.</summary>
    /// <param name="chain">The chain's name.</param>
    /// <returns>The anchor.</returns>
    public AnchorPoint? BeforeOf(string chain) => Before.FirstOrDefault(point => point.Chain == chain);

    /// <summary>Gives a chain's head after, or <see langword="null"/>.</summary>
    /// <param name="chain">The chain's name.</param>
    /// <returns>The head.</returns>
    public AnchorPoint? AfterOf(string chain) => After.FirstOrDefault(point => point.Chain == chain);

    /// <summary>The details as the Cases module writes them.</summary>
    /// <param name="Problem">The problem's name.</param>
    /// <param name="Anchors">The anchors the case no longer reached.</param>
    /// <param name="Heads">Each chain's head.</param>
    private sealed record Details(
        [property: JsonPropertyName("problem")] string? Problem,
        [property: JsonPropertyName("anchors")] IReadOnlyList<AnchorPoint>? Anchors,
        [property: JsonPropertyName("heads")] IReadOnlyList<AnchorPoint>? Heads);
}
