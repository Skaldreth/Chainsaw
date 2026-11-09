using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The strip as a lens (sketch TL2a): the selection view's time window, kept when the view closes, is magnified in the
/// strip with the held-back transactions as short grey strokes and the traced money's arrival as a dashed accent line,
/// and the strip ends dotted at the newest block the case knows. Nothing is written in the case.
/// </summary>
public sealed partial class GraphTimelineViewModel
{
    /// <summary>The windows the lens's chip steps through, in hours around its centre; after the last it closes.</summary>
    public static readonly IReadOnlyList<int> LensSteps = [48, 168, 6];

    /// <summary>What the lens's chip says before its window.</summary>
    public const string LensName = "Valvyn";

    /// <summary>The lens's centre: the traced arrival, or the window's own centre.</summary>
    private DateTimeOffset _lensCentre;

    /// <summary>Gets the lens's window in hours around its centre, or 0 when there is no lens.</summary>
    public int LensHours { get; private set; }

    /// <summary>Gets whether the strip is a lens now.</summary>
    public bool HasLens => LensHours > 0;

    /// <summary>Gets the traced money's arrival in the lens, drawn as a dashed accent line, or <see langword="null"/>.</summary>
    public DateTimeOffset? LensArrival { get; private set; }

    /// <summary>Gets the times of the held-back transactions, drawn as grey strokes inside the lens.</summary>
    public IReadOnlyList<DateTimeOffset> LensHeldBack { get; private set; } = [];

    /// <summary>Gets the newest block the case knows, where the strip ends, or <see langword="null"/>.</summary>
    public ChainTip? NewestBlock { get; private set; }

    /// <summary>Gets what the lens's chip says, for example <c>Valvyn · ±48 h</c>.</summary>
    public string LensText => HasLens ? $"{LensName} · {WindowName(LensHours)}" : string.Empty;

    /// <summary>Gets the lens's window on the strip's axis, or <see langword="null"/> without a lens.</summary>
    public StripWindow? LensWindow => HasLens
        ? new StripWindow(Axis.Hours(_lensCentre) - LensHours, Axis.Hours(_lensCentre) + LensHours)
        : null;

    /// <summary>
    /// Keeps the selection view's window as the strip's lens, when it closes with one; a view on the whole history
    /// takes the lens away.
    /// </summary>
    /// <param name="centre">The window's centre.</param>
    /// <param name="hours">The window's hours on either side; 0 for the whole history.</param>
    /// <param name="arrival">When the traced money arrived, or <see langword="null"/>.</param>
    /// <param name="heldBack">The held-back transactions' times.</param>
    public void ShowLens(
        DateTimeOffset centre, int hours, DateTimeOffset? arrival, IReadOnlyList<DateTimeOffset> heldBack)
    {
        ArgumentNullException.ThrowIfNull(heldBack);
        _lensCentre = centre;
        LensHours = Math.Max(0, hours);
        LensArrival = arrival;
        LensHeldBack = heldBack;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Steps the lens's window (the chip): ±48 h, ±7 d, ±6 h, and then the whole case without a lens.</summary>
    public void StepLens()
    {
        if (!HasLens)
        {
            return;
        }

        // A window not among the steps (one the selection view had) starts the steps over.
        var next = LensSteps.ToList().IndexOf(LensHours) + 1;
        LensHours = next < LensSteps.Count ? LensSteps[next] : 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the lens away, as when another case is shown.</summary>
    public void ClearLens()
    {
        LensHours = 0;
        LensArrival = null;
        LensHeldBack = [];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets the newest block the case knows, where the strip ends dotted.</summary>
    /// <param name="tip">The block, or <see langword="null"/>.</param>
    public void ShowNewestBlock(ChainTip? tip)
    {
        if (NewestBlock == tip)
        {
            return;
        }

        NewestBlock = tip;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes a window as the chip does.</summary>
    /// <param name="hours">The hours on either side.</param>
    /// <returns>For example <c>±48 h</c> or <c>±7 d</c>.</returns>
    public static string WindowName(int hours) => hours % 24 == 0 && hours > 48
        ? string.Create(CultureInfo.InvariantCulture, $"±{hours / 24} d")
        : string.Create(CultureInfo.InvariantCulture, $"±{hours} h");
}
