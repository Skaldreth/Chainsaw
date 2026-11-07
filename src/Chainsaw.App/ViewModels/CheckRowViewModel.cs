using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Checks.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>Where a row stands in the list (sketch KF1a).</summary>
public enum CheckRowGroup
{
    /// <summary><em>Att avgöra</em>: a suggestion that waits for a decision.</summary>
    ToDecide,

    /// <summary><em>Gjort av sig självt</em>: a check that did its work by itself.</summary>
    Done,

    /// <summary><em>Avgjort</em>: a suggestion the user has decided all of.</summary>
    Decided,
}

/// <summary>A candidate in a row: its score as a bar, its address shortened and the user's decision about it.</summary>
public sealed partial class CandidateRowViewModel : ObservableObject
{
    /// <summary>The widest bar, for a score of 1.</summary>
    public const double FullBar = 34;

    /// <summary>Creates the row.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="decision">The user's decision about it, or <see langword="null"/>.</param>
    public CandidateRowViewModel(CheckCandidate candidate, CheckDecision? decision)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
        Verdict = decision?.Verdict;
    }

    /// <summary>Gets the candidate.</summary>
    public CheckCandidate Candidate { get; }

    /// <summary>Gets the user's decision, or <see langword="null"/> while it waits.</summary>
    public CheckVerdict? Verdict { get; }

    /// <summary>Gets whether it still waits for a decision.</summary>
    public bool IsWaiting => Verdict is null;

    /// <summary>Gets whether the user approved it.</summary>
    public bool IsApproved => Verdict == CheckVerdict.Approved;

    /// <summary>Gets whether the user dismissed it.</summary>
    public bool IsDismissed => Verdict == CheckVerdict.Dismissed;

    /// <summary>Gets the score as written, for example <c>0,82</c>.</summary>
    public string ScoreText => Candidate.Score.ToString("0.00", CultureInfo.GetCultureInfo("sv-SE"));

    /// <summary>Gets the score's bar width.</summary>
    public double BarWidth => Math.Round(FullBar * Candidate.Score);

    /// <summary>Gets the address, shortened in the middle.</summary>
    public string ShortAddress => SubjectText.Fit(Candidate.Address.Value, 16);

    /// <summary>Gets whether it is the candidate Enter and X decide.</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }
}

/// <summary>
/// A row in the list of checks and suggestions (sketch KF1a): the analysis's finding, its icon and tint, and for a
/// suggestion its candidates and what Enter does.
/// </summary>
public sealed partial class CheckRowViewModel : ObservableObject
{
    /// <summary>Creates the row.</summary>
    /// <param name="check">The check or suggestion.</param>
    /// <param name="anchor">The name of the actor it starts from, when that actor has a name; otherwise null.</param>
    /// <param name="nodes">The actors its area is drawn around.</param>
    public CheckRowViewModel(StoredCheck check, string? anchor, IReadOnlyList<string> nodes)
    {
        ArgumentNullException.ThrowIfNull(check);
        Check = check;
        Nodes = nodes;
        Key = GraphAreas.KeyOf(check);
        Look = AreaLooks.OfAnalysis(check.Analysis.Name, check.Finding.Area);
        Summary = anchor is null ? check.Finding.Summary : $"{anchor} · {check.Finding.Summary}";
        Candidates = [.. check.Finding.Candidates.Select(c => new CandidateRowViewModel(c, check.DecisionFor(c.Key)))];
        Group = Classify(check);
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

    /// <summary>Gets the icon and tint.</summary>
    public AreaLook Look { get; }

    /// <summary>Gets the group it stands in.</summary>
    public CheckRowGroup Group { get; }

    /// <summary>Gets the title.</summary>
    public string Title => Check.Finding.Title;

    /// <summary>Gets the line under the title.</summary>
    public string Summary { get; }

    /// <summary>Gets the analysis with its version as the user reads it, for example <c>Mixerutgångar v1</c>.</summary>
    public string AnalysisText =>
        $"{Check.AnalysisName} v{Check.Analysis.Version.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Gets how many candidates wait, as the row's count; empty when it has none.</summary>
    public string CountText => Candidates.Count > 0
        ? Candidates.Count(c => c.IsWaiting).ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    /// <summary>Gets whether it is a suggestion, which waits for a decision (dashed).</summary>
    public bool IsSuggestion => Check.Finding.Kind == CheckKind.Suggestion;

    /// <summary>Gets whether Enter and X decide something in it now.</summary>
    public bool CanDecide => Group == CheckRowGroup.ToDecide;

    /// <summary>Gets the candidates, best first; empty when it has none.</summary>
    public IReadOnlyList<CandidateRowViewModel> Candidates { get; }

    /// <summary>Gets the candidate Enter and X decide, or <see langword="null"/>.</summary>
    public CandidateRowViewModel? ChosenCandidate => Candidates.FirstOrDefault(c => c.IsChosen);

    /// <summary>Gets what Enter does, as its button says it.</summary>
    public string GoText
    {
        get
        {
            if (!CanDecide)
            {
                return "Visa i grafen";
            }

            return ChosenCandidate is { } chosen && chosen == Candidates.FirstOrDefault(c => c.IsWaiting)
                && Candidates.Count > 1
                ? "Godkänn bästa"
                : "Godkänn";
        }
    }

    /// <summary>Gets or sets the optional reason the user writes before deciding.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    /// <summary>Gets whether it is the chosen row, which folds out and shows its area.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Chooses the candidate Enter and X decide; one that is decided cannot be chosen.</summary>
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
    }

    /// <summary>Gives the decision Enter or X makes, or <see langword="null"/> when the row has nothing to decide.</summary>
    /// <param name="verdict">Approve or dismiss.</param>
    /// <returns>The request.</returns>
    public DecisionRequest? Request(CheckVerdict verdict)
    {
        if (!CanDecide || (Candidates.Count > 0 && ChosenCandidate is null))
        {
            return null;
        }

        var reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();
        return new DecisionRequest(
            Check.Analysis, Check.Finding.Key, ChosenCandidate?.Candidate.Key, verdict, reason);
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

        return check.Waiting > 0 ? CheckRowGroup.ToDecide : CheckRowGroup.Decided;
    }
}
