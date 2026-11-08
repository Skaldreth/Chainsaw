using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Requests.Contracts;

/// <summary>
/// The parts of a report (sketch RP1b), in the order they come in the file; each can be switched on and off in the
/// view Rapport.
/// </summary>
public enum ReportPart
{
    /// <summary>Two sentences on where the money went.</summary>
    Summary,

    /// <summary>The trail step by step: time, UTC, amount and transaction for every hop.</summary>
    Trail,

    /// <summary>Each step's block height and block hash, so that it can be checked against the chain.</summary>
    Chain,

    /// <summary>Where the money leaves the chain, and whether a request can be made there.</summary>
    CashOut,

    /// <summary>The conclusions, each with <em>Varför?</em>: heuristic or source, version and transaction.</summary>
    Conclusions,

    /// <summary>The labels the report rests on and their sources.</summary>
    Labels,

    /// <summary>The traced amounts in SEK at the rate when the money moved.</summary>
    Sek,

    /// <summary>The top hash of the case's hash chain, checked before the export.</summary>
    Ledger,
}

/// <summary>Where a trail in a report ends, which its heading's tag says (sketch RP1b).</summary>
public enum ReportTrailEnd
{
    /// <summary>The trail goes on from the last actor marked.</summary>
    Open,

    /// <summary>The money left the chain at a cash-out: an exchange, an ATM, OTC or a card.</summary>
    CashOut,

    /// <summary>The end is on a sanction list.</summary>
    Sanctioned,

    /// <summary>The trail ends at a risk: a mixer, a CoinJoin, a darknet market.</summary>
    Risk,

    /// <summary>The money was spent: gambling, a merchant, gift cards.</summary>
    Spent,

    /// <summary>The money goes on on another chain, through a bridge or a swap.</summary>
    OtherChain,
}

/// <summary>
/// One transfer along a step of a trail: a fact from the chain (principle 2), written whole so that it can be checked
/// against the chain with any tool by its transaction, height and hash (principle 1).
/// </summary>
/// <param name="From">Who sent it, as the report may name them: a service's name or a shortened address.</param>
/// <param name="To">Who received it, named the same way.</param>
/// <param name="Transaction">The transaction's id, written out in full.</param>
/// <param name="Amount">The amount moved in the transaction along the step.</param>
/// <param name="Time">The block's time, or <see langword="null"/> while unconfirmed.</param>
/// <param name="BlockHeight">The block's height, or <see langword="null"/> while unconfirmed.</param>
/// <param name="BlockHash">The block's hash, or <see langword="null"/> when the case does not have it.</param>
public sealed record ReportStep(
    string From,
    string To,
    string Transaction,
    AssetAmount Amount,
    DateTimeOffset? Time,
    long? BlockHeight,
    string? BlockHash)
{
    /// <summary>Gets the transaction's id.</summary>
    public string Transaction { get; } = string.IsNullOrWhiteSpace(Transaction)
        ? throw new ArgumentException("A step names its transaction.", nameof(Transaction))
        : Transaction;
}

/// <summary>
/// A trail in a report: from an actor marked in the graph to where the money leaves the chain, or to where the trail
/// ends, along traced money. The traced amount is a conclusion by the method (principle 2).
/// </summary>
/// <param name="From">The start, as the report may name it.</param>
/// <param name="To">The end, as the report may name it.</param>
/// <param name="End">What the end is.</param>
/// <param name="Steps">The transfers along the trail, in its order; at least one.</param>
/// <param name="Traced">How much of the traced money reached the end, or <see langword="null"/> when unknown.</param>
public sealed record ReportTrail(
    string From, string To, ReportTrailEnd End, IReadOnlyList<ReportStep> Steps, AssetAmount? Traced)
{
    /// <summary>Gets the transfers along the trail.</summary>
    public IReadOnlyList<ReportStep> Steps { get; } = Steps is { Count: > 0 }
        ? Steps
        : throw new ArgumentException("A trail has at least one step.", nameof(Steps));

    /// <summary>
    /// Gets what the traced amount was worth when it moved, for example <c>≈ 263 100 kr</c>, by the rates in the case;
    /// <see langword="null"/> when the case has no rate for it, which the report then does not make up.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>Gets the time the traced money reached the end: the last step's.</summary>
    public DateTimeOffset? Reached => Steps[^1].Time;
}

