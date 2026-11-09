using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Checks.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>An address on the card of a wallet whose approval waits to be taken back (sketch PL1b).</summary>
/// <param name="Short">The address shortened for the row.</param>
/// <param name="Full">The address in full, for the tooltip.</param>
public sealed record WalletUndoAddress(string Short, string Full);

/// <summary>
/// A wallet whose approval waits to be taken back, confirmed in the graph (sketch PL1b): the cluster opens as a card
/// with its addresses, and a small box under it asks before anything is sealed.
/// </summary>
/// <param name="Key">The suggestion's key in the panel.</param>
/// <param name="Addresses">The addresses the approval merged, in order.</param>
/// <param name="Nodes">The actors in the graph that hold them; empty when none of them is drawn.</param>
public sealed record WalletUndo(string Key, IReadOnlyList<WalletUndoAddress> Addresses, IReadOnlyList<string> Nodes)
{
    /// <summary>Gets the card's heading, for example <c>3 adresser</c>.</summary>
    public string CountText => Addresses.Count.ToString(CultureInfo.InvariantCulture) + " adresser";
}

/// <summary>
/// Taking back an approved wallet (sketch PL1b): Ctrl+Z, the toast's <em>Ångra</em> or <em>Ångra</em> in the log does
/// not take it back at once, since it splits a cluster the graph stands on. The wallet's cluster is chosen in the graph
/// and opens as a card with its addresses and a box under it; the panel says <em>Bekräfta i grafen</em>. Enter takes
/// the approval back as a new sealed decision, with an optional reason, and the Checks module withdraws the merge the
/// approval made (principle 1: the approval stays sealed, struck through in Historik). Esc leaves it as it was.
/// </summary>
public sealed partial class ChecksViewModel
{
    /// <summary>What the panel says while the take-back waits in the graph.</summary>
    public const string ConfirmInGraphText = "Bekräfta i grafen";

    /// <summary>What the box under the card says happens.</summary>
    public const string WalletUndoText =
        "Sammanslagningen dras tillbaka och adresserna står var för sig igen. Godkännandet står kvar, överstruket.";

    /// <summary>The decisions the box in the graph takes back once confirmed, or <see langword="null"/>.</summary>
    private MadeDecisions? _pendingUndo;

    /// <summary>Raised when a wallet's take-back waits in the graph, so that its cluster is chosen and shown.</summary>
    public event EventHandler<WalletUndo>? WalletUndoWanted;

    /// <summary>Gets the wallet whose take-back waits in the graph, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInGraph))]
    public partial WalletUndo? PendingWalletUndo { get; private set; }

    /// <summary>Gets whether a wallet's take-back waits in the graph, so that the panel says so.</summary>
    public bool IsConfirmingInGraph => PendingWalletUndo is not null;

    /// <summary>Gets whether the box's optional reason is folded out.</summary>
    [ObservableProperty]
    public partial bool IsUndoReasonOpen { get; private set; }

    /// <summary>Gets or sets the optional reason for taking the approval back.</summary>
    [ObservableProperty]
    public partial string UndoReason { get; set; } = string.Empty;

    /// <summary>
    /// Enter in the box: takes the wallet's approval back, sealed in the case with the optional reason; the merge it
    /// made is withdrawn by the Checks module.
    /// </summary>
    /// <returns>A task that completes when it is stored and the checks read again.</returns>
    [RelayCommand]
    public Task ConfirmWalletUndoAsync()
    {
        if (_pendingUndo is not { } decided)
        {
            return Task.CompletedTask;
        }

        var reason = string.IsNullOrWhiteSpace(UndoReason) ? null : UndoReason.Trim();
        ForgetWalletUndo();
        return TakeBackAsync(decided, confirmed: true, reason);
    }

    /// <summary>Esc in the box, or another actor chosen: nothing is taken back.</summary>
    [RelayCommand]
    public void CancelWalletUndo() => ForgetWalletUndo();

    /// <summary>Folds the box's optional reason out or in.</summary>
    [RelayCommand]
    public void ToggleUndoReason() => IsUndoReasonOpen = !IsUndoReasonOpen;

    /// <summary>Says whether a decision approves a wallet, which the graph confirms before it is undone.</summary>
    /// <param name="decision">The decision.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsWalletApproval(CheckDecision decision) =>
        decision.Analysis.Name == CheckAnalysisNames.Wallets && decision.Verdict == CheckVerdict.Approved;

    /// <summary>
    /// Lets the graph confirm a take-back that undoes a wallet's approval: remembers it, and asks for the cluster to
    /// be shown as a card.
    /// </summary>
    /// <param name="decided">The decisions to take back, with their case.</param>
    /// <param name="key">The suggestion's key.</param>
    /// <returns><see langword="true"/> when the graph confirms it; <see langword="false"/> for other decisions.</returns>
    private bool AskInGraph(MadeDecisions decided, string key)
    {
        var first = decided.Decisions[0];
        if (!decided.Decisions.Any(IsWalletApproval)
            || _checks.FirstOrDefault(check => check.Analysis == first.Analysis
                && check.Finding.Key == first.FindingKey) is not { } check)
        {
            return false;
        }

        var addresses = check.Finding.Addresses;
        var nodes = _scene is { } scene ? GraphAreas.NodesOf(scene, addresses, []) : [];
        _pendingUndo = decided;
        UndoReason = string.Empty;
        IsUndoReasonOpen = false;
        PendingWalletUndo = new WalletUndo(
            key,
            [.. addresses.Select(address => new WalletUndoAddress(SubjectText.Fit(address.Value, 22), address.Value))],
            nodes);
        WalletUndoWanted?.Invoke(this, PendingWalletUndo);
        return true;
    }

    /// <summary>Forgets the take-back that waited in the graph.</summary>
    private void ForgetWalletUndo()
    {
        _pendingUndo = null;
        UndoReason = string.Empty;
        IsUndoReasonOpen = false;
        PendingWalletUndo = null;
    }
}
