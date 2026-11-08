namespace Chainsaw.App.ViewModels;

/// <summary>
/// The case's dialogs of sketch ÄR1b in one place: the colleague's file opened with its case password, the case's
/// properties with its envelopes, and the choice of branch after a crash. It shows the first and the last when the
/// cases ask for them, and only one of the three at a time.
/// </summary>
public sealed class CaseDialogsViewModel
{
    /// <summary>The cases.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>Asks for a case file, or <see langword="null"/> where there is none.</summary>
    private readonly ICaseFilePicker? _files;

    /// <summary>Creates the dialogs and follows the cases.</summary>
    /// <param name="workflow">The cases.</param>
    /// <param name="password">The dialog for a colleague's file.</param>
    /// <param name="properties">The case's properties.</param>
    /// <param name="branch">The choice of branch after a crash.</param>
    /// <param name="files">Asks for a case file, or <see langword="null"/> where there is none.</param>
    public CaseDialogsViewModel(
        CaseWorkflow workflow,
        CasePasswordViewModel password,
        CasePropertiesViewModel properties,
        CaseBranchViewModel branch,
        ICaseFilePicker? files = null)
    {
        _workflow = workflow;
        _files = files;
        Password = password;
        Properties = properties;
        Branch = branch;
        workflow.PasswordWanted += (_, offer) =>
        {
            CloseAll();
            _ = password.ShowAsync(offer);
            Shown?.Invoke(this, EventArgs.Empty);
        };
        workflow.ConflictFound += (_, conflict) =>
        {
            CloseAll();
            branch.Show(conflict);
            Shown?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Raised when the cases asked for one of the dialogs and it is shown, so that what lies over it (the case
    /// picker) is folded in.
    /// </summary>
    public event EventHandler? Shown;

    /// <summary>Gets the dialog for a colleague's file.</summary>
    public CasePasswordViewModel Password { get; }

    /// <summary>Gets the case's properties.</summary>
    public CasePropertiesViewModel Properties { get; }

    /// <summary>Gets the choice of branch after a crash.</summary>
    public CaseBranchViewModel Branch { get; }

    /// <summary>Gets whether one of the dialogs is shown.</summary>
    public bool IsAnyOpen => Password.IsOpen || Properties.IsOpen || Branch.IsOpen;

    /// <summary>Shows the open case's properties on the tab Kuvert.</summary>
    /// <returns>A task that completes when the envelopes are shown.</returns>
    public Task ShowPropertiesAsync()
    {
        CloseAll();
        return Properties.ShowAsync();
    }

    /// <summary>
    /// <em>Öppna en kollegas fil …</em>: asks for a case file and opens it through the cases, which offer its case
    /// password when this vault has no envelope.
    /// </summary>
    /// <returns>Why it could not be opened, when no dialog says so; otherwise an empty text.</returns>
    public async Task<string> PickAndOpenFileAsync()
    {
        if (_files?.PickCaseFile() is not { } path)
        {
            return string.Empty;
        }

        var reply = await _workflow.OpenFileAsync(path);
        return reply.IsError && !IsAnyOpen ? reply.Text : string.Empty;
    }

    /// <summary>Closes every dialog, as when Chainsaw locks.</summary>
    public void CloseAll()
    {
        Password.Close();
        Properties.Close();
        Branch.Close();
    }
}