/// <summary>
/// A conclusion in a report: a statement Chainsaw or the user drew, never a fact, with what it rests on in plain
/// language (principles 2 and 3).
/// </summary>
/// <param name="Statement">The statement, for example <c>Klustret bc1q…x0wl är en plånbok med 14 adresser.</c></param>
/// <param name="Why">What it rests on: the heuristic or source, its version and the transactions.</param>
public sealed record ReportConclusion(string Statement, string Why)
{
    /// <summary>Gets the statement.</summary>
    public string Statement { get; } = string.IsNullOrWhiteSpace(Statement)
        ? throw new ArgumentException("A conclusion says something.", nameof(Statement))
        : Statement;

    /// <summary>Gets what it rests on.</summary>
    public string Why { get; } = string.IsNullOrWhiteSpace(Why)
        ? throw new ArgumentException("A conclusion says why.", nameof(Why))
        : Why;
}

/// <summary>A label a report rests on, with its source; never a victim's label, a reason or a note.</summary>
/// <param name="Text">What it says, for example <c>Börs · Börs A</c>.</param>
/// <param name="Source">Where it comes from, for example <c>OFAC SDN (2026-11-02)</c>.</param>
public sealed record ReportLabel(string Text, string Source);

/// <summary>
/// The top of the case's hash chain as checked: the journal's last sealed line and its hash, and whether the whole
/// case was found unchanged (what <c>/verify</c> runs).
/// </summary>
/// <param name="Line">The journal's last sealed line that was found unbroken.</param>
/// <param name="Hash">That line's hash in lower-case hex, or <see langword="null"/> when no line is unbroken.</param>
/// <param name="CheckedUtc">When it was checked.</param>
/// <param name="IsIntact">Whether every chain in the case was found unbroken.</param>
public sealed record ReportLedger(long Line, string? Hash, DateTimeOffset CheckedUtc, bool IsIntact);

/// <summary>
/// What a report is made from, built in the view Rapport out of what is marked in the graph (sketch RP1b): only the
/// trails, conclusions and labels, never a victim's name, the case's title or a note (principle 4). The case number,
/// the operator and the time are added when it is composed.
/// </summary>
/// <param name="Trails">The trails, in the graph's order.</param>
/// <param name="Conclusions">The conclusions with why.</param>
/// <param name="Labels">The labels and their sources.</param>
/// <param name="Method">The tracing method the traced amounts were counted by, for example <c>FIFO v1</c>.</param>
/// <param name="Parts">The parts switched on.</param>
/// <param name="TimeZone">The time zone the local times are written in.</param>
public sealed record ReportDraft(
    IReadOnlyList<ReportTrail> Trails,
    IReadOnlyList<ReportConclusion> Conclusions,
    IReadOnlyList<ReportLabel> Labels,
    string Method,
    IReadOnlySet<ReportPart> Parts,
    TimeZoneInfo TimeZone)
{
    /// <summary>Gets the trails.</summary>
    public IReadOnlyList<ReportTrail> Trails { get; } = Trails ?? throw new ArgumentNullException(nameof(Trails));

    /// <summary>Gets the conclusions.</summary>
    public IReadOnlyList<ReportConclusion> Conclusions { get; } =
        Conclusions ?? throw new ArgumentNullException(nameof(Conclusions));

    /// <summary>Gets the labels.</summary>
    public IReadOnlyList<ReportLabel> Labels { get; } = Labels ?? throw new ArgumentNullException(nameof(Labels));

    /// <summary>Gets the parts switched on.</summary>
    public IReadOnlySet<ReportPart> Parts { get; } = Parts ?? throw new ArgumentNullException(nameof(Parts));

    /// <summary>Gets the time zone.</summary>
    public TimeZoneInfo TimeZone { get; } = TimeZone ?? throw new ArgumentNullException(nameof(TimeZone));

    /// <summary>
    /// Gets the top of the hash chain as last checked, for the preview; an export checks it anew. <see langword="null"/>
    /// while it has not been checked, which the report says honestly.
    /// </summary>
    public ReportLedger? Ledger { get; init; }

    /// <summary>
    /// Gets when the report is made, the time its preview showed, so that the file says what the user saw; when
    /// <see langword="null"/>, it is made now.
    /// </summary>
    public DateTimeOffset? MadeUtc { get; init; }

    /// <summary>Says whether a part is switched on.</summary>
    /// <param name="part">The part.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Has(ReportPart part) => Parts.Contains(part);

    /// <summary>Gets every transaction the trails rest on, each once, in the trails' order.</summary>
    public IReadOnlyList<string> Transactions =>
        [.. Trails.SelectMany(trail => trail.Steps).Select(step => step.Transaction).Distinct(StringComparer.Ordinal)];
}
