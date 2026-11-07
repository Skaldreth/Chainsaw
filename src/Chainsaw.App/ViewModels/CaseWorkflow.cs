using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.ViewModels;

/// <summary>How the creation of a case went.</summary>
/// <param name="IsCreated">Whether the case was created and is the open case.</param>
/// <param name="Reply">
/// The answer; an error with <paramref name="IsCreated"/> true says what could not be done after the case existed.
/// </param>
public sealed record CaseCreation(bool IsCreated, CommandReply Reply);

/// <summary>
/// Opens, creates and closes cases for the window, one at a time: a case open before is always saved and closed
/// first, the slow work runs off the UI thread, and a case that was opened while Chainsaw locked is closed again
/// rather than shown. Every answer is a short Swedish text; nothing here throws for what the user did.
/// </summary>
public sealed class CaseWorkflow
{
    /// <summary>The most start points a new case is created with at once (principle 7).</summary>
    public const int MaximumStartPoints = 50;

    /// <summary>What is said while another opening, creation or closing runs.</summary>
    public const string BusyText = "Vänta, ett ärende öppnas, skapas eller stängs redan.";

    /// <summary>What is said when there is no open case.</summary>
    public const string NoCaseText = "Inget ärende är öppet.";

    /// <summary>What is said when Chainsaw locked while a case was opened or created.</summary>
    private const string LockedMeanwhileText = "Chainsaw låstes medan ärendet öppnades, så det stängdes igen.";

    /// <summary>The cases folder.</summary>
    private readonly ICaseStore _cases;

    /// <summary>Where start points are added.</summary>
    private readonly ITracingService _tracing;

    /// <summary>The vault, read again after slow work in case Chainsaw locked meanwhile.</summary>
    private readonly IVault _vault;

    /// <summary>The window's open case.</summary>
    private readonly CaseSession _session;

    /// <summary>Keeps two openings, creations or closings from running at once.</summary>
    private bool _busy;

    /// <summary>Creates the workflow.</summary>
    /// <param name="cases">The cases folder.</param>
    /// <param name="tracing">Where start points are added.</param>
    /// <param name="vault">The vault.</param>
    /// <param name="session">The window's open case.</param>
    public CaseWorkflow(ICaseStore cases, ITracingService tracing, IVault vault, CaseSession session)
    {
        _cases = cases;
        _tracing = tracing;
        _vault = vault;
        _session = session;
    }

    /// <summary>Gets whether an opening, creation or closing runs.</summary>
    public bool IsBusy => _busy;

