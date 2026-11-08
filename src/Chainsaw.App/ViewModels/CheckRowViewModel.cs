using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>Where a row stands (sketch KF2b).</summary>
public enum CheckRowGroup
{
    /// <summary><em>Att avgöra</em>: a suggestion that waits for a decision.</summary>
    ToDecide,

    /// <summary><em>Gjort av sig självt</em>: a check that did its work by itself.</summary>
    Done,

    /// <summary><em>Avgjort</em>: a suggestion the user has settled.</summary>
    Decided,
}

/// <summary>
/// A chip under the suggestion that says what it rests on (sketch KF2b), with the longer text on hover.
/// </summary>
/// <param name="Icon">The chip's icon's resource key.</param>
/// <param name="Label">The short label.</param>
/// <param name="Tip">What it means.</param>
public sealed record BasisChip(string Icon, string Label, string Tip);

/// <summary>A band in the small picture of how the money flows: from the money in to one candidate.</summary>
/// <param name="Rank">The candidate's rank, from 1.</param>
/// <param name="Width">The band's width in pixels: the score.</param>
/// <param name="AmountText">The candidate's amount, for example <c>0,7920</c>.</param>
/// <param name="IsChosen">Whether it is the candidate Enter approves.</param>
public sealed record FlowBand(int Rank, double Width, string AmountText, bool IsChosen);

/// <summary>
/// The small picture of how the money flows (sketch KF2b): the money in on the left, the candidates on the right.
/// </summary>
/// <param name="InText">What came in, for example <c>0,8000 in</c>; empty when it is not known.</param>
/// <param name="FeeText">The chosen candidate's fee, for example <c>−1 % trolig avgift</c>;
/// empty when it is not known. A judgement: it holds only if the candidate is right.</param>
/// <param name="Bands">One band per candidate, best first.</param>
public sealed record SuggestionFlow(string InText, string FeeText, IReadOnlyList<FlowBand> Bands)
{
    /// <summary>Gets an empty picture, for a suggestion without candidates.</summary>
    public static SuggestionFlow None { get; } = new(string.Empty, string.Empty, []);

    /// <summary>Gets whether there is anything to draw.</summary>
    public bool HasBands => Bands.Count > 0;
}

/// <summary>What an analysis's suggestions say beside the finding itself: the chips and the table's words.</summary>
/// <param name="Chips">The basis as chips.</param>
/// <param name="CandidateNoun">What one candidate is called, for the toast, for example <c>Trolig utgång</c>.</param>
/// <param name="FirstColumn">The table's first column's heading.</param>
/// <param name="TimeColumn">The table's time column's heading.</param>
/// <param name="CandidatesAreParts">
/// Whether the candidates are parts of one combination (<c>mixer-parts</c>), so that the fee is the deposit against
/// all the parts that are not dismissed, not against the chosen one alone.
/// </param>
public sealed record SuggestionWords(
    IReadOnlyList<BasisChip> Chips,
    string CandidateNoun,
    string FirstColumn,
    string TimeColumn,
    bool CandidatesAreParts = false)
{
    /// <summary>The words of an analysis this table does not know: no chips, plain headings.</summary>
    public static SuggestionWords Plain { get; } = new([], "Kandidaten", "ADRESS", "TID");

    /// <summary>The words of the mixer's likely outputs (<c>mixer-outputs</c>).</summary>
    public static SuggestionWords MixerOutputs { get; } = new(
        [
            new("Icon.Amounts", "Belopp − avgift", "Utbetalningen jämförs med insättningen minus mixerns avgift"),
            new("Icon.Clock", "Tid", "Ju kortare tid efter insättningen, desto högre poäng"),
            new("Icon.Layers", "Adresstyp", "Samma adresstyp som insättarens väger in"),
        ],
        "Trolig utgång",
        "ADRESS",
        "TID");

    /// <summary>The words of a deposit paid out in parts (<c>mixer-parts</c>).</summary>
    public static SuggestionWords MixerParts { get; } = new(
        [
            new("Icon.Split", "Delarna tillsammans", "Två till fyra utbetalningar som tillsammans är insättningen "
                + "minus mixerns avgift"),
            new("Icon.Clock", "Tid", "Ju kortare tid efter insättningen, desto högre poäng"),
            new("Icon.Block", "Spridda block", "Delar i olika block väger mer än delar i samma tx"),
            new("Icon.Layers", "Adresstyp", "Samma adresstyp som insättarens väger in"),
        ],
        "Trolig del",
        "ADRESS",
        "TID",
        CandidatesAreParts: true);

    /// <summary>The words of mixer after mixer (<c>mixer-chain</c>).</summary>
    public static SuggestionWords MixerChain { get; } = new(
        [
            new("Icon.Link", "Mixer efter mixer", "En utgång ur en mixer går in i en mixer eller en CoinJoin igen"),
            new("Icon.Check", "Godkänd före trolig", "Kedjan följer de utgångar du godkänt, annars de troligaste"),
            new("Icon.Amounts", "Avgifter längs kedjan", "Beloppet in och avgifterna i varje mixer summeras"),
        ],
        "Steg",
        "ADRESS",
        "TID");

    /// <summary>Gives an analysis's words.</summary>
    /// <param name="analysis">The analysis's name.</param>
    /// <returns>Its words, or <see cref="Plain"/>.</returns>
    public static SuggestionWords Of(string analysis) => analysis switch
    {
        "mixer-outputs" => MixerOutputs,
        "mixer-parts" => MixerParts,
        "mixer-chain" => MixerChain,
        _ => Plain,
    };
}

