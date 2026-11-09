using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The choice after a crash when the working copy does not belong to the file any more (sketch ÄR1b, <em>gick åt två
/// håll</em>): the case drawn as two branches, the file (opened as saved, the working copy moved to Återställda) or
/// the working copy (saved as a case of its own under a new number). The other branch is never removed, and the
/// choice is journalled by the case. It knows nothing of WPF, so that it is tested.
/// </summary>
public sealed partial class CaseBranchViewModel : ObservableObject
{
    /// <summary>What is said about the file's branch.</summary>
    public const string FileHint = "Öppnas som den är sparad. Arbetskopian flyttas till Återställda.";

    /// <summary>What is said about the working copy's branch after a crash.</summary>
    public const string CrashCopyHint =
        "Sparas som ett eget ärende med ditt kuvert. Journalen följer med fram till kraschen.";

    /// <summary>
    /// What is said about the working copy's branch when the file on disk was changed or replaced while the case was
    /// open (decided 2026-11-09).
    /// </summary>
    public const string ReplacedCopyHint =
        "Filen på disken ändrades medan ärendet var öppet; ditt arbete finns i arbetskopian.";

    /// <summary>What the fork says when it is not known where the journals went apart.</summary>
    public const string ApartWords = "Gick isär";

    /// <summary>The cases, which carry out the choice.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>The clock, for the times of the branches.</summary>
    private readonly IClock _clock;

    /// <summary>The time zone times are told in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>Creates the dialog, closed.</summary>
    /// <param name="workflow">The cases.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="zone">The time zone times are told in; the computer's when not given.</param>
    public CaseBranchViewModel(CaseWorkflow workflow, IClock clock, TimeZoneInfo? zone = null)
    {
        _workflow = workflow;
        _clock = clock;
        _zone = zone ?? TimeZoneInfo.Local;
    }

    /// <summary>Gets whether the dialog is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets the case number in conflict.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    public partial string CaseNumber { get; private set; } = string.Empty;

    /// <summary>Gets when the file was last saved.</summary>
    [ObservableProperty]
    public partial string FileWhen { get; private set; } = string.Empty;

    /// <summary>Gets when the working copy was last written.</summary>
    [ObservableProperty]
    public partial string CopyWhen { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what the fork says: <c>Gick isär vid rad 41</c> when it is known where the journals went apart, otherwise
    /// <c>Gick isär</c>.
    /// </summary>
    [ObservableProperty]
    public partial string ApartText { get; private set; } = ApartWords;

    /// <summary>
    /// Gets the hash of the last journal entry the two share, shown on hovering the fork, or <see langword="null"/>
    /// (no tooltip) when it is not known.
    /// </summary>
    [ObservableProperty]
    public partial string? ApartHash { get; private set; }

    /// <summary>Gets how many journal entries the file has after the fork, for example <c>+3 rader · </c>.</summary>
    [ObservableProperty]
    public partial string FileGrowth { get; private set; } = string.Empty;

    /// <summary>Gets how many journal entries the working copy has after the fork.</summary>
    [ObservableProperty]
    public partial string CopyGrowth { get; private set; } = string.Empty;

    /// <summary>Gets whether the working copy's branch is chosen; it is from the start, as in the sketch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChoosesFile), nameof(GoText))]
    public partial bool ChoosesCopy { get; private set; } = true;

    /// <summary>Gets or sets the new case number for the working copy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoText), nameof(CanGo))]
    public partial string BranchNumber { get; set; } = string.Empty;

    /// <summary>Gets whether the choice is being carried out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGo))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets what went wrong, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what is said about the working copy's branch: <see cref="CrashCopyHint"/> after a crash,
    /// <see cref="ReplacedCopyHint"/> when the file was replaced while the case was open.
    /// </summary>
    [ObservableProperty]
    public partial string CopyHint { get; private set; } = CrashCopyHint;

    /// <summary>Gets the dialog's heading, for example <c>2026-0117 gick åt två håll</c>.</summary>
    public string Heading => $"{CaseNumber} gick åt två håll";

    /// <summary>Gets whether the file's branch is chosen.</summary>
    public bool ChoosesFile => !ChoosesCopy;

    /// <summary>Gets what the button says: <c>Öppna filen</c>, or <c>Spara som 2026-0117-B</c>.</summary>
    public string GoText => ChoosesCopy ? $"Spara som {BranchNumber.Trim()}" : "Öppna filen";

    /// <summary>Gets whether the button can be pressed: not while it runs, and with a valid new number.</summary>
    public bool CanGo => !IsBusy && (!ChoosesCopy || CaseNumberRules.IsValid(BranchNumber.Trim()));

    /// <summary>Gets whether <see cref="ErrorText"/> has something to say.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Shows the dialog for a conflict, with the working copy's branch chosen.</summary>
    /// <param name="conflict">The conflict.</param>
    /// <param name="fileReplaced">
    /// Whether the file was changed or replaced on disk while the case was open, rather than a crash leaving the
    /// working copy.
    /// </param>
    public void Show(WorkingCopyConflict conflict, bool fileReplaced = false)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        CopyHint = fileReplaced ? ReplacedCopyHint : CrashCopyHint;
        var now = _clock.UtcNow;
        CaseNumber = conflict.CaseNumber;
        FileWhen = EnvelopeWords.When(conflict.CaseFileWrittenUtc, now, _zone);
        CopyWhen = EnvelopeWords.When(conflict.WorkingCopyWrittenUtc, now, _zone);
        ShowDivergence(conflict.Divergence);
        BranchNumber = conflict.SuggestedBranchNumber;
        ChoosesCopy = true;
        ErrorText = string.Empty;
        IsBusy = false;
        IsOpen = true;
    }

    /// <summary>Shows where the journals went apart and how far each branch went on, when it is known.</summary>
    /// <param name="divergence">The divergence, or <see langword="null"/>.</param>
    private void ShowDivergence(CaseDivergence? divergence)
    {
        ApartText = divergence is { Sequence: > 0 } shared
            ? string.Create(CultureInfo.InvariantCulture, $"{ApartWords} vid rad {shared.Sequence}")
            : ApartWords;
        ApartHash = divergence is { Hash.Length: > 0 } known ? known.Hash : null;
        FileGrowth = divergence is null ? string.Empty : Growth(divergence.FileEntries);
        CopyGrowth = divergence is null ? string.Empty : Growth(divergence.CopyEntries);
    }

    /// <summary>Says how many journal entries a branch has after the fork.</summary>
    /// <param name="entries">The number.</param>
    /// <returns>For example <c>+3 rader · </c>, to stand before the time.</returns>
    internal static string Growth(int entries) => entries == 1
        ? "+1 rad · "
        : string.Create(CultureInfo.InvariantCulture, $"+{entries} rader · ");

    /// <summary>Chooses the file's branch.</summary>
    [RelayCommand]
    public void PickFile() => ChoosesCopy = false;

    /// <summary>Chooses the working copy's branch.</summary>
    [RelayCommand]
    public void PickCopy() => ChoosesCopy = true;

    /// <summary>
    /// Carries out the choice (Enter): opens the file with the working copy set aside, or saves the working copy as a
    /// case of its own; the dialog closes when the chosen case is open.
    /// </summary>
    /// <returns>A task that completes when the case is open, or why not is said.</returns>
    [RelayCommand]
    public async Task GoAsync()
    {
        if (!IsOpen || !CanGo)
        {
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        var reply = await _workflow.ChooseBranchAsync(CaseNumber, ChoosesCopy ? BranchNumber.Trim() : null);
        IsBusy = false;
        if (reply.IsError)
        {
            ErrorText = reply.Text;
            return;
        }

        IsOpen = false;
    }

    /// <summary>Avbryt (Esc): leaves both branches as they are.</summary>
    [RelayCommand]
    public void Close()
    {
        if (!IsBusy)
        {
            IsOpen = false;
        }
    }
}
