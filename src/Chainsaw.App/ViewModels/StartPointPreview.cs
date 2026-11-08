using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>Where a start point's row in Nytt ärende stands (sketch NC2a); never silent (principle 9).</summary>
public enum PreviewState
{
    /// <summary>It is being fetched through the exit.</summary>
    Fetching,

    /// <summary>It was fetched; the facts are shown.</summary>
    Fetched,

    /// <summary>No exit is live, so nothing was sent; it is fetched when the exit is connected.</summary>
    WaitingForExit,

    /// <summary>Its chain is recognised but not built yet (TRON), so nothing is fetched.</summary>
    NotBuilt,

    /// <summary>The lookup failed or the source does not know it; the problem says why.</summary>
    Failed,

    /// <summary>The text is not an address or a tx on any chain Chainsaw knows.</summary>
    NotRecognised,
}

/// <summary>One fact of a row: the value in a stronger ink and its unit after it, as <c>38</c> and <c> tx</c>.</summary>
/// <param name="Value">The value.</param>
/// <param name="Unit">The unit, with a space first when it needs one; may be empty.</param>
public sealed record PreviewFact(string Value, string Unit);

/// <summary>
/// What was fetched for a start point before the case existed (sketch NC2a): only facts the source gave, never
/// anything made up (balance and number of tx from the address's summary, first and last activity from the
/// transactions fetched, the height of the chain and the hash of the block they rest on).
/// </summary>
/// <param name="State">Where the row stands.</param>
/// <param name="Facts">The facts, in the order shown; empty unless fetched.</param>
/// <param name="Height">The block height the facts rest on, or <see langword="null"/>.</param>
/// <param name="HeightTip">What the height means and the block's hash, for the tooltip; empty without a height.</param>
/// <param name="Problem">Why it failed, in Swedish; empty otherwise.</param>
/// <param name="SanctionTip">
/// The imported sanction list's entry when the list names the address, for the tooltip; empty when it does not.
/// </param>
public sealed record StartPointPreview(
    PreviewState State,
    IReadOnlyList<PreviewFact> Facts,
    long? Height,
    string HeightTip,
    string Problem,
    string SanctionTip)
{
    /// <summary>Gets whether the imported sanction list names the address.</summary>
    public bool IsSanctioned => SanctionTip.Length > 0;

    /// <summary>Gives a preview with nothing but a state.</summary>
    /// <param name="state">The state.</param>
    /// <param name="sanctionTip">The sanction list's entry, or empty.</param>
    /// <returns>The preview.</returns>
    public static StartPointPreview Only(PreviewState state, string sanctionTip = "") =>
        new(state, [], null, string.Empty, string.Empty, sanctionTip);

    /// <summary>Gives a preview of a lookup that failed.</summary>
    /// <param name="problem">Why, in Swedish.</param>
    /// <param name="sanctionTip">The sanction list's entry, or empty.</param>
    /// <returns>The preview.</returns>
    public static StartPointPreview Failure(string problem, string sanctionTip = "") =>
        new(PreviewState.Failed, [], null, string.Empty, problem, sanctionTip);
}

/// <summary>
/// Fetches what Nytt ärende shows about its start points before the case exists (sketch NC2a), and seals it in the
/// case once it is created (principle 1). Lookups go only through the chains' sources, so only through the exit, and
/// send only the address or the txid (principle 4). What is fetched lies in a case in memory until then: nothing is
/// written to disk, and <see cref="Forget"/> drops it when the panel is cancelled.
/// </summary>
public interface IStartPointPrefetch
{
    /// <summary>
    /// Looks a start point up off the UI thread: the sanction list always (it is on the computer), and the source only
    /// when <paramref name="mayFetch"/> says the exit is live.
    /// </summary>
    /// <param name="startPoint">The recognised start point.</param>
    /// <param name="mayFetch">Whether lookups may go out now; without it nothing is sent.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>What to show; never throws for a failed lookup.</returns>
    Task<StartPointPreview> PreviewAsync(
        ChainStartPoint startPoint, bool mayFetch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seals every raw response fetched since the last <see cref="Forget"/> in the new case, unchanged and checked
    /// against its SHA-256, with one line in the journal; nothing when nothing was fetched. Called off the UI thread.
    /// </summary>
    /// <param name="target">The new case.</param>
    /// <returns>How many responses were sealed.</returns>
    int SealInto(ICase target);

    /// <summary>Drops everything fetched; the next lookup starts anew. Nothing of it was ever on disk.</summary>
    void Forget();
}
