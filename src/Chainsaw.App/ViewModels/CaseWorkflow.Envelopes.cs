using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.ViewModels;

/// <summary>A case file that only opens with its case password, offered to the user (sketch ÄR1b).</summary>
/// <param name="Path">The file.</param>
/// <param name="CaseNumber">The case number, its file name.</param>
/// <param name="Size">The file's size in bytes.</param>
/// <param name="InFolder">Whether it lies in the cases folder; otherwise it is copied there when opened.</param>
public sealed record CaseFileOffer(string Path, string CaseNumber, long Size, bool InFolder);

/// <summary>How opening a case with its case password went.</summary>
/// <param name="Reply">The answer.</param>
/// <param name="IsOpen">Whether the case was opened and is the open case.</param>
/// <param name="IsWrongPassword">Whether the password did not open the file; nothing was changed then.</param>
/// <param name="EnvelopeAdded">Whether this vault got an envelope of its own in the file.</param>
public sealed record PasswordOpening(CommandReply Reply, bool IsOpen, bool IsWrongPassword, bool EnvelopeAdded);

/// <summary>
/// The case's envelopes (sketch ÄR1b): a colleague's file opened with its case password, and the choice between the
/// file and a working copy that went another way after a crash.
/// </summary>
public sealed partial class CaseWorkflow
{
    /// <summary>What is said when a chosen file is not a case file.</summary>
    public const string NotCaseFileText = "Filen är inte ett ärende (.cut).";

    /// <summary>What is said when a chosen file cannot be read.</summary>
    public const string UnreadableFileText = "Filen gick inte att läsa.";

    /// <summary>What is said when the case password does not open the file.</summary>
    public const string WrongPasswordText = "Lösenordet stämmer inte. Inget har ändrats i filen.";

    /// <summary>
    /// Raised on the UI thread when a case cannot be opened with this vault and may open with its case password, so
    /// that the shell asks for it.
    /// </summary>
    public event EventHandler<CaseFileOffer>? PasswordWanted;

    /// <summary>
    /// Raised on the UI thread when a case has a working copy that does not belong to its file, so that the shell lets
    /// the user choose a branch.
    /// </summary>
    public event EventHandler<WorkingCopyConflict>? ConflictFound;

    /// <summary>
    /// Takes a case file the user chose (<em>Öppna en fil …</em>): one in the cases folder is opened as usual (and asks
    /// for its password when this vault has no envelope), and one elsewhere is offered for its case password.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The answer; empty when a dialog takes over.</returns>
    public async Task<CommandReply> OpenFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = System.IO.Path.GetFullPath(path);
        if (!string.Equals(System.IO.Path.GetExtension(full), ".cut", StringComparison.OrdinalIgnoreCase))
        {
            return CommandReply.Failed(NotCaseFileText);
        }

        var number = System.IO.Path.GetFileNameWithoutExtension(full);
        if (!CaseNumberRules.IsValid(number))
        {
            return CommandReply.Failed(CaseTexts.For(CaseError.InvalidCaseNumber));
        }

        if (IsInFolder(full))
        {
            return await OpenAsync(number);
        }

        long size;
        try
        {
            size = await Task.Run(() => new FileInfo(full).Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Gone or out of reach since it was chosen; the raw message names the path and is never shown.
            return CommandReply.Failed(UnreadableFileText);
        }

        PasswordWanted?.Invoke(this, new CaseFileOffer(full, number, size, InFolder: false));
        return CommandReply.Done(string.Empty);
    }

