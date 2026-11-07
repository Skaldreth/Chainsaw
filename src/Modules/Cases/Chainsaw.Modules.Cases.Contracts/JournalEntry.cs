using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>One sealed entry in a case's journal.</summary>
/// <param name="Sequence">The entry's place in the journal, from 1 without gaps.</param>
/// <param name="Utc">When it happened, in UTC.</param>
/// <param name="Local">When it happened, in the local time of the computer that recorded it, with its offset.</param>
/// <param name="TimeZone">The identifier of that computer's time zone.</param>
/// <param name="Operator">Who did it, from the operator profile in the vault at the time.</param>
/// <param name="Kind">The stable code for what happened.</param>
/// <param name="Summary">One line in plain language.</param>
/// <param name="Details">Optional details.</param>
/// <param name="Hash">The entry's SHA-256 in the hash chain, in lower-case hex.</param>
public sealed record JournalEntry(
    long Sequence,
    DateTimeOffset Utc,
    DateTimeOffset Local,
    string TimeZone,
    OperatorProfile Operator,
    string Kind,
    string Summary,
    string? Details,
    string Hash);
