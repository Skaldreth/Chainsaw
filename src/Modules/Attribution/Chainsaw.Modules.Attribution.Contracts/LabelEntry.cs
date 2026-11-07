namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// One label as it is sealed in the case: what it is on, what it says and where it comes from. It cannot be changed,
/// only taken back with a line in the journal (<see cref="IAttributionService.Retract"/>).
/// </summary>
/// <param name="Sequence">The label's place in the case's sealed chain of labels.</param>
/// <param name="Subject">What it is on.</param>
/// <param name="Kind">What it says the subject is.</param>
/// <param name="Name">
/// The service's or the owner's name, for example an exchange; <see langword="null"/> if none.
/// </param>
/// <param name="Origin">Where it comes from.</param>
/// <param name="DoNotSpread">
/// Whether the label stays in this case: it is never given to the label library and so never reaches another case.
/// </param>
/// <param name="JournalSequence">The journal entry that records it.</param>
public sealed record LabelEntry(
    long Sequence,
    LabelSubject Subject,
    LabelKind Kind,
    string? Name,
    LabelOrigin Origin,
    bool DoNotSpread,
    long JournalSequence);