    /// <summary>
    /// Suggests case numbers for <c>/open</c>: the cases in the folder whose number starts with what is written, the
    /// most recently saved first (the case worked on last), without the one that is open.
    /// </summary>
    /// <param name="prefix">What is written so far.</param>
    /// <returns>The case numbers.</returns>
    public IReadOnlyList<string> SuggestCaseNumbers(string prefix)
    {
        IReadOnlyList<CaseFileInfo> files;
        try
        {
            files = _cases.List();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var open = _session.Current?.Info.CaseNumber;
        return files
            .Where(file => file.CaseNumber.StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(file => !string.Equals(file.CaseNumber, open, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWrittenUtc)
            .ThenBy(file => file.CaseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.CaseNumber)
            .ToList();
    }

    /// <summary>Opens a case in the folder, after saving and closing the case open before.</summary>
    /// <param name="caseNumber">The case number.</param>
    /// <returns>The answer.</returns>
    public async Task<CommandReply> OpenAsync(string caseNumber)
    {
        var number = caseNumber.Trim();
        if (!CaseNumberRules.IsValid(number))
        {
            return CommandReply.Failed(CaseTexts.InvalidCaseNumber);
        }

        if (_session.Current is { } current
            && string.Equals(current.Info.CaseNumber, number, StringComparison.OrdinalIgnoreCase))
        {
            return CommandReply.Done($"Ärendet {CaseSession.NameOf(current)} är redan öppet.");
        }

        return await RunAlone(async () =>
        {
            var closed = await CloseCurrentAsync();
            ICase opened;
            try
            {
                opened = await Task.Run(() => _cases.Open(number));
            }
            catch (CaseException exception)
            {
                return CommandReply.Failed(closed.Prefix + CaseTexts.For(exception.Error));
            }

            if (!Keep(opened))
            {
                return CommandReply.Failed(LockedMeanwhileText);
            }

            _session.Open(opened);
            return CommandReply.Done($"{closed.Prefix}Ärendet {CaseSession.NameOf(opened)} är öppet.");
        });
    }

    /// <summary>Saves and closes the open case.</summary>
    /// <returns>The answer.</returns>
    public async Task<CommandReply> CloseAsync()
    {
        if (_session.Current is null)
        {
            return CommandReply.Failed(NoCaseText);
        }

        return await RunAlone(async () =>
        {
            var closed = await CloseCurrentAsync();
            return closed.Saved
                ? CommandReply.Done($"Ärendet {closed.Name} sparades och stängdes.")
                : CommandReply.Failed(closed.Prefix.TrimEnd());
        });
    }

    /// <summary>
    /// Creates a case with its title, start points and, when asked, a sharing envelope, after saving and closing the
    /// case open before, and makes it the open case. Once the case exists it is kept open: a later step that fails is
    /// said in the answer instead.
    /// </summary>
    /// <param name="request">The checked request.</param>
    /// <param name="sharingPassword">
    /// The sharing password, or <see langword="null"/> for none; the caller clears it when this returns.
    /// </param>
    /// <returns>Whether the case was created and is open, and the answer.</returns>
    public async Task<CaseCreation> CreateAsync(NewCaseRequest request, char[]? sharingPassword)
    {
        ArgumentNullException.ThrowIfNull(request);
        var isCreated = false;
        var reply = await RunAlone(async () =>
        {
            var closed = await CloseCurrentAsync();
            (ICase Case, List<string> Missed) created;
            try
            {
                created = await Task.Run(() => CreateWithDetails(request, sharingPassword));
            }
            catch (CaseException exception)
            {
                return CommandReply.Failed(closed.Prefix + CaseTexts.For(exception.Error));
            }

            if (!Keep(created.Case))
            {
                return CommandReply.Failed(LockedMeanwhileText);
            }

            _session.Open(created.Case);
            isCreated = true;
            var text = $"{closed.Prefix}Ärendet {CaseSession.NameOf(created.Case)} skapades.";
            return created.Missed.Count == 0
                ? CommandReply.Done(text)
                : CommandReply.Failed($"{text} Men {string.Join(" och ", created.Missed)}.");
        });
        return new CaseCreation(isCreated, reply);
    }

    /// <summary>Adds a start point to the open case through Tracing, with a line in the journal.</summary>
    /// <param name="startPoint">The recognised start point; only Bitcoin can be added yet.</param>
    /// <returns>The answer.</returns>
    public async Task<CommandReply> AddStartPointAsync(ChainStartPoint startPoint)
    {
        ArgumentNullException.ThrowIfNull(startPoint);
        if (_session.Current is not { } openCase)
        {
            return CommandReply.Failed(NoCaseText);
        }

        if (ToTrace(startPoint) is not { } traced)
        {
            return CommandReply.Failed(NotBuiltText(startPoint));
        }

        try
        {
            var added = await Task.Run(() => _tracing.AddStartPoint(openCase, traced));
            var shown = CaseTexts.Shorten(startPoint.Value);
            return CommandReply.Done(added ? $"Startpunkten {shown} lades till." : $"{shown} är redan en startpunkt.");
        }
        catch (CaseException exception)
        {
            return CommandReply.Failed(CaseTexts.For(exception.Error));
        }
    }

    /// <summary>Says that a start point's chain is not built yet.</summary>
    /// <param name="startPoint">The start point.</param>
    /// <returns>The text.</returns>
    public static string NotBuiltText(ChainStartPoint startPoint) =>
        startPoint.Chain is { } chain
            ? $"{CaseTexts.ChainName(chain)} är inte byggt än, så det kan inte bli en startpunkt."
            : "Det känns inte igen som en adress eller en tx.";

    /// <summary>Gives the Tracing start point for a Bitcoin start point.</summary>
    /// <param name="startPoint">The recognised start point.</param>
    /// <returns>The start point, or <see langword="null"/> when its chain is not built.</returns>
    private static TraceStartPoint? ToTrace(ChainStartPoint startPoint) => startPoint.Bitcoin switch
    {
        { Address: { } address } => TraceStartPoint.ForAddress(address),
        { Txid: { } txid } => TraceStartPoint.ForTransaction(txid),
        _ => null,
    };

    /// <summary>
    /// Creates the case and adds what goes with it, off the UI thread. Only the creation itself may fail the whole;
    /// what fails after it is collected, since the case already exists and is kept.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="sharingPassword">The sharing password, or <see langword="null"/>.</param>
    /// <returns>The open case and, in Swedish, what could not be done.</returns>
    private (ICase Case, List<string> Missed) CreateWithDetails(NewCaseRequest request, char[]? sharingPassword)
    {
        var created = _cases.Create(request.CaseNumber);
        var missed = new List<string>();
        Try(missed, "titeln sparades inte", () => created.SetTitle(request.Title));
        foreach (var startPoint in request.StartPoints.Take(MaximumStartPoints))
        {
            var shown = CaseTexts.Shorten(startPoint.Value);
            Try(missed, $"startpunkten {shown} lades inte till", () => AddOrNote(created, startPoint));
        }

        if (sharingPassword is not null)
        {
            Try(missed, "ärendelösenordet sattes inte", () => created.SetSharingPassword(sharingPassword));
        }

        return (created, missed);
    }

    /// <summary>
    /// Adds a Bitcoin start point through Tracing; a start point on a chain that is not built is written in the
    /// journal instead, so that it is not lost.
    /// </summary>
    /// <param name="created">The new case.</param>
    /// <param name="startPoint">The start point.</param>
    private void AddOrNote(ICase created, ChainStartPoint startPoint)
    {
        if (ToTrace(startPoint) is { } traced)
        {
            _tracing.AddStartPoint(created, traced);
            return;
        }

        var chain = CaseTexts.ChainName(startPoint.Chain!.Value);
        created.AppendJournal(new JournalDraft(
            AppJournalKinds.StartPointNoted,
            $"Startpunkt på {chain} noterad, men {chain} är inte byggt än: {startPoint.Value}",
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["chain"] = startPoint.Chain.Value.ToString(),
                ["kind"] = startPoint.Kind.ToString(),
                ["value"] = startPoint.Value,
            })));
    }

    /// <summary>Runs one step after the creation, and notes it when it fails.</summary>
    /// <param name="missed">What could not be done.</param>
    /// <param name="what">What is said when the step fails.</param>
    /// <param name="step">The step.</param>
    private static void Try(List<string> missed, string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The case exists and is kept open whatever fails here, so that it is never left open and unseen.
            missed.Add(what);
        }
    }