    /// <summary>
    /// Describes a file's envelopes off the UI thread, for the dialog that asks for the case password.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The envelopes, or <see langword="null"/> when the file cannot be read.</returns>
    public async Task<IReadOnlyList<CaseEnvelope>?> DescribeEnvelopesAsync(string path)
    {
        try
        {
            return await Task.Run(() => _cases.DescribeEnvelopes(path));
        }
        catch (Exception exception) when (exception is CaseException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens a colleague's file with its case password, copying it into the cases folder when it lies elsewhere, and
    /// adds an envelope for this vault when asked. The case open before is closed only once the new one is open, so
    /// that a wrong password costs the user nothing.
    /// </summary>
    /// <param name="offer">The file.</param>
    /// <param name="password">The password; the caller clears it when this returns.</param>
    /// <param name="addOwnEnvelope">Whether this vault gets an envelope of its own.</param>
    /// <returns>How it went.</returns>
    public async Task<PasswordOpening> OpenWithPasswordAsync(CaseFileOffer offer, char[] password, bool addOwnEnvelope)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(password);
        var wrong = false;
        var added = false;
        var isOpen = false;
        var reply = await RunAlone(async () =>
        {
            ICase opened;
            try
            {
                opened = await Task.Run(() => _cases.ImportWithPassword(offer.Path, password));
            }
            catch (CaseException exception)
            {
                wrong = exception.Error == CaseError.NoAccess;
                return CommandReply.Failed(wrong ? WrongPasswordText : CaseTexts.For(exception.Error));
            }

            var missed = addOwnEnvelope && !await TryAsync(opened.AddOperatorAccess);
            added = addOwnEnvelope && !missed;
            var closed = await CloseCurrentAsync();
            var shown = Show(opened, closed.Prefix);
            isOpen = !shown.IsError;
            return missed && isOpen
                ? CommandReply.Failed($"{shown.Text} Men ditt kuvert lades inte till.")
                : shown;
        });
        return new PasswordOpening(reply, isOpen, wrong, added && isOpen);
    }

    /// <summary>
    /// Chooses a branch after a crash (sketch ÄR1b): the file, with the working copy moved to <c>Återställda</c>, or
    /// the working copy, saved as a case of its own; the chosen case becomes the open case.
    /// </summary>
    /// <param name="caseNumber">The case in conflict.</param>
    /// <param name="branchNumber">
    /// The new case number for the working copy, or <see langword="null"/> to open the file.
    /// </param>
    /// <returns>The answer.</returns>
    public async Task<CommandReply> ChooseBranchAsync(string caseNumber, string? branchNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseNumber);
        if (branchNumber is not null && !CaseNumberRules.IsValid(branchNumber.Trim()))
        {
            return CommandReply.Failed(CaseTexts.InvalidCaseNumber);
        }

        return await RunAlone(async () =>
        {
            var closed = await CloseCurrentAsync();
            ICase opened;
            try
            {
                opened = await Task.Run(() => branchNumber is null
                    ? _cases.OpenSettingAsideWorkingCopy(caseNumber)
                    : _cases.SaveWorkingCopyAsCase(caseNumber, branchNumber.Trim()));
            }
            catch (CaseException exception)
            {
                return CommandReply.Failed(closed.Prefix + CaseTexts.For(exception.Error));
            }

            return Show(opened, closed.Prefix);
        });
    }

    /// <summary>Makes an opened case the open one, unless Chainsaw locked meanwhile.</summary>
    /// <param name="opened">The case.</param>
    /// <param name="prefix">What to say first about the case closed before.</param>
    /// <returns>The answer.</returns>
    private CommandReply Show(ICase opened, string prefix)
    {
        if (!Keep(opened))
        {
            return CommandReply.Failed(LockedMeanwhileText);
        }

        _session.Open(opened);
        _absorbing = AbsorbAsync(opened);
        return CommandReply.Done($"{prefix}Ärendet {CaseSession.NameOf(opened)} är öppet.");
    }

    /// <summary>
    /// Offers the way in that an opening which was refused may still have: the case password for a file without this
    /// vault's envelope, and the choice of branch for a working copy in conflict.
    /// </summary>
    /// <param name="caseNumber">The case number.</param>
    /// <param name="error">Why the opening was refused.</param>
    /// <returns>A task that completes when the offer is made, or there is none.</returns>
    private async Task OfferWayInAsync(string caseNumber, CaseError error)
    {
        if (error == CaseError.NoAccess
            && (await ListAsync())?.FirstOrDefault(file => SameNumber(file.CaseNumber, caseNumber)) is { } file)
        {
            PasswordWanted?.Invoke(this, new CaseFileOffer(file.Path, file.CaseNumber, file.Size, InFolder: true));
        }
        else if (error == CaseError.Conflict && await FindConflictAsync(caseNumber) is { } conflict)
        {
            ConflictFound?.Invoke(this, conflict);
        }
    }

    /// <summary>Looks for a working copy in conflict off the UI thread.</summary>
    /// <param name="caseNumber">The case number.</param>
    /// <returns>The conflict, or <see langword="null"/> when there is none or the folder cannot be read.</returns>
    private async Task<WorkingCopyConflict?> FindConflictAsync(string caseNumber)
    {
        try
        {
            return await Task.Run(() => _cases.FindConflict(caseNumber));
        }
        catch (Exception exception) when (exception is CaseException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Runs a step off the UI thread and tells whether it went well.</summary>
    /// <param name="step">The step.</param>
    /// <returns><see langword="true"/> when it did not fail.</returns>
    private static async Task<bool> TryAsync(Action step)
    {
        try
        {
            await Task.Run(step);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Said in the answer; the case is open all the same.
            return false;
        }
    }

    /// <summary>Tells whether a file lies directly in the cases folder.</summary>
    /// <param name="path">The full path.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool IsInFolder(string path) =>
        string.Equals(
            System.IO.Path.GetDirectoryName(path)?.TrimEnd(System.IO.Path.DirectorySeparatorChar),
            System.IO.Path.GetFullPath(_cases.Directory).TrimEnd(System.IO.Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
