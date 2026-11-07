using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>An endpoint's dot on the page Källor (sketch KL1a).</summary>
public enum EndpointDot
{
    /// <summary>Green: it answered right when it was last tried.</summary>
    Answered,

    /// <summary>Yellow: it answered, but lags behind the others in height, so it is not used.</summary>
    Behind,

    /// <summary>Red: it answered wrongly or not at all, or a lookup through it failed since.</summary>
    Failed,

    /// <summary>Grey: it is on but has not been tried.</summary>
    Untried,

    /// <summary>Dark grey: it is switched off.</summary>
    Off,
}

/// <summary>
/// An endpoint as a row on the page Källor (sketch KL1a): the dot, the name and address, the time of the latest probe
/// as a number and a small bar, ★ for the one used, a yellow mark for one that lags behind, the switch and ×, and in
/// Prioriterad its place with the handle. The same instance is kept for an endpoint while the page shows its chain,
/// so that moving it keeps its focus. It knows nothing of WPF.
/// </summary>
/// <param name="id">The endpoint's id.</param>
public sealed partial class EndpointRowViewModel(Guid id) : ObservableObject
{
    /// <summary>The longest time the bar shows; a slower answer fills it.</summary>
    public const int BarMilliseconds = 300;

    /// <summary>The bar's width at <see cref="BarMilliseconds"/>.</summary>
    public const double BarMaxWidth = 64;

    /// <summary>The bar's narrowest width, so that a fast answer still shows one.</summary>
    public const double BarMinWidth = 6;

    /// <summary>Gets the endpoint's id.</summary>
    public Guid Id { get; } = id;

    /// <summary>Gets the endpoint's name.</summary>
    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    /// <summary>Gets the endpoint's address.</summary>
    [ObservableProperty]
    public partial string Address { get; private set; } = string.Empty;

    /// <summary>Gets the dot.</summary>
    [ObservableProperty]
    public partial EndpointDot Dot { get; private set; }

    /// <summary>Gets what the dot means, on hovering.</summary>
    [ObservableProperty]
    public partial string DotTip { get; private set; } = string.Empty;

    /// <summary>Gets the time of the latest probe, such as <c>182 ms</c>, or a dash.</summary>
    [ObservableProperty]
    public partial string MillisecondsText { get; private set; } = string.Empty;

    /// <summary>Gets the bar's width for the time, or zero when there is none.</summary>
    [ObservableProperty]
    public partial double BarWidth { get; private set; }

    /// <summary>Gets whether the next lookup asks it first (★).</summary>
    [ObservableProperty]
    public partial bool IsInUse { get; private set; }

    /// <summary>Gets what ★ means in the chain's mode.</summary>
    [ObservableProperty]
    public partial string InUseTip { get; private set; } = string.Empty;

    /// <summary>Gets the yellow mark's words for one that lags behind, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; private set; } = string.Empty;

    /// <summary>Gets the mark's explanation, on hovering.</summary>
    [ObservableProperty]
    public partial string NoteTip { get; private set; } = string.Empty;

    /// <summary>Gets whether the yellow mark is shown.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>Gets whether the endpoint is switched on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchTip))]
    public partial bool IsOn { get; private set; }

    /// <summary>Gets what the switch does.</summary>
    public string SwitchTip => IsOn ? "Stäng av" : "Slå på";

    /// <summary>Gets the place in the user's order, from 1.</summary>
    [ObservableProperty]
    public partial string Position { get; private set; } = string.Empty;

    /// <summary>Gets whether the chain is in Prioriterad, so that the place and the handle show.</summary>
    [ObservableProperty]
    public partial bool IsPriority { get; private set; }

    /// <summary>Gets whether it can move up.</summary>
    [ObservableProperty]
    public partial bool CanMoveUp { get; private set; }

    /// <summary>Gets whether it can move down.</summary>
    [ObservableProperty]
    public partial bool CanMoveDown { get; private set; }

    /// <summary>Shows the endpoint as it is now.</summary>
    /// <param name="view">The endpoint with its probe.</param>
    /// <param name="mode">The chain's mode.</param>
    /// <param name="index">Its place in the list, from 0.</param>
    /// <param name="count">How many the list has.</param>
    public void Show(EndpointView view, SourceMode mode, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(view);
        var endpoint = view.Endpoint;
        var probe = view.Probe;
        Name = endpoint.Name;
        Address = endpoint.Address.AbsoluteUri.TrimEnd('/');
        IsOn = endpoint.Enabled;
        (Dot, DotTip) = DotFor(view);
        var milliseconds = probe?.Duration is { } duration ? (int)Math.Round(duration.TotalMilliseconds) : (int?)null;
        MillisecondsText = milliseconds is { } ms ? ms.ToString(CultureInfo.InvariantCulture) + " ms" : "–";
        BarWidth = milliseconds is { } shown
            ? Math.Max(BarMinWidth, Math.Min(shown, BarMilliseconds) * BarMaxWidth / BarMilliseconds)
            : 0;
        IsInUse = view.InUse;
        InUseTip = mode == SourceMode.Fastest ? "Snabbast just nu, används" : "Används nu";
        // The mark says that Snabbast passes it over; in Prioriterad the user's order holds, so it is not shown there.
        var behind = endpoint.Enabled && probe?.Outcome == ProbeOutcome.Behind && mode == SourceMode.Fastest;
        Note = behind ? BehindText(probe!) : string.Empty;
        NoteTip = behind ? probe!.Problem ?? string.Empty : string.Empty;
        Position = (index + 1).ToString(CultureInfo.InvariantCulture);
        IsPriority = mode == SourceMode.Priority;
        CanMoveUp = index > 0;
        CanMoveDown = index < count - 1;
    }

    /// <summary>Gives the dot and its explanation.</summary>
    /// <param name="view">The endpoint with its probe.</param>
    /// <returns>The dot and the words on hovering.</returns>
    private static (EndpointDot Dot, string Tip) DotFor(EndpointView view)
    {
        if (!view.Endpoint.Enabled)
        {
            return (EndpointDot.Off, "Avstängd");
        }

        if (view.FailedSince)
        {
            return (EndpointDot.Failed, "Ett uppslag genom den misslyckades efter senaste provet");
        }

        return view.Probe?.Outcome switch
        {
            ProbeOutcome.Answered => (EndpointDot.Answered, "Svarar rätt"),
            ProbeOutcome.Behind => (EndpointDot.Behind, "Svarar, men på en lägre höjd"),
            ProbeOutcome.Failed => (EndpointDot.Failed, "Svarade inte rätt: " + view.Probe.Problem),
            ProbeOutcome.NotSent => (EndpointDot.Untried, "Inte provad: " + view.Probe.Problem),
            _ => (EndpointDot.Untried, "Inte provad än"),
        };
    }

    /// <summary>Says how far behind an endpoint is, such as <c>2 block efter</c>.</summary>
    /// <param name="probe">Its probe, marked behind.</param>
    /// <returns>The words.</returns>
    private static string BehindText(EndpointProbe probe) =>
        probe.BlocksBehind is { } blocks
            ? blocks.ToString(CultureInfo.InvariantCulture) + " block efter"
            : "Efter";
}
