using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.App.Settings;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A throw in the trash's view (sketch SO1a): its line with when, who and how many, and its tree.</summary>
/// <param name="Throw">The throw.</param>
/// <param name="When">When, for example <c>i dag 21:14</c>.</param>
/// <param name="By">Who threw it.</param>
/// <param name="Count">How many, for example <c>5 aktörer</c>.</param>
/// <param name="Rows">The tree's rows.</param>
public sealed record TrashThrowGroup(
    TrashThrow Throw, string When, string By, string Count, IReadOnlyList<TrashRowViewModel> Rows);

/// <summary>A case thrown on the start page, as the trash's view lists it.</summary>
/// <param name="CaseNumber">The case.</param>
/// <param name="When">When it was thrown.</param>
public sealed record TrashCaseRow(string CaseNumber, string When)
{
    /// <summary>Gets the glyph that hangs it under its pile (sketch SO1a), or empty for a case thrown on its own.</summary>
    public string Glyph { get; init; } = string.Empty;
}

/// <summary>
/// A pile thrown whole on the start page (sketches GR8a and SO1a), as the trash's view lists it: the pile on top, its
/// cases under it as a tree.
/// </summary>
/// <param name="PileId">The pile.</param>
/// <param name="Name">Its name when it was thrown, or <see cref="GraphStartViewModel.UnnamedPileText"/>.</param>
/// <param name="When">When it was thrown.</param>
/// <param name="Cases">Its cases in the trash, each hung under it.</param>
public sealed record TrashPileRow(string PileId, string Name, string When, IReadOnlyList<TrashCaseRow> Cases)
{
    /// <summary>Gets how many cases lie in it, for example <c>3 ärenden</c>.</summary>
    public string CountText => Cases.Count == 1
        ? "1 ärende"
        : string.Create(CultureInfo.InvariantCulture, $"{Cases.Count} ärenden");
}

/// <summary>
/// The trash's own view (sketch SO1a), opened by a click on the bin: an indented tree per throw, the newest first, with
/// <em>Lägg tillbaka</em> on each row (the branch) and <em>Lägg tillbaka allt</em> per throw; and the cases thrown on
/// the start page, which lie on this computer. No WPF.
/// </summary>
public sealed partial class TrashViewModel : ObservableObject
{
    /// <summary>What the view says when nothing lies in it.</summary>
    public const string EmptyTitle = "Soptunnan är tom";

    /// <summary>The line under it.</summary>
    public const string EmptyText = "Dra något i grafen mot soptunnan nere till vänster för att dölja det.";

    /// <summary>What the head says beside the count: nothing is taken out of the case.</summary>
    public const string NothingRemovedText = "Inget tas bort ur ärendet: det slängda göms bara i grafen";

    /// <summary>The cases' heading.</summary>
    public const string CasesTitle = "Ärenden från startsidan";

    /// <summary>What the cases' heading says: they lie on this computer, and the file is untouched.</summary>
    public const string CasesText = "Döljs bara på startsidan på den här datorn. Filen ligger kvar.";

    /// <summary>The month names, short, so that the text does not hang on the computer's culture.</summary>
    private static readonly string[] Months =
        ["jan", "feb", "mars", "apr", "maj", "juni", "juli", "aug", "sep", "okt", "nov", "dec"];

    /// <summary>The cases thrown on the start page.</summary>
    private readonly CaseTrash _cases;

    /// <summary>The clock, for <em>i dag</em>.</summary>
    private readonly IClock _clock;

    /// <summary>The time zone the times are told in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>Creates the view.</summary>
    /// <param name="cases">The cases thrown on the start page.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="zone">The time zone; the computer's own when none is given.</param>
    public TrashViewModel(CaseTrash cases, IClock clock, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(clock);
        _cases = cases;
        _clock = clock;
        _zone = zone ?? TimeZoneInfo.Local;
        cases.Changed += (_, _) => ShowCases();
        ShowCases();
    }

    /// <summary>Raised when the user puts something of a throw back: the throw, and the member or none for all.</summary>
    public event EventHandler<(long Throw, string? Key)>? RestoreWanted;