/// <summary>
/// A candidate in the ranked table (sketch KF2b): its rank, address, amount, time after the money came in, score as a
/// bar and the user's decision about it.
/// </summary>
public sealed partial class CandidateRowViewModel : ObservableObject
{
    /// <summary>The widest bar, for a score of 1.</summary>
    public const double FullBar = 28;

    /// <summary>The characters the shortened address takes, as the table writes it (<c>bc1q…7f2k</c>).</summary>
    public const int AddressRoom = SubjectText.Shortest;

    /// <summary>The Swedish culture the numbers are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Creates the row.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="rank">Its rank, from 1.</param>
    /// <param name="decision">The user's decision in force about it, or <see langword="null"/>.</param>
    /// <param name="after">How long after the money came in it was paid, or <see langword="null"/> if not
    /// known.</param>
    public CandidateRowViewModel(CheckCandidate candidate, int rank, CheckDecision? decision, TimeSpan? after)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
        Rank = rank;
        Verdict = decision?.Verdict;
        TimeText = after is { } span ? Span(span) : string.Empty;
    }

    /// <summary>Gets the candidate.</summary>
    public CheckCandidate Candidate { get; }

    /// <summary>Gets its rank, from 1.</summary>
    public int Rank { get; }

    /// <summary>Gets the rank as written in its ring.</summary>
    public string RankText => Rank.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the user's decision, or <see langword="null"/> while it waits.</summary>
    public CheckVerdict? Verdict { get; }

    /// <summary>Gets whether it still waits for a decision.</summary>
    public bool IsWaiting => Verdict is null;

    /// <summary>Gets whether the user approved it.</summary>
    public bool IsApproved => Verdict == CheckVerdict.Approved;

    /// <summary>Gets whether the user dismissed it.</summary>
    public bool IsDismissed => Verdict == CheckVerdict.Dismissed;

    /// <summary>Gets the score as written, for example <c>0,82</c>.</summary>
    public string ScoreText => Candidate.Score.ToString("0.00", Swedish);

    /// <summary>Gets the score's bar width.</summary>
    public double BarWidth => Math.Round(FullBar * Math.Clamp(Candidate.Score, 0, 1));

    /// <summary>Gets the address, shortened in the middle.</summary>
    public string ShortAddress => SubjectText.Fit(Candidate.Address.Value, AddressRoom);

    /// <summary>Gets the amount, for example <c>0,7920</c>.</summary>
    public string AmountText => WriteAmount(Candidate.Amount);

    /// <summary>Gets how long after the money came in it was paid, for example <c>4 h</c>; empty if unknown.</summary>
    public string TimeText { get; }

    /// <summary>Gets whether it is the candidate Enter approves.</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    /// <summary>Writes an amount in bitcoin without the unit.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>For example <c>0,7920</c>.</returns>
    public static string WriteAmount(Satoshi amount) => Graph.AmountText.Btc(amount);

    /// <summary>Writes a time span as short as the table has room for: minutes, hours or days.</summary>
    /// <param name="span">The span.</param>
    /// <returns>For example <c>45 min</c>, <c>9 h</c> or <c>3 d</c>.</returns>
    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.FromHours(1))
        {
            return Whole(Math.Max(0, span.TotalMinutes)) + " min";
        }

        return span < TimeSpan.FromHours(48) ? Whole(span.TotalHours) + " h" : Whole(span.TotalDays) + " d";
    }

    /// <summary>Writes a number rounded to a whole.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The whole number.</returns>
    private static string Whole(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}