    /// <summary>
    /// Keeps a case that was opened or created only if the vault is still open; otherwise closes it quietly, since
    /// Chainsaw locked while it worked.
    /// </summary>
    /// <param name="openCase">The case.</param>
    /// <returns><see langword="true"/> when the case can be shown.</returns>
    private bool Keep(ICase openCase)
    {
        if (_vault.State == VaultState.Unlocked && !openCase.IsClosed)
        {
            return true;
        }

        _ = Task.Run(() => CaseSession.DisposeQuietly(openCase));
        return false;
    }

    /// <summary>Saves and closes the open case, if there is one.</summary>
    /// <returns>Its name, whether it was saved, and what to say before the next answer when it was not.</returns>
    private async Task<(string Name, bool Saved, string Prefix)> CloseCurrentAsync()
    {
        if (_session.Current is not { } current)
        {
            return (string.Empty, true, string.Empty);
        }

        var name = CaseSession.NameOf(current);
        var saved = await _session.CloseAsync();
        return saved
            ? (name, true, string.Empty)
            : (name, false, $"Ärendet {name} gick inte att spara; arbetskopian finns kvar och tas tillbaka "
                + "nästa gång. ");
    }

    /// <summary>Runs work unless other work runs; unexpected failures become a short answer, never a crash.</summary>
    /// <param name="work">The work.</param>
    /// <returns>The work's answer.</returns>
    private async Task<CommandReply> RunAlone(Func<Task<CommandReply>> work)
    {
        if (_busy)
        {
            return CommandReply.Failed(BusyText);
        }

        _busy = true;
        try
        {
            return await work();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandReply.Failed("Ärendemappen gick inte att läsa eller skriva i.");
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
        {
            return CommandReply.Failed("Det stoppades av ett oväntat fel. Inget mer gjordes med ärendet.");
        }
        finally
        {
            _busy = false;
        }
    }
}
