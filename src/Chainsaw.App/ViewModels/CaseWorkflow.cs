using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.ViewModels;

/// <summary>How the creation of a case went.</summary>
/// <param name="IsCreated">Whether the case was created and is the open case.</param>
/// <param name="Reply">
/// The answer; an error with <paramref name="IsCreated"/> true says what could not be done after the case existed.
/// </param>
/// <param name="Exists">
/// Whether the case file exists, also when the case is not open because Chainsaw locked while it was created.
/// </param>
public sealed record CaseCreation(bool IsCreated, CommandReply Reply, bool Exists = false);

/// <summary>
/// Opens, creates and closes cases for the window, one at a time: a case open before is always saved and closed
/// first, the slow work runs off the UI thread, and a case that was opened while Chainsaw locked is closed again
/// rather than shown. Every answer is a short Swedish text; nothing here throws for what the user did.
/// </summary>
public sealed partial class CaseWorkflow
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

    /// <summary>
    /// The label library, which takes in the labels of every case that is opened (CLAUDE.md, <em>Etiketter
    /// smittar</em>); <see langword="null"/> where there is none.
    /// </summary>
    private readonly ILabelLibrary? _library;

    /// <summary>
    /// Labels the start points the imported sanction list names when a case is created, as Nytt ärende showed them;
    /// <see langword="null"/> where there is none.
    /// </summary>
    private readonly IAttributionService? _attribution;

    /// <summary>The taking in of the open case's labels under way, waited for before the case is closed.</summary>
    private Task _absorbing = Task.CompletedTask;

    /// <summary>The fetching of the start points that are transactions the case lacks.</summary>
    private readonly StartTransactionFetches _fetches;

    /// <summary>Keeps two openings, creations or closings from running at once.</summary>
    private bool _busy;

    /// <summary>The work under way, or a completed task (see <see cref="Running"/>).</summary>
    private Task _running = Task.CompletedTask;

    /// <summary>
    /// The case files as last listed off the UI thread, so that suggestions never read the folder while typing
    /// (principle 6).
    /// </summary>
    private IReadOnlyList<CaseFileInfo> _files = [];

    /// <summary>Creates the workflow.</summary>
    /// <param name="cases">The cases folder.</param>
    /// <param name="tracing">Where start points are added.</param>
    /// <param name="vault">The vault.</param>
    /// <param name="session">The window's open case.</param>
    /// <param name="library">The label library, or <see langword="null"/> where there is none.</param>
    /// <param name="attribution">
    /// Labels the sanctioned start points of a new case, or <see langword="null"/> where there is none.
    /// </param>
    public CaseWorkflow(
        ICaseStore cases,
        ITracingService tracing,
        IVault vault,
        CaseSession session,
        ILabelLibrary? library = null,
        IAttributionService? attribution = null)
    {
        _attribution = attribution;
        _cases = cases;
        _tracing = tracing;
        _vault = vault;
        _session = session;
        _library = library;
        _fetches = new StartTransactionFetches(tracing);
        _fetches.Fetched += (_, _) => StartPointAdded?.Invoke(this, EventArgs.Empty);
        _fetches.Missed += (_, text) => StartTransactionsMissed?.Invoke(this, text);
        vault.StateChanged += (_, state) =>
        {
            // Nothing is looked up while Chainsaw is locked; what is left is tried when the case is opened again.
            if (state != VaultState.Unlocked)
            {
                _fetches.Cancel();
            }
        };
    }

    /// <summary>
    /// Raised off the UI thread when start points that are transactions could not be fetched, with the reason in
    /// Swedish and that Chainsaw tries again (principle 9).
    /// </summary>
    public event EventHandler<string>? StartTransactionsMissed;

    /// <summary>
    /// Gets the fetching of start points under way, which never fails; an expansion waits for it, so that the same
    /// transaction is not fetched twice.
    /// </summary>
    public Task StartTransactionsSettled => _fetches.Settled;

    /// <summary>
    /// Tries again to fetch the open case's start points that are transactions it lacks, as when the exit connects.
    /// </summary>
    public void RetryStartTransactions()
    {
        if (_session.Current is { IsClosed: false } openCase && _vault.State == VaultState.Unlocked)
        {
            _fetches.Start(openCase);
        }
    }

    /// <summary>
    /// Gets the case files as last listed off the UI thread (<see cref="RefreshCasesAsync"/>), for the latest cases
    /// around Nytt ärende in the empty graph (sketch GR6c).
    /// </summary>
    public IReadOnlyList<CaseFileInfo> Files => _files;

    /// <summary>
    /// Gets whether the folder could be read the last time it was listed, so that what is not in <see cref="Files"/>
    /// is known to be gone (the start page's piles forget it) and not merely unread.
    /// </summary>
    public bool IsListed { get; private set; }

    /// <summary>Gets whether an opening, creation or closing runs.</summary>
    public bool IsBusy => _busy;

    /// <summary>
    /// Gets the work under way (an opening, a creation or a closing), or a completed task, so that Chainsaw can wait
    /// for it, with a limit, before it exits instead of leaving a case half made.
    /// </summary>
    public Task Running => _running;

    /// <summary>Raised on the UI thread when the case files have been listed anew.</summary>
    public event EventHandler? CasesListed;

    /// <summary>Raised when a start point has been added to the open case, so that the graph reads it anew.</summary>
    public event EventHandler? StartPointAdded;

    /// <summary>
    /// Lists the case files off the UI thread for the suggestions; called when the shell is shown and after every
    /// opening, creation and closing. A folder that cannot be read gives no suggestions.
    /// </summary>
    /// <returns>A task that completes when the list is replaced.</returns>
    public async Task RefreshCasesAsync()
    {
        var files = await ListAsync();
        IsListed = files is not null;
        _files = files ?? [];
        CasesListed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Suggests case numbers for <c>/open</c>: the cases in the folder whose number starts with what is written, the
    /// most recently saved first (the case worked on last), without the one that is open.
    /// </summary>
    /// <param name="prefix">What is written so far.</param>
    /// <returns>The case numbers.</returns>
    /// <remarks>Reads the list made by <see cref="RefreshCasesAsync"/>, never the folder.</remarks>
    public IReadOnlyList<string> SuggestCaseNumbers(string prefix)
    {
        var open = _session.Current is { } current ? CaseSession.NumberOf(current) : null;
        return _files
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
            && string.Equals(CaseSession.NumberOf(current), number, StringComparison.OrdinalIgnoreCase))
        {
            return CommandReply.Done($"Ärendet {CaseSession.NameOf(current)} är redan öppet.");
        }

        return await RunAlone(async () =>
        {
            // Checked before the open case is closed, so that a mistyped number does not cost the user that case.
            if (await ListAsync() is { } files && !files.Any(file => SameNumber(file.CaseNumber, number)))
            {
                return CommandReply.Failed(CaseTexts.For(CaseError.NotFound));
            }

            var closed = await CloseCurrentAsync();
            ICase opened;
            try
            {
                opened = await Task.Run(() => _cases.Open(number));
            }
            catch (CaseException exception)
            {
                // A file without this vault's envelope, or a working copy in conflict, asks the user (sketch ÄR1b).
                await OfferWayInAsync(number, exception.Error);
                return CommandReply.Failed(closed.Prefix + CaseTexts.For(exception.Error));
            }

            return Show(opened, closed.Prefix);
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
    /// <param name="prefetch">
    /// What Nytt ärende fetched for the start points before the case existed (sketch NC2a), sealed in the case right
    /// after it is created; <see langword="null"/> for nothing.
    /// </param>
    /// <returns>Whether the case was created and is open, and the answer.</returns>
    public async Task<CaseCreation> CreateAsync(
        NewCaseRequest request, char[]? sharingPassword, IStartPointPrefetch? prefetch = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var isCreated = false;
        var exists = false;
        var reply = await RunAlone(async () =>
        {
            // Checked before the open case is closed: a number that exists (also the open case's) is refused at once.
            if (_session.Current is { } current && SameNumber(CaseSession.NumberOf(current), request.CaseNumber)
                || (await ListAsync() ?? []).Any(file => SameNumber(file.CaseNumber, request.CaseNumber)))
            {
                return CommandReply.Failed(CaseTexts.For(CaseError.AlreadyExists));
            }

            var closed = await CloseCurrentAsync();
            (ICase Case, List<string> Missed) created;
            try
            {
                created = await Task.Run(() => CreateWithDetails(request, sharingPassword, prefetch));
            }
            catch (CaseException exception)
            {
                return CommandReply.Failed(closed.Prefix + CaseTexts.For(exception.Error));
            }

            exists = true;
            if (!Keep(created.Case))
            {
                return CommandReply.Failed(LockedAfterCreation(created.Case.Info.CaseNumber, created.Missed));
            }

            _session.Open(created.Case);
            _absorbing = AbsorbAsync(created.Case);
            _fetches.Start(
                created.Case,
                [.. request.StartPoints.Take(MaximumStartPoints).Select(ToTrace).OfType<TraceStartPoint>()]);
            isCreated = true;
            var text = $"{closed.Prefix}Ärendet {CaseSession.NameOf(created.Case)} skapades.";
            return created.Missed.Count == 0
                ? CommandReply.Done(text)
                : CommandReply.Failed($"{text} Men {string.Join(" och ", created.Missed)}.");
        });
        return new CaseCreation(isCreated, reply, exists);
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
            if (added)
            {
                StartPointAdded?.Invoke(this, EventArgs.Empty);
                _fetches.Start(openCase, [traced]);
            }

            var shown = CaseTexts.Shorten(startPoint.Value);
            return CommandReply.Done(added ? $"Startpunkten {shown} lades till." : $"{shown} är redan en startpunkt.");
        }
        catch (CaseException exception)
        {
            return CommandReply.Failed(CaseTexts.For(exception.Error));
        }
    }

    /// <summary>Says that a start point's chain cannot be traced yet (TRON).</summary>
    /// <param name="startPoint">The start point.</param>
    /// <returns>The text.</returns>
    public static string NotBuiltText(ChainStartPoint startPoint) => startPoint.Chain switch
    {
        { } chain when startPoint.CanFetch =>
            $"{CaseTexts.ChainName(chain)} kan inte spåras i grafen än, så det kan inte bli en startpunkt.",
        { } chain => $"{CaseTexts.ChainName(chain)} är inte byggt än, så det kan inte bli en startpunkt.",
        _ => "Det känns inte igen som en adress eller en tx.",
    };

    /// <summary>Gives the Tracing start point for a Bitcoin, Ethereum, TRON or BSC start point.</summary>
    /// <param name="startPoint">The recognised start point.</param>
    /// <returns>The start point, or <see langword="null"/> when its chain is not built.</returns>
    public static TraceStartPoint? ToTrace(ChainStartPoint startPoint)
    {
        ArgumentNullException.ThrowIfNull(startPoint);
        if (startPoint.Chain is Chain.Ethereum or Chain.Tron or Chain.Bsc)
        {
            if (startPoint.EthereumAccount is { } address)
            {
                return TraceStartPoint.ForEthereumAddress(address);
            }

            return startPoint.AccountTransaction is { } hash ? TraceStartPoint.ForEthereumTransaction(hash) : null;
        }

        return startPoint.Bitcoin switch
        {
            { Address: { } address } => TraceStartPoint.ForAddress(address),
            { Txid: { } txid } => TraceStartPoint.ForTransaction(txid),
            _ => null,
        };
    }

    /// <summary>
    /// Creates the case and adds what goes with it, off the UI thread. Only the creation itself may fail the whole;
    /// what fails after it is collected, since the case already exists and is kept.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="sharingPassword">The sharing password, or <see langword="null"/>.</param>
    /// <param name="prefetch">What was fetched before the case existed, or <see langword="null"/>.</param>
    /// <returns>The open case and, in Swedish, what could not be done.</returns>
    private (ICase Case, List<string> Missed) CreateWithDetails(
        NewCaseRequest request, char[]? sharingPassword, IStartPointPrefetch? prefetch)
    {
        var created = _cases.Create(request.CaseNumber);
        var missed = new List<string>();
        Try(missed, "titeln sparades inte", () => created.SetTitle(request.Title));
        if (prefetch is not null)
        {
            // First, so that what the panel showed is in the case before anything is built on it (principle 1).
            Try(missed, "det som hämtades i förväg förseglades inte", () => prefetch.SealInto(created));
        }

        foreach (var startPoint in request.StartPoints.Take(MaximumStartPoints))
        {
            var shown = CaseTexts.Shorten(startPoint.Value);
            Try(missed, $"startpunkten {shown} lades inte till", () => AddOrNote(created, startPoint));
        }

        CheckSanctions(created, request.StartPoints, missed);
        if (sharingPassword is not null)
        {
            Try(missed, "ärendelösenordet sattes inte", () => created.SetSharingPassword(sharingPassword));
        }

        return (created, missed);
    }

    /// <summary>
    /// Adds a Bitcoin or Ethereum start point through Tracing; one on a chain that is not built is written in the
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
        var why = startPoint.CanFetch ? "kan inte spåras i grafen än" : "är inte byggt än";
        created.AppendJournal(new JournalDraft(
            AppJournalKinds.StartPointNoted,
            $"Startpunkt på {chain} noterad, men {chain} {why}: {startPoint.Value}",
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["chain"] = startPoint.Chain.Value.ToString(),
                ["kind"] = startPoint.Kind.ToString(),
                ["value"] = startPoint.Value,
            })));
    }

    /// <summary>
    /// Labels the Bitcoin and Ethereum start points the imported sanction list names, with the list as the origin, so
    /// that the tag Nytt ärende showed is in the case as a label (principle 2: an assessment from an open source).
    /// </summary>
    /// <param name="created">The new case.</param>
    /// <param name="startPoints">The start points.</param>
    /// <param name="missed">What could not be done.</param>
    private void CheckSanctions(ICase created, IReadOnlyList<ChainStartPoint> startPoints, List<string> missed)
    {
        if (_attribution is not { } attribution)
        {
            return;
        }

        // One step per chain, so that a failure for one never keeps the other from being checked.
        var bitcoin = startPoints.Select(each => each.Bitcoin?.Address).OfType<BitcoinAddress>().ToList();
        if (bitcoin.Count > 0)
        {
            Try(missed, "sanktionslistan kontrollerades inte för Bitcoin",
                () => attribution.CheckSanctions(created, bitcoin));
        }

        foreach (var chain in startPoints.Select(each => each.EthereumAccount).OfType<EthereumAddress>()
            .GroupBy(account => account.Chain))
        {
            var accounts = chain.ToList();
            Try(missed, $"sanktionslistan kontrollerades inte för {CaseTexts.ChainName(chain.Key)}",
                () => attribution.CheckSanctions(created, accounts));
        }

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

        _session.Release(openCase);
        return false;
    }

    /// <summary>
    /// Says that a case was created but closed again because Chainsaw locked, what was not done, and how to open it.
    /// </summary>
    /// <param name="number">The case number.</param>
    /// <param name="missed">What could not be done after the case existed.</param>
    /// <returns>The text.</returns>
    private static string LockedAfterCreation(string number, IReadOnlyList<string> missed)
    {
        var notDone = missed.Count == 0 ? string.Empty : $" Det som inte hann göras: {string.Join(", ", missed)}.";
        return $"Ärendet {number} skapades, men Chainsaw låstes, så det stängdes.{notDone} "
            + $"Öppna det med /open {number}.";
    }

    /// <summary>Tells whether two case numbers name the same file (Windows ignores the case of letters).</summary>
    /// <param name="first">One number.</param>
    /// <param name="second">The other.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    private static bool SameNumber(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lists the case files off the UI thread.</summary>
    /// <returns>The files, or <see langword="null"/> when the folder cannot be read.</returns>
    private async Task<IReadOnlyList<CaseFileInfo>?> ListAsync()
    {
        try
        {
            return await Task.Run(_cases.List);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
        // A start point being fetched is stopped first; it is tried again when the case is opened.
        await _fetches.StopAsync();
        // The labels set while it was open go into the library before it closes, so that the next case gets them.
        await _absorbing;
        await AbsorbAsync(current);
        var saved = await _session.CloseAsync();
        return saved
            ? (name, true, string.Empty)
            : (name, false, $"Ärendet {name} gick inte att spara; arbetskopian finns kvar och tas tillbaka "
                + "nästa gång. ");
    }

    /// <summary>
    /// Takes the case's labels into the label library off the UI thread (CLAUDE.md, <em>Etiketter smittar</em>). A
    /// locked vault or a library that cannot be read never keeps the case from being worked with; the labels are
    /// taken in the next time the case is opened or closed.
    /// </summary>
    /// <param name="openCase">The case.</param>
    /// <returns>A task that completes when the labels are taken in or could not be.</returns>
    private async Task AbsorbAsync(ICase openCase)
    {
        if (_library is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => _library.Absorb(openCase));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // See the summary: the library is a help, never a condition for the case.
        }
    }

    /// <summary>
    /// Runs work unless other work runs; unexpected failures become a short answer, never a crash. The case files are
    /// listed anew before the answer comes, so that a caller sees the list as the work left it and never lists it a
    /// second time beside this listing.
    /// </summary>
    /// <param name="work">The work.</param>
    /// <returns>The work's answer.</returns>
    private async Task<CommandReply> RunAlone(Func<Task<CommandReply>> work)
    {
        if (_busy)
        {
            return CommandReply.Failed(BusyText);
        }

        var reply = await RunBusy(work);
        await RefreshCasesAsync();
        return reply;
    }

    /// <summary>Runs work while the workflow is busy; unexpected failures become a short answer, never a crash.</summary>
    /// <param name="work">The work.</param>
    /// <returns>The work's answer.</returns>
    private async Task<CommandReply> RunBusy(Func<Task<CommandReply>> work)
    {
        _busy = true;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _running = done.Task;
        try
        {
            // A case that a lock started closing in the background must be closed before it can be opened again.
            await _session.Settled;
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
            done.SetResult();
        }
    }
}
