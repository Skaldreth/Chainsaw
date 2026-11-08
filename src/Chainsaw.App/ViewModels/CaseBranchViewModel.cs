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

    /// <summary>What is said about the working copy's branch.</summary>
    public const string CopyHint =
        "Sparas som ett eget ärende med samma kuvert. Journalen följer med fram till kraschen.";

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
    public void Show(WorkingCopyConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        var now = _clock.UtcNow;
        CaseNumber = conflict.CaseNumber;
        FileWhen = EnvelopeWords.When(conflict.CaseFileWrittenUtc, now, _zone);
        CopyWhen = EnvelopeWords.When(conflict.WorkingCopyWrittenUtc, now, _zone);
        BranchNumber = conflict.SuggestedBranchNumber;
        ChoosesCopy = true;
        ErrorText = string.Empty;
        IsBusy = false;
        IsOpen = true;
    }

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
