using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's part in a transaction the source no longer knows (sketch GR9a): asking once per case which transaction
/// spends its coins now, and the link in the block mark's box that follows that replacer, or takes the choice back.
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

    /// <summary>The gone transactions asked about in this case, so that each is asked once per session.</summary>
    private readonly HashSet<string> _askedReplacements = new(StringComparer.Ordinal);

    /// <summary>Gets whether a choice about a replacer is being stored, so that a second click waits.</summary>
    public bool IsChoosingReplacement { get; private set; }

    /// <summary>
    /// Follows the replacer named in a block mark's box, or takes that choice back when the trace already follows it
    /// (the link in the row <em>Ersatt av …</em>); the graph is read again afterwards.
    /// </summary>
    /// <param name="edgeKey">The line whose mark's box holds the link.</param>
    /// <returns>A task that completes when the choice is stored and the graph read again.</returns>
    public async Task ChooseReplacementAsync(string edgeKey)
    {
        if (IsChoosingReplacement || _session.Current is not { } openCase
            || !BlockMarksByLine.TryGetValue(edgeKey, out var mark) || mark.Replacement is not { } replacement)
        {
            return;
        }

        IsChoosingReplacement = true;
        var changed = false;
        try
        {
            if (replacement.Follow is { } follow)
            {
                changed = (await _editor.TakeBackReplacementAsync(openCase, follow)).Done;
                Tell(ToastKind.Information, ReplacementTakenBackText);
            }
            else
            {
                var chosen = await _editor.FollowReplacementAsync(openCase, replacement.Gone, replacement.Replacer);
                changed = true;
                ToastWanted?.Invoke(this, new ToastRequest(
                    ToastKind.Success, ReplacementFollowedText, replacement.Text,
                    ActionText: ReplacementUndoText, Action: () => _ = TakeBackReplacementAsync(chosen)));
            }
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
    /// yet; only the coins go out, through the exit (principle 4), and the graph is read again when an answer came.
    /// Offline nothing is sent, and the box says what is not known (principle 9).
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
            .Where(gone => _askedReplacements.Add(gone.Txid.Value))
            .ToList();
        var answered = false;
        foreach (var gone in unknown)
        {
            try
            {
                answered |= await state.AskReplacementAsync(openCase, gone.Txid, _work.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A help to the eye: the trail ends at the gone transaction all the same, and the box says what is
                // not known.
            }
        }

        if (answered && _session.Current == openCase)
        {
            await RefreshAsync();
        }
    }
}
