using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's part in a transaction the source no longer knows (sketch GR9a): asking once per case which transaction
/// spends its coins now, and the link in the block mark's box that follows a replacer, or takes the choice back.
/// Following is the user's judgement that the replacer carries the same money on (principle 2), so the trace never
/// follows it by itself.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>What the toast says when the trace follows a replacer.</summary>
    public const string ReplacementFollowedText = "Spåret följer ersättaren";

    /// <summary>What the toast says when the choice to follow a replacer is taken back.</summary>
    public const string ReplacementTakenBackText = "Spåret slutar vid tx:en som inte finns längre";

    /// <summary>What the toast says when following a replacer, or taking it back, failed.</summary>
    public const string ReplacementFailedText = "Ersättaren kunde inte följas";

    /// <summary>What the toast's action says, which takes the choice back.</summary>
    public const string ReplacementUndoText = "Ångra";

    /// <summary>
    /// The gone transactions whose coins were asked about in this case and answered (or already known), so that each is
    /// asked once per session; one that could not be asked (offline) is asked again later.
    /// </summary>
    private readonly HashSet<string> _askedReplacements = new(StringComparer.Ordinal);

    /// <summary>Gets whether a choice about a replacer is being stored, so that a second click waits.</summary>
    public bool IsChoosingReplacement { get; private set; }

    /// <summary>
    /// Says whether a replacement row's link can be used now: taking a choice back always can, following needs the
    /// source, which offline cannot be asked (principle 9). The link is not shown otherwise (principle 7).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when the link is shown.</returns>
    public bool CanChooseReplacement(BlockMarkReplacement row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return !IsChoosingReplacement && (row.IsFollowed || !ShowsOffline);
    }

    /// <summary>
    /// Follows a replacer named in a block mark's box, or takes that choice back when the trace already follows it
    /// (the link in the row <em>Ersatt av …</em>); the graph is read again afterwards.
    /// </summary>
    /// <param name="edgeKey">The line whose mark's box holds the link.</param>
    /// <param name="replacer">The replacer whose row's link was clicked.</param>
    /// <returns>A task that completes when the choice is stored and the graph read again.</returns>
    public async Task ChooseReplacementAsync(string edgeKey, Txid replacer)
    {
        if (_session.Current is not { } openCase || !BlockMarksByLine.TryGetValue(edgeKey, out var mark)
            || mark.Replacements.FirstOrDefault(row => row.Replacer == replacer) is not { } row
            || !CanChooseReplacement(row))
        {
            return;
        }

        IsChoosingReplacement = true;
        var changed = false;
        try
        {
            changed = await ChooseAsync(openCase, row);
        }
        catch (ReplacementUnavailableException exception)
        {
            // Said as the lookup said it, for example that the exit is not connected.
            Tell(ToastKind.Warning, ReplacementFailedText, exception.Message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Tell(ToastKind.Error, ReplacementFailedText, InspectorFormsViewModel.Failure(exception));
        }
        finally
        {
            IsChoosingReplacement = false;
        }

        if (changed)
        {
            await RefreshAsync();
        }
    }

    /// <summary>Stores the choice a row's link makes, and says it in a toast.</summary>
    /// <param name="openCase">The case.</param>
    /// <param name="row">The row.</param>
    /// <returns>Whether anything was stored.</returns>
    private async Task<bool> ChooseAsync(ICase openCase, BlockMarkReplacement row)
    {
        if (row.Follow is { } follow)
        {
            var done = (await _editor.TakeBackReplacementAsync(openCase, follow)).Done;
            Tell(ToastKind.Information, ReplacementTakenBackText);
            return done;
        }

        var chosen = await _editor.FollowReplacementAsync(openCase, row.Gone, row.Replacer);
        ToastWanted?.Invoke(this, new ToastRequest(
            ToastKind.Success, ReplacementFollowedText, row.Text,
            ActionText: ReplacementUndoText, Action: () => _ = TakeBackReplacementAsync(chosen)));
        return true;
    }

    /// <summary>Opens the Guide on a transaction that is gone, from the <em>?</em> of its block mark's box.</summary>
    private void OpenGoneGuide() => GuideWanted?.Invoke(
        this, GuideRequest.ForTopic(GuideTopic.GoneTransaction) with { IsChoice = true });

    /// <summary>Takes back a choice to follow a replacer (the toast's <em>Ångra</em>) and reads the graph again.
    /// </summary>
    /// <param name="follow">The choice.</param>
    /// <returns>A task that completes when it is taken back.</returns>
    private async Task TakeBackReplacementAsync(ReplacementFollow follow)
    {
        if (_session.Current is not { } openCase)
        {
            return;
        }

        try
        {
            if ((await _editor.TakeBackReplacementAsync(openCase, follow)).Done)
            {
                await RefreshAsync();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Tell(ToastKind.Error, ReplacementFailedText, InspectorFormsViewModel.Failure(exception));
        }
    }

    /// <summary>
    /// Asks, once per gone transaction and case, which transaction spends its coins now, when the case does not know
    /// yet; only the ids of the transactions whose coins it spent go out, through the exit (principle 4), and the
    /// graph is read again when an answer came. Offline nothing is sent, and the box says what is not known (principle
    /// 9); the transaction is asked about again once a lookup can be made.
    /// </summary>
    /// <returns>A task that completes when the answers are in.</returns>
    private async Task AskReplacementsAsync()
    {
        if (_chainState is not { } state || _session.Current is not { } openCase || Scene is not { } scene)
        {
            return;
        }

        var unknown = scene.Snapshot.Graph.Gone.Values
            .Where(gone => gone.Replacement?.Kind is null or ReplacementKind.Unknown)
            .Select(gone => gone.Txid)
            .ToList();
        if (await AskAsync(state, openCase, unknown) && _session.Current == openCase)
        {
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Asks about each transaction not asked about yet in this case, and remembers those answered, so that one that
    /// could not be asked is asked again later.
    /// </summary>
    /// <param name="state">The graph's way to the chains.</param>
    /// <param name="openCase">The case.</param>
    /// <param name="txids">The gone transactions.</param>
    /// <returns><see langword="true"/> when a new answer was sealed.</returns>
    private async Task<bool> AskAsync(IGraphChainState state, ICase openCase, IReadOnlyList<Txid> txids)
    {
        var answered = false;
        foreach (var txid in txids.Where(txid => _askedReplacements.Add(txid.Stored)))
        {
            var status = LookupStatus.Failed;
            try
            {
                status = await state.AskReplacementAsync(openCase, txid, _work.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A help to the eye: the trail stops at the gone transaction all the same, and the box says what is
                // not known.
            }

            if (status is not (LookupStatus.Fetched or LookupStatus.FromCase))
            {
                _askedReplacements.Remove(txid.Stored);
            }

            answered |= status == LookupStatus.Fetched;
        }

        return answered;
    }

    /// <summary>
    /// Gives the transactions the case's block changes say are gone from a UTXO chain (Bitcoin or Litecoin), by the
    /// rule.
    /// </summary>
    /// <returns>Their ids as stored, with the chain (<see cref="Txid.Stored"/>).</returns>
    private HashSet<string> GoneByRule()
    {
        var gone = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in _changes.Where(change => change.Kind == BlockChangeKind.Gone
            && change.Chain.Model() == LedgerModel.Utxo))
        {
            if (Txid.TryParse(change.Chain, change.TransactionId, out var txid))
            {
                gone.Add(txid.Stored);
            }
        }

        return gone;
    }
}
