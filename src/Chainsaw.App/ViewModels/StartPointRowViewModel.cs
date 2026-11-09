using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// One start point in Nytt ärende's list (sketch NC2a): the chain's badge, the value shortened and what it is, and
/// what was fetched for it in advance, or honestly why nothing is shown (fetching, waiting for the exit, not built,
/// failed, not recognised).
/// </summary>
public sealed partial class StartPointRowViewModel : ObservableObject
{
    /// <summary>What a row says while no exit is connected.</summary>
    public const string WaitingText = OfflineWords.NoExitWaiting;

    /// <summary>What a row says when its text is not recognised.</summary>
    public const string NotRecognisedText = "Känns inte igen som adress eller tx";

    /// <summary>Creates the row, fetching nothing yet.</summary>
    /// <param name="startPoint">The start point as recognised.</param>
    public StartPointRowViewModel(ChainStartPoint startPoint)
    {
        ArgumentNullException.ThrowIfNull(startPoint);
        StartPoint = startPoint;
        Preview = StartPointPreview.Only(InitialState(startPoint));
    }

    /// <summary>What the row says while it waits (<see cref="OfflineWords.Waiting"/>).</summary>
    private string _waiting = WaitingText;

    /// <summary>Gets the start point.</summary>
    public ChainStartPoint StartPoint { get; }

    /// <summary>Gets the chain's sign in the badge: <c>₿</c> or <c>Ξ</c>; empty for TRON and the unknown, which draw
    /// an icon (<see cref="IsTron"/>, <see cref="IsNotRecognised"/>).</summary>
    public string Glyph => StartPoint.Chain switch
    {
        Chain.Bitcoin => "₿",
        Chain.Ethereum => "Ξ",
        Chain.Litecoin => "Ł",
        Chain.Bsc => "B",
        Chain.Zcash => "Z",
        Chain.Solana => "S",
        _ => string.Empty,
    };

    /// <summary>Gets whether the badge draws TRON's mark.</summary>
    public bool IsTron => StartPoint.Chain == Chain.Tron;

    /// <summary>Gets the value, shortened: <c>bc1qv8x0…c9pm4tz</c>.</summary>
    public string Short => CaseTexts.Shorten(StartPoint.Value);

    /// <summary>Gets what it is: <c>Adress</c> or <c>Tx</c>, and empty when unknown.</summary>
    public string KindText => StartPoint.Kind switch
    {
        StartPointKind.Address => "Adress",
        StartPointKind.Transaction => "Tx",
        _ => string.Empty,
    };

    /// <summary>Gets the row's tooltip: the whole value and what it is, for example <c>Bitcoin-adress</c>.</summary>
    public string ToolTip => StartPoint.Value + Environment.NewLine + CaseTexts.Describe(StartPoint);

    /// <summary>Gets what was fetched, or where the row stands.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFetching), nameof(IsFetched), nameof(IsNotRecognised), nameof(HasLine))]
    [NotifyPropertyChangedFor(nameof(LineText), nameof(IsWaiting), nameof(IsSanctioned), nameof(HeightText))]
    [NotifyPropertyChangedFor(nameof(ShowsHeight))]
    public partial StartPointPreview Preview { get; private set; }

    /// <summary>Gets whether it is being fetched (the shimmer and the spinner).</summary>
    public bool IsFetching => Preview.State == PreviewState.Fetching;

    /// <summary>Gets whether the facts are shown.</summary>
    public bool IsFetched => Preview.State == PreviewState.Fetched;

    /// <summary>Gets whether the chain's height when it was fetched is known, so that the block's chip shows.</summary>
    public bool ShowsHeight => IsFetched && Preview.Height is not null;

    /// <summary>Gets whether it waits for the exit.</summary>
    public bool IsWaiting => Preview.State == PreviewState.WaitingForExit;

    /// <summary>Gets whether the text was not recognised (the dashed badge and the remove button always shown).</summary>
    public bool IsNotRecognised => Preview.State == PreviewState.NotRecognised;

    /// <summary>Gets whether the imported sanction list names the address (the red tag).</summary>
    public bool IsSanctioned => Preview.IsSanctioned;

    /// <summary>Gets whether a quiet line says why there are no facts.</summary>
    public bool HasLine => LineText.Length > 0;

    /// <summary>Gets the quiet line under the value, when there are no facts to show.</summary>
    public string LineText => Preview.State switch
    {
        PreviewState.WaitingForExit => _waiting,
        PreviewState.NotBuilt when StartPoint.IsShielded => CaseWorkflow.ShieldedText(StartPoint),
        PreviewState.NotBuilt => CaseTexts.ChainName(StartPoint.Chain!.Value) + " hämtas inte, inte byggt än",
        PreviewState.Failed => Preview.Problem,
        PreviewState.NotRecognised => NotRecognisedText,
        _ => string.Empty,
    };

    /// <summary>Gets the block height to the right, with a space between thousands; empty without one.</summary>
    public string HeightText => Preview.Height is { } height
        ? height.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("sv-SE"))
        : string.Empty;

    /// <summary>Gets whether the start point can be fetched at all (its chain is built).</summary>
    public bool CanFetch => StartPoint.CanFetch;

    /// <summary>Gets whether the row still wants a lookup: it waits for the exit or was stopped while fetching.</summary>
    public bool WantsFetch => CanFetch && Preview.State is PreviewState.WaitingForExit or PreviewState.Fetching;

    /// <summary>
    /// Says what the row waits for while lookups cannot go: an exit, the network through Windows, or the exit being
    /// connected again.
    /// </summary>
    /// <param name="waiting">The words (<see cref="OfflineWords.Waiting"/>).</param>
    public void FollowOffline(string waiting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waiting);
        if (_waiting != waiting)
        {
            _waiting = waiting;
            OnPropertyChanged(nameof(LineText));
        }
    }

    /// <summary>Stops the lookup under way, if any.</summary>
    internal CancellationTokenSource? Lookup { get; set; }

    /// <summary>Shows what was fetched, or where the row stands.</summary>
    /// <param name="preview">The preview.</param>
    public void Show(StartPointPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Preview = preview;
    }

    /// <summary>Gives the state a row starts in before anything is asked.</summary>
    /// <param name="startPoint">The start point.</param>
    /// <returns>The state.</returns>
    private static PreviewState InitialState(ChainStartPoint startPoint)
    {
        if (!startPoint.IsRecognised)
        {
            return PreviewState.NotRecognised;
        }

        return startPoint.CanFetch ? PreviewState.WaitingForExit : PreviewState.NotBuilt;
    }
}