    /// <summary>Gets whether the view stands over the graph.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets the throws, the newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThrows), nameof(IsEmpty), nameof(HeadText))]
    public partial IReadOnlyList<TrashThrowGroup> Groups { get; private set; } = [];

    /// <summary>Gets the cases thrown on their own on the start page, the latest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCases), nameof(IsEmpty))]
    public partial IReadOnlyList<TrashCaseRow> Cases { get; private set; } = [];

    /// <summary>Gets the piles thrown whole on the start page, the latest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCases), nameof(IsEmpty))]
    public partial IReadOnlyList<TrashPileRow> Piles { get; private set; } = [];

    /// <summary>Gets how many nodes the trash hides in the graph now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadText))]
    public partial int HiddenInGraph { get; private set; }

    /// <summary>Gets whether any throw lies in the trash.</summary>
    public bool HasThrows => Groups.Count > 0;

    /// <summary>Gets whether any case or pile from the start page lies in the trash.</summary>
    public bool HasCases => Cases.Count > 0 || Piles.Count > 0;

    /// <summary>Gets whether nothing lies in the trash.</summary>
    public bool IsEmpty => !HasThrows && !HasCases;

    /// <summary>Gets the head's count: <c>2 slängningar · 6 dolda i grafen</c>, or <c>Soptunnan</c>.</summary>
    public string HeadText => Groups.Count switch
    {
        0 => "Soptunnan",
        1 => string.Create(CultureInfo.InvariantCulture, $"1 slängning · {HiddenInGraph} dolda i grafen"),
        _ => string.Create(
            CultureInfo.InvariantCulture, $"{Groups.Count} slängningar · {HiddenInGraph} dolda i grafen"),
    };

    /// <summary>Opens the view.</summary>
    public void Open() => IsOpen = true;

    /// <summary>Closes the view, back to the graph.</summary>
    [RelayCommand]
    public void Close() => IsOpen = false;

    /// <summary>Opens the view, or closes it when it is open (a click on the bin).</summary>
    [RelayCommand]
    public void Toggle() => IsOpen = !IsOpen;

    /// <summary>Shows the open case's throws.</summary>
    /// <param name="throws">The throws, oldest first.</param>
    /// <param name="hiddenInGraph">How many drawn nodes they hide.</param>
    /// <param name="watchesOf">Gives the watches on a member, which the rows mark.</param>
    public void Show(
        IReadOnlyList<TrashThrow> throws, int hiddenInGraph, Func<TrashMember, IReadOnlyList<string>> watchesOf)
    {
        ArgumentNullException.ThrowIfNull(throws);
        ArgumentNullException.ThrowIfNull(watchesOf);
        HiddenInGraph = hiddenInGraph;
        Groups =
        [
            .. throws.Reverse().Select(each => new TrashThrowGroup(
                each,
                When(each.Utc),
                each.By.Name,
                CountText(each.Members),
                TrashRowViewModel.Tree(
                    each.Members, member => each.DepthOf(member.Key), watchesOf, each.Sequence))),
        ];
    }

    /// <summary>Puts a row back with what hangs under it.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void Restore(TrashRowViewModel? row)
    {
        if (row is not null)
        {
            RestoreWanted?.Invoke(this, (row.ThrowSequence, row.Key));
        }
    }

    /// <summary>Puts a whole throw back.</summary>
    /// <param name="group">The throw.</param>
    [RelayCommand]
    public void RestoreAll(TrashThrowGroup? group)
    {
        if (group is not null)
        {
            RestoreWanted?.Invoke(this, (group.Throw.Sequence, null));
        }
    }

    /// <summary>Puts a case back on the start page.</summary>
    /// <param name="row">The case.</param>
    [RelayCommand]
    public void RestoreCase(TrashCaseRow? row)
    {
        if (row is not null)
        {
            _cases.Restore(row.CaseNumber);
        }
    }

    /// <summary>Puts a pile back on the start page, whole.</summary>
    /// <param name="row">The pile.</param>
    [RelayCommand]
    public void RestorePile(TrashPileRow? row)
    {
        if (row is not null)
        {
            _cases.RestorePile(row.PileId);
        }
    }

