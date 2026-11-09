using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// A replacement of a case's file key (sketch VT1a), as its line in the journal tells it: when, which line, which
/// envelopes fell away and whether a new case password was made in the same step.
/// </summary>
/// <param name="Sequence">The journal line's sequence number.</param>
/// <param name="Utc">When the key was replaced.</param>
/// <param name="Dropped">The envelopes that fell away, in words (a colleague's name, <em>ärendelösenordet</em> …).</param>
/// <param name="SharingPasswordCreated">Whether a new case password was made with the new key.</param>
public sealed record FileKeyReplacement(
    long Sequence, DateTimeOffset Utc, IReadOnlyList<string> Dropped, bool SharingPasswordCreated)
{
    /// <summary>
    /// Tells whether an envelope is wrapped anew when the file key is replaced: this vault's and the one for the
    /// recovery key built into Chainsaw. Every other envelope (another vault's, the case password, another or revoked
    /// recovery key, one of an unknown kind) holds a key that Chainsaw cannot wrap the new file key for, and falls away.
    /// </summary>
    /// <param name="envelope">The envelope, as <see cref="ICase.DescribeEnvelopes"/> gave it.</param>
    /// <returns><see langword="true"/> when it is wrapped anew.</returns>
    public static bool Keeps(CaseEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.Kind == CaseEnvelopeKind.Yours
            || (envelope.Kind == CaseEnvelopeKind.Recovery && envelope.Recovery == RecoveryEnvelopeKey.BuiltIn);
    }

    /// <summary>Reads a replacement from its journal line.</summary>
    /// <param name="entry">A journal line.</param>
    /// <returns>
    /// The replacement, or <see langword="null"/> when the line is of another kind or its details do not read.
    /// </returns>
    public static FileKeyReplacement? FromJournal(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != CaseJournalKinds.FileKeyReplaced || entry.Details is not { } details)
        {
            return null;
        }

        try
        {
            var read = JsonSerializer.Deserialize<FileKeyReplacementDetails>(details);
            return read is null
                ? null
                : new FileKeyReplacement(entry.Sequence, entry.Utc, read.Dropped ?? [], read.SharingPasswordCreated);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The details of the journal line <see cref="CaseJournalKinds.FileKeyReplaced"/>, as JSON.</summary>
/// <param name="Dropped">The envelopes that fell away, in words.</param>
/// <param name="Kept">The envelopes that were wrapped anew, in words.</param>
/// <param name="SharingPasswordCreated">Whether a new case password was made.</param>
public sealed record FileKeyReplacementDetails(
    [property: JsonPropertyName("dropped")] IReadOnlyList<string>? Dropped,
    [property: JsonPropertyName("kept")] IReadOnlyList<string>? Kept,
    [property: JsonPropertyName("sharingPasswordCreated")] bool SharingPasswordCreated);