/// <summary>
/// A check or suggestion (sketch KF2b): its row in the overview and, for a suggestion, everything the panel shows of
/// it one at a time: the title and the line under it, the flow, the chips, the ranked candidates, the analysis with
/// its version and what Enter and X decide.
/// </summary>
public sealed partial class CheckRowViewModel : ObservableObject
{
    /// <summary>The thinnest band in the flow, for a score of 0.</summary>
    public const double ThinnestBand = 3;

    /// <summary>How much wider a band of score 1 is than the thinnest.</summary>
    public const double BandGrowth = 14;

    /// <summary>What a suggestion approved is written as in the log.</summary>
    public const string ApprovedText = "Godkänd";

    /// <summary>What a suggestion dismissed is written as in the log.</summary>
    public const string DismissedText = "Avfärdad";

    /// <summary>What a check that marked the graph by itself is written as in the log.</summary>
    public const string MarkedText = "Markerad";

    /// <summary>The Swedish culture dates are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>What came in to the suggestion, when the graph knows it.</summary>
    private readonly Satoshi? _incoming;

    /// <summary>Creates the row.</summary>
    /// <param name="check">The check or suggestion.</param>
    /// <param name="anchor">The name of the actor it starts from, when that actor has a name; otherwise null.</param>
    /// <param name="nodes">The actors its area is drawn around.</param>
    /// <param name="incoming">What came in, as the graph knows it, or <see langword="null"/>.</param>
    /// <param name="zone">The time zone the date is written in; the computer's when null.</param>
    public CheckRowViewModel(
        StoredCheck check,
        string? anchor,
        IReadOnlyList<string> nodes,
        Satoshi? incoming = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        Check = check;
        Nodes = nodes;
        Anchor = anchor;
        _incoming = incoming;
        Key = GraphAreas.KeyOf(check);
        Look = AreaLooks.OfAnalysis(check.Analysis.Name, check.Finding.Area);
        Words = SuggestionWords.Of(check.Analysis.Name);
        Summary = anchor is null ? check.Finding.Summary : $"{anchor} · {check.Finding.Summary}";
        var start = check.Finding.Basis.FirstOrDefault()?.Block?.Time;
        Candidates =
        [
            .. check.Finding.Candidates.Select((candidate, index) => new CandidateRowViewModel(
                candidate, index + 1, check.DecisionFor(candidate.Key), After(check, candidate, start))),
        ];
        Group = Classify(check);
        Meta = MetaOf(check, anchor, incoming, start, zone ?? TimeZoneInfo.Local);
        var first = Candidates.FirstOrDefault(c => c.IsWaiting);
        if (first is not null)
        {
            first.IsChosen = true;
        }
    }