    /// <summary>Puts every case and pile back on the start page.</summary>
    [RelayCommand]
    public void RestoreAllCases() => _cases.RestoreAll();

    /// <summary>Writes how many: <c>1 aktör</c> or <c>5 aktörer</c>.</summary>
    /// <param name="count">How many.</param>
    /// <returns>The text.</returns>
    public static string CountText(int count) => Counted(count, "aktör", "aktörer");

    /// <summary>
    /// Writes how many of each the members are: actors, comments and own areas, for example <c>1 kommentar</c> or
    /// <c>3 aktörer och 1 yta</c>, so that a comment is never called an actor.
    /// </summary>
    /// <param name="members">The members.</param>
    /// <returns>The text; <c>0 aktörer</c> when there are none.</returns>
    public static string CountText(IReadOnlyCollection<TrashMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var comments = members.Count(TrashKeys.IsComment);
        var areas = members.Count(TrashKeys.IsArea);
        var actors = members.Count - comments - areas;
        var parts = new List<string>();
        if (actors > 0 || members.Count == 0)
        {
            parts.Add(CountText(actors));
        }

        if (comments > 0)
        {
            parts.Add(Counted(comments, "kommentar", "kommentarer"));
        }

        if (areas > 0)
        {
            parts.Add(Counted(areas, "yta", "ytor"));
        }

        return parts.Count == 1 ? parts[0] : string.Join(", ", parts[..^1]) + " och " + parts[^1];
    }

    /// <summary>Writes a count with the word in the singular or the plural.</summary>
    /// <param name="count">How many.</param>
    /// <param name="one">The word for one.</param>
    /// <param name="many">The word for more, or none.</param>
    /// <returns>For example <c>1 yta</c> or <c>2 ytor</c>.</returns>
    private static string Counted(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");

    /// <summary>Writes when: <c>i dag 21:14</c>, <c>i går 09:02</c> or <c>4 nov 21:14</c>.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>The text, in the view's time zone.</returns>
    public string When(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, _zone);
        var today = TimeZoneInfo.ConvertTime(_clock.UtcNow, _zone).Date;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (local.Date == today)
        {
            return "i dag " + time;
        }

        if (local.Date == today.AddDays(-1))
        {
            return "i går " + time;
        }

        var day = string.Create(CultureInfo.InvariantCulture, $"{local.Day} {Months[local.Month - 1]}");
        return local.Year == today.Year
            ? day + " " + time
            : day + " " + local.Year.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Lists what was thrown on the start page, the latest first: the cases thrown on their own, and each pile thrown
    /// whole with its cases under it.
    /// </summary>
    private void ShowCases()
    {
        var latest = _cases.Cases.Reverse().ToList();
        Cases = [.. latest.Where(each => each.PileId is null).Select(each => new TrashCaseRow(
            each.CaseNumber, When(each.ThrownUtc)))];
        Piles =
        [
            .. latest.Where(each => each.PileId is not null)
                .GroupBy(each => each.PileId!, StringComparer.Ordinal)
                .Select(PileRow),
        ];
    }

    /// <summary>Makes a thrown pile's row, with its cases hung under it in the order they lie in the trash.</summary>
    /// <param name="pile">The pile's thrown cases, the latest first.</param>
    /// <returns>The row.</returns>
    private TrashPileRow PileRow(IGrouping<string, TrashedCase> pile)
    {
        var cases = pile.Reverse().ToList();
        var rows = cases.Select((each, index) => new TrashCaseRow(each.CaseNumber, When(each.ThrownUtc))
        {
            Glyph = index == cases.Count - 1 ? TrashRowViewModel.LastGlyph : TrashRowViewModel.MiddleGlyph,
        });
        var name = cases[0].PileName.Length > 0 ? cases[0].PileName : GraphStartViewModel.UnnamedPileText;
        return new TrashPileRow(pile.Key, name, When(cases.Max(each => each.ThrownUtc)), [.. rows]);
    }
}
