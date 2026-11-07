using Chainsaw.App.Links;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel.Commands;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Nytt ärende (sketch NC1a): the panel from the right that darkens the rest. The case number, the title, the start
/// points one per line with what Chains recognises in each, and *Kan öppnas av en kollega* with a sharing password.
/// The sharing password is never a property: it is taken from the two fields as characters when the case is created,
/// given to Cases and cleared.
/// </summary>
public sealed partial class NewCaseViewModel : ObservableObject
{
    /// <summary>The most start points at once; more are refused rather than silently dropped.</summary>
    public const int MaximumLines = CaseWorkflow.MaximumStartPoints;

    /// <summary>Creates and opens the case.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>Creates the panel, closed.</summary>
    /// <param name="workflow">Creates and opens the case.</param>
    public NewCaseViewModel(CaseWorkflow workflow) => _workflow = workflow;

    /// <summary>Raised when a case has been created and the panel has closed, with the answer to show.</summary>
    public event EventHandler<CommandReply>? Created;

    /// <summary>Gets whether the panel is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets or sets the case number.</summary>
    [ObservableProperty]
    public partial string CaseNumber { get; set; } = string.Empty;

    /// <summary>Gets or sets the title.</summary>
    [ObservableProperty]
    public partial string CaseTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the start points, one per line.</summary>
    [ObservableProperty]
    public partial string StartPointsText { get; set; } = string.Empty;

    /// <summary>Gets what is recognised in each line that is not empty, in order.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StartPointLine> StartPoints { get; private set; } = [];

    /// <summary>Gets or sets whether the case gets a sharing envelope (*Kan öppnas av en kollega*).</summary>
    [ObservableProperty]
    public partial bool IsShared { get; set; }

    /// <summary>Gets why the case was not created, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether an error is shown.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets whether the case is being created.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>
    /// Shows the panel. A panel that is already shown keeps what is written, and a start point is added as a new line;
    /// otherwise the panel starts empty.
    /// </summary>
    /// <param name="startPoint">A start point to fill in, for example from a <c>chainsaw:</c> link.</param>
    /// <param name="caseNumber">A case number to fill in, for example from <c>/new 2026-0117</c>.</param>
    public void Show(string? startPoint = null, string? caseNumber = null)
    {
        if (!IsOpen)
        {
            Reset();
        }

        if (!string.IsNullOrWhiteSpace(caseNumber))
        {
            CaseNumber = caseNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(startPoint))
        {
            StartPointsText = StartPointsText.Length == 0
                ? startPoint.Trim()
                : StartPointsText.TrimEnd() + Environment.NewLine + startPoint.Trim();
        }

        IsOpen = true;
    }

    /// <summary>Closes the panel without creating anything (Avbryt, Esc or ×).</summary>
    public void Cancel()
    {
        if (!IsBusy)
        {
            IsOpen = false;
            Reset();
        }
    }

    /// <summary>
    /// Closes the panel and empties it whatever runs, when Chainsaw locks; a creation that runs ends on its own, and
    /// a case it creates while locked is closed again by the workflow.
    /// </summary>
    public void Dismiss()
    {
        IsOpen = false;
        Reset();
    }

    /// <summary>
    /// Creates the case (Skapa ärende, Ctrl+Enter) after checking every field. The two password fields are read only
    /// when the case is to be shared, and are then always emptied and their characters cleared.
    /// </summary>
    /// <param name="password">The sharing password field.</param>
    /// <param name="repeat">The field where the sharing password is written again.</param>
    /// <returns>A task that completes when the case is created or the reason is shown.</returns>
    public async Task CreateAsync(ISecretEntry password, ISecretEntry repeat)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(repeat);
        if (IsBusy || !IsOpen)
        {
            return;
        }