    /// <summary>Gets the check.</summary>
    public StoredCheck Check { get; }

    /// <summary>Gets the row's key, which its area shares.</summary>
    public string Key { get; }

    /// <summary>Gets the actors its area is drawn around.</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>Gets the name of the actor it starts from, or <see langword="null"/>.</summary>
    public string? Anchor { get; }

    /// <summary>Gets the icon and tint.</summary>
    public AreaLook Look { get; }

    /// <summary>Gets the analysis's chips and headings.</summary>
    public SuggestionWords Words { get; }

    /// <summary>Gets the group it stands in.</summary>
    public CheckRowGroup Group { get; }

    /// <summary>Gets the title.</summary>
    public string Title => Check.Finding.Title;

    /// <summary>Gets the anchor and the finding's summary, for the inspector and the Guide.</summary>
    public string Summary { get; }

    /// <summary>
    /// Gets the overview's short muted word after the title: the actor it starts from, else the finding's summary.
    /// </summary>
    public string Short => Anchor ?? Check.Finding.Summary;

    /// <summary>
    /// Gets the line under the title in the panel: the actor, what came in and when, for example
    /// <c>Blandaren · 0,8000 BTC in · 4 nov 21:14</c>.
    /// </summary>
    public string Meta { get; }

    /// <summary>Gets the chips that say what the suggestion rests on.</summary>
    public IReadOnlyList<BasisChip> Basis => Words.Chips;

    /// <summary>Gets whether it has chips.</summary>
    public bool HasBasis => Basis.Count > 0;

    /// <summary>Gets the analysis with its version, for example <c>Mixerutgångar · v1</c>.</summary>
    public string AnalysisText =>
        $"{Check.AnalysisName} · v{Check.Analysis.Version.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Gets how many candidates wait, as the overview's count; empty when it has none or is settled.</summary>
    public string CountText => Group == CheckRowGroup.ToDecide && Candidates.Count > 0
        ? Candidates.Count(c => c.IsWaiting).ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    /// <summary>Gets whether it is a suggestion (dashed frame).</summary>
    public bool IsSuggestion => Check.Finding.Kind == CheckKind.Suggestion;

    /// <summary>Gets whether Enter and X decide something in it now.</summary>
    public bool CanDecide => Group == CheckRowGroup.ToDecide;

    /// <summary>Gets the candidates, best first; empty when it has none.</summary>
    public IReadOnlyList<CandidateRowViewModel> Candidates { get; }

    /// <summary>Gets whether it has candidates, so that the flow and the table are shown.</summary>
    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>Gets the candidate Enter approves, or <see langword="null"/>.</summary>
    public CandidateRowViewModel? ChosenCandidate => Candidates.FirstOrDefault(c => c.IsChosen);

    /// <summary>Gets what Enter does, as its button says it: <c>Godkänn 1</c> for the first candidate.</summary>
    public string GoText => ChosenCandidate is { } chosen ? "Godkänn " + chosen.RankText : "Godkänn";

    /// <summary>Gets the small picture of how the money flows, with the chosen candidate in accent.</summary>
    public SuggestionFlow Flow => FlowOf();

    /// <summary>Gets whether the user approved it (one of its candidates, or the whole).</summary>
    public bool IsApproved =>
        Check.DecisionFor(null)?.Verdict == CheckVerdict.Approved || Candidates.Any(c => c.IsApproved);

    /// <summary>Gets what the log of decisions writes for it: approved, dismissed or marked.</summary>
    public string StateText => Group switch
    {
        CheckRowGroup.Decided when IsApproved => ApprovedText,
        CheckRowGroup.Decided => DismissedText,
        _ => MarkedText,
    };

    /// <summary>Gets whether the log offers <em>Ångra</em> on it: only on what the user decided.</summary>
    public bool CanUndo => Group == CheckRowGroup.Decided;