        ErrorText = string.Empty;
        if (Check() is { } request)
        {
            var sharing = IsShared ? TakePassword(password, repeat) : null;
            if (!IsShared || sharing is not null)
            {
                await CreateAsync(request, sharing);
            }
        }
    }

    /// <summary>Recognises the start points again whenever the text changes.</summary>
    /// <param name="value">The new text.</param>
    partial void OnStartPointsTextChanged(string value) =>
        StartPoints = Lines(value).Take(MaximumLines + 1)
            .Select(line => new StartPointLine(StartPointText.Parse(line)))
            .ToList();

    /// <summary>Splits the start points into lines that are not empty.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The trimmed lines.</returns>
    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    /// <summary>Checks the case number, the title and the start points.</summary>
    /// <returns>The request, or <see langword="null"/> when <see cref="ErrorText"/> says what is wrong.</returns>
    private NewCaseRequest? Check()
    {
        var number = CaseNumber.Trim();
        if (number.Length == 0)
        {
            ErrorText = "Skriv ett ärendenummer.";
            return null;
        }

        if (!CaseNumberRules.IsValid(number))
        {
            ErrorText = CaseTexts.InvalidCaseNumber;
            return null;
        }

        if (!CaseTitleRules.IsValid(CaseTitle))
        {
            ErrorText = CaseTexts.InvalidTitle;
            return null;
        }

        if (StartPoints.Count > MaximumLines)
        {
            ErrorText = $"Högst {MaximumLines} startpunkter åt gången.";
            return null;
        }

        var unknown = StartPoints.Select((line, index) => (line, index)).FirstOrDefault(item => item.line.IsUnknown);
        if (unknown.line is not null)
        {
            ErrorText = $"Startpunkt {unknown.index + 1} känns inte igen som en adress eller en tx.";
            return null;
        }

        return new NewCaseRequest(
            number, CaseTitleRules.Normalize(CaseTitle), StartPoints.Select(line => line.StartPoint).ToList());
    }

    /// <summary>Takes the sharing password from its two fields and checks it; both fields are always emptied.</summary>
    /// <param name="passwordEntry">The password field.</param>
    /// <param name="repeatEntry">The repeat field.</param>
    /// <returns>The password, which the caller clears, or <see langword="null"/> when it is refused.</returns>
    private char[]? TakePassword(ISecretEntry passwordEntry, ISecretEntry repeatEntry)
    {
        var password = passwordEntry.Take();
        var repeat = repeatEntry.Take();
        try
        {
            if (password.Length < SharingPasswordRules.MinimumLength)
            {
                ErrorText = CaseTexts.For(CaseError.PasswordTooShort);
            }
            else if (!password.AsSpan().SequenceEqual(repeat))
            {
                ErrorText = "Ärendelösenorden är inte lika. Skriv samma lösenord två gånger.";
            }
            else
            {
                return password;
            }

            Secret.Clear(password);
            return null;
        }
        finally
        {
            Secret.Clear(repeat);
        }
    }

    /// <summary>Creates the case through the workflow and closes the panel when it exists.</summary>
    /// <param name="request">The checked request.</param>
    /// <param name="sharing">The sharing password, or <see langword="null"/>; cleared here.</param>
    /// <returns>A task that completes when the case is created or the reason is shown.</returns>
    private async Task CreateAsync(NewCaseRequest request, char[]? sharing)
    {
        IsBusy = true;
        CaseCreation creation;
        try
        {
            creation = await _workflow.CreateAsync(request, sharing);
        }
        finally
        {
            if (sharing is not null)
            {
                Secret.Clear(sharing);
            }

            IsBusy = false;
        }

        if (creation.IsCreated)
        {
            IsOpen = false;
            Reset();
            Created?.Invoke(this, creation.Reply);
        }
        else if (IsOpen)
        {
            ErrorText = creation.Reply.Text;
        }
    }

    /// <summary>Empties every field.</summary>
    private void Reset()
    {
        CaseNumber = string.Empty;
        CaseTitle = string.Empty;
        StartPointsText = string.Empty;
        IsShared = false;
        ErrorText = string.Empty;
    }
}