    /// <summary>Gets or sets the optional reason the user writes before deciding.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    /// <summary>Gets whether it is the chosen row, which shows its area.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Chooses the candidate Enter approves; one that is decided cannot be chosen.</summary>
    /// <param name="candidate">The candidate.</param>
    public void Choose(CandidateRowViewModel candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsWaiting || !Candidates.Contains(candidate))
        {
            return;
        }

        foreach (var row in Candidates)
        {
            row.IsChosen = row == candidate;
        }

        OnPropertyChanged(nameof(ChosenCandidate));
        OnPropertyChanged(nameof(GoText));
        OnPropertyChanged(nameof(Flow));
    }

    /// <summary>
    /// Gives the decision Enter makes: approve the chosen candidate, or the whole when it has none; null when the row
    /// has nothing to decide.
    /// </summary>
    /// <returns>The request, or <see langword="null"/>.</returns>
    public DecisionRequest? ApproveRequest()
    {
        if (!CanDecide || (HasCandidates && ChosenCandidate is null))
        {
            return null;
        }

        return new DecisionRequest(
            Check.Analysis, Check.Finding.Key, ChosenCandidate?.Candidate.Key, CheckVerdict.Approved, ReasonText());
    }

    /// <summary>
    /// Gives the decisions X makes: dismiss the suggestion as a whole, or, once a candidate is approved, every
    /// candidate that still waits (<em>Avfärda resten</em>); empty when nothing is to decide.
    /// </summary>
    /// <returns>The requests.</returns>
    public IReadOnlyList<DecisionRequest> DismissRequests()
    {
        if (!CanDecide)
        {
            return [];
        }

        if (!HasApprovedCandidate)
        {
            return [new DecisionRequest(Check.Analysis, Check.Finding.Key, null, CheckVerdict.Dismissed, ReasonText())];
        }

        return
        [
            .. Candidates.Where(c => c.IsWaiting).Select(c => new DecisionRequest(
                Check.Analysis, Check.Finding.Key, c.Candidate.Key, CheckVerdict.Dismissed, ReasonText())),
        ];
    }

    /// <summary>Gets whether one of the candidates is approved, so that X dismisses only the rest.</summary>
    public bool HasApprovedCandidate => Candidates.Any(c => c.IsApproved);

    /// <summary>Gets what the dismiss button says: <c>Avfärda</c>, or <c>Avfärda resten</c> after an approval.</summary>
    public string DismissText => HasApprovedCandidate ? "Avfärda resten" : "Avfärda";

    /// <summary>Gets the dismiss button's tooltip.</summary>
    public string DismissTip => HasApprovedCandidate
        ? "Avfärda kandidaterna som inte är avgjorda · X"
        : "Avfärda förslaget · X";

    /// <summary>
    /// Gives the decisions <em>Ångra</em> takes back in the log: every decision in force about it, so that it waits
    /// again.
    /// </summary>
    /// <returns>The decisions in force; empty for a check.</returns>
    public IReadOnlyList<CheckDecision> InForce()
    {
        if (!IsSuggestion)
        {
            return [];
        }

        IEnumerable<string?> keys = [null, .. Check.Finding.Candidates.Select(c => c.Key)];
        return [.. keys.Select(key => Check.DecisionFor(key)).OfType<CheckDecision>()];
    }

    /// <summary>Gives the group a check stands in.</summary>
    /// <param name="check">The check.</param>
    /// <returns>The group.</returns>
    public static CheckRowGroup Classify(StoredCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Finding.Kind == CheckKind.Check)
        {
            return CheckRowGroup.Done;
        }

        return check.IsSettled ? CheckRowGroup.Decided : CheckRowGroup.ToDecide;
    }

    /// <summary>Gives the reason as stored: trimmed, or null when none is written.</summary>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    private string? ReasonText() => string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();

    /// <summary>Works out the flow: what came in, the chosen candidate's fee and a band per candidate.</summary>
    /// <returns>The flow.</returns>
    private SuggestionFlow FlowOf()
    {
        if (!HasCandidates)
        {
            return SuggestionFlow.None;
        }

        var bands = Candidates.Select(c => new FlowBand(
            c.Rank,
            ThinnestBand + (BandGrowth * Math.Clamp(c.Candidate.Score, 0, 1)),
            c.AmountText,
            c.IsChosen));
        var inText = _incoming is { } amount ? CandidateRowViewModel.WriteAmount(amount) + " in" : string.Empty;
        return new SuggestionFlow(inText, FeeText(), [.. bands]);
    }

    /// <summary>
    /// Writes the chosen candidate's fee against what came in, for example <c>−1 % trolig avgift</c>, or, when the
    /// candidates are parts, the fee of all the parts not dismissed together (<c>−2 % trolig avgift, 3 delar</c>):
    /// only if the candidates are right, so it says <em>trolig</em> (principle 2).
    /// </summary>
    /// <returns>The fee, or an empty text when what came in is not known or nothing is chosen.</returns>
    private string FeeText()
    {
        if (_incoming is not { Value: > 0 } amount || ChosenCandidate is not { } chosen)
        {
            return string.Empty;
        }

        if (!Words.CandidatesAreParts)
        {
            return FeeWords(amount, chosen.Candidate.Amount.Value);
        }

        var parts = Candidates.Where(c => !c.IsDismissed).ToList();
        var count = parts.Count.ToString(CultureInfo.InvariantCulture);
        return FeeWords(amount, parts.Sum(c => c.Candidate.Amount.Value)) + ", "
            + (parts.Count == 1 ? count + " del" : count + " delar");
    }

    /// <summary>Writes a fee as the share of what came in that did not come out.</summary>
    /// <param name="incoming">What came in.</param>
    /// <param name="paid">What came out, in satoshi.</param>
    /// <returns>For example <c>−1 % trolig avgift</c>.</returns>
    private static string FeeWords(Satoshi incoming, long paid)
    {
        var fee = (double)(incoming.Value - paid) / incoming.Value;
        return "−" + Math.Round(fee * 100, 1).ToString("0.#", Swedish) + " % trolig avgift";
    }

    /// <summary>Works out how long after the money came in a candidate was paid, from the blocks it rests on.</summary>
    /// <param name="check">The suggestion.</param>
    /// <param name="candidate">The candidate.</param>
    /// <param name="start">When the money came in, or <see langword="null"/>.</param>
    /// <returns>The span, or <see langword="null"/> when a block is not known.</returns>
    private static TimeSpan? After(StoredCheck check, CheckCandidate candidate, DateTimeOffset? start)
    {
        var paid = check.Finding.Basis.FirstOrDefault(basis => basis.Txid == candidate.Txid)?.Block?.Time;
        return start is { } from && paid is { } to ? to - from : null;
    }

    /// <summary>Writes the line under the title: the actor, what came in and when.</summary>
    /// <param name="check">The suggestion.</param>
    /// <param name="anchor">The actor's name, or null.</param>
    /// <param name="incoming">What came in, or null.</param>
    /// <param name="start">When it came in, or null.</param>
    /// <param name="zone">The time zone the date is written in.</param>
    /// <returns>The parts that are known, joined; the finding's summary when none is.</returns>
    private static string MetaOf(
        StoredCheck check, string? anchor, Satoshi? incoming, DateTimeOffset? start, TimeZoneInfo zone)
    {
        var parts = new List<string>();
        if (anchor is not null)
        {
            parts.Add(anchor);
        }

        if (incoming is { } amount)
        {
            parts.Add(CandidateRowViewModel.WriteAmount(amount) + " BTC in");
        }

        if (start is { } time)
        {
            parts.Add(TimeZoneInfo.ConvertTime(time, zone).ToString("d MMM HH:mm", Swedish).Replace(".", string.Empty,
                StringComparison.Ordinal));
        }

        return parts.Count == 0 ? check.Finding.Summary : string.Join(" · ", parts);
    }
}
