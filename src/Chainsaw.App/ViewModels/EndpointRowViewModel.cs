using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>The state of the own node's certificate on its row (sketch KL3b).</summary>
public enum EndpointPin
{
    /// <summary>Not the own node: no pin.</summary>
    None,

    /// <summary>The own node, whose certificate is not pinned: the amber pin struck through.</summary>
    Unpinned,

    /// <summary>Its certificate is pinned and shown: the green pin.</summary>
    Pinned,

    /// <summary>It showed another certificate than the pinned one and is refused: the red pin.</summary>
    Changed,
}

/// <summary>An endpoint's dot on the page Källor (sketches KL1a and KL2b).</summary>
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

/// <summary>What the small mark beside an endpoint's name says, deciding its icon and colour (sketch KL2b).</summary>
public enum EndpointNote
{
    /// <summary>No mark.</summary>
    None,

    /// <summary>It lags behind in height (↓ <c>3 block efter</c>, in the warning colour); only in Snabbast.</summary>
    Behind,

    /// <summary>It asked Chainsaw to wait (the clock, <c>Väntar</c>, in the warning colour).</summary>
    Waiting,

    /// <summary>It answered wrongly or not at all (×, <c>Svarar inte</c>, in the error colour).</summary>
    Failed,
}

/// <summary>
/// An endpoint on the page Källor (sketch KL2b): the dot, the name, ★ for the one used, the house for the user's own
/// node, the mark for one behind, waiting or failing, the time of the latest probe, the height and the hash it
/// answered, the switch and ×, and in Prioriterad its place among those switched on. The same instance is kept for an
/// endpoint while the page shows its chain, whether it is the one used, a reserve or switched off, so that moving it
/// keeps its focus. It knows nothing of WPF.
/// </summary>
/// <param name="id">The endpoint's id.</param>
public sealed partial class EndpointRowViewModel(Guid id) : ObservableObject
{
    /// <summary>The mark of an endpoint that asked Chainsaw to wait.</summary>
    public const string WaitingText = "Väntar";

    /// <summary>The mark of an endpoint that answered wrongly or not at all.</summary>
    public const string FailedText = "Svarar inte";

    /// <summary>What an endpoint that has not been tried says instead of its height.</summary>
    public const string UntriedText = "Inte provad än";

    /// <summary>What the mark <em>3 block efter</em> means, on hovering.</summary>
    public const string BehindTip = "Används inte förrän den är i fas. Ett block efter räknas inte som efter.";

    /// <summary>What the house beside a name means, on hovering.</summary>
    public const string LocalTip =
        "Egen nod på datorn eller ett privat nät. Uppslagen går direkt dit, aldrig genom utgången.";

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

    /// <summary>Gets the time of the latest probe as the number alone, such as <c>182</c>, or a dash.</summary>
    [ObservableProperty]
    public partial string MillisecondsNumber { get; private set; } = string.Empty;

    /// <summary>Gets whether the latest probe has a time, so that the used card writes <c>ms</c> after it.</summary>
    [ObservableProperty]
    public partial bool HasMilliseconds { get; private set; }

    /// <summary>
    /// Gets what the latest probe answered: <c>Höjd 921 404 · 0000…3f2a</c> (the hash only when it was asked for), why
    /// it failed, or that it has not been tried.
    /// </summary>
    [ObservableProperty]
    public partial string AnswerText { get; private set; } = string.Empty;

    /// <summary>Gets whether the next lookup asks it first (★).</summary>
    [ObservableProperty]
    public partial bool IsInUse { get; private set; }

    /// <summary>Gets what ★ means in the chain's mode.</summary>
    [ObservableProperty]
    public partial string InUseTip { get; private set; } = string.Empty;

    /// <summary>Gets the mark's kind, which decides its icon and colour.</summary>
    [ObservableProperty]
    public partial EndpointNote NoteKind { get; private set; }

    /// <summary>Gets the mark's words, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; private set; } = string.Empty;

    /// <summary>Gets the mark's explanation, on hovering.</summary>
    [ObservableProperty]
    public partial string NoteTip { get; private set; } = string.Empty;

    /// <summary>Gets whether the mark is shown.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>
    /// Gets whether the user marked it as their own node, asked straight there and never through the exit.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLocal { get; private set; }

    /// <summary>Gets whether it sends an API key from the vault, shown as a key on a row switched off.</summary>
    [ObservableProperty]
    public partial bool HasKey { get; private set; }

    /// <summary>Gets whether the endpoint is switched on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchTip))]
    public partial bool IsOn { get; private set; }

    /// <summary>Gets what the switch does.</summary>
    public string SwitchTip => IsOn ? "Stäng av" : "Slå på";

    /// <summary>Gets the place among those switched on, from 1; empty for one switched off.</summary>
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

    /// <summary>Gets the latest probe's time in milliseconds, so that Snabbast's reserves are ordered by it.</summary>
    public int? Milliseconds { get; private set; }

    /// <summary>
    /// Gets the state of the own node's certificate, the pin after its name in the state's colour (sketch KL3b): amber
    /// when it is not pinned, green when it is, red when the node shows another; none for an endpoint not marked local.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPin), nameof(PinWord), nameof(PinTip))]
    public partial EndpointPin Pin { get; private set; }

    /// <summary>Gets whether the row has the pin: only an endpoint marked local.</summary>
    public bool HasPin => Pin != EndpointPin.None;

    /// <summary>Gets the pin's word on a reserve's row.</summary>
    public string PinWord => Pin switch
    {
        EndpointPin.Pinned => "Fäst",
        EndpointPin.Changed => "Bytt",
        _ => "Inte fäst",
    };

    /// <summary>Gets the pin's tip.</summary>
    public string PinTip => Pin switch
    {
        EndpointPin.Pinned => "Certifikatet är fäst",
        EndpointPin.Changed => "Certifikatet har bytts sedan det fästes",
        _ => "Certifikatet är inte fäst. Klicka för att se det.",
    };

    /// <summary>Gets or sets whether the pin's box is folded out at this row; the box closes it by itself.</summary>
    [ObservableProperty]
    public partial bool IsPinOpen { get; set; }

    /// <summary>Shows the endpoint as it is now.</summary>
    /// <param name="view">The endpoint with its probe.</param>
    /// <param name="mode">The chain's mode.</param>
    /// <param name="place">Its place in the list, from 0, and how many the list has.</param>
    /// <param name="position">Its place among those switched on, from 1, or 0 when it is switched off.</param>
    /// <param name="timeZone">
    /// The time zone a wait's end is shown in; the computer's when <see langword="null"/>.
    /// </param>
    public void Show(
        EndpointView view, SourceMode mode, (int Index, int Count) place, int position, TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var endpoint = view.Endpoint;
        var probe = view.Probe;
        Name = endpoint.Name;
        IsLocal = endpoint.Local;
        Pin = PinOf(view);
        HasKey = endpoint.ApiKey is not null;
        Address = endpoint.Address.AbsoluteUri.TrimEnd('/');
        IsOn = endpoint.Enabled;
        (Dot, DotTip) = DotFor(view);
        Milliseconds = probe?.Duration is { } duration ? (int)Math.Round(duration.TotalMilliseconds) : null;
        HasMilliseconds = Milliseconds is not null;
        MillisecondsNumber = Milliseconds?.ToString(CultureInfo.InvariantCulture) ?? "–";
        MillisecondsText = HasMilliseconds ? MillisecondsNumber + " ms" : "–";
        AnswerText = AnswerFor(probe);
        IsInUse = view.InUse;
        InUseTip = mode == SourceMode.Fastest ? "Snabbast just nu, används" : "Används nu";
        (NoteKind, Note, NoteTip) = NoteFor(view, mode, timeZone);
        Position = position > 0 ? position.ToString(CultureInfo.InvariantCulture) : string.Empty;
        IsPriority = mode == SourceMode.Priority;
        CanMoveUp = place.Index > 0;
        CanMoveDown = place.Index < place.Count - 1;
    }

    /// <summary>Gives the state of an endpoint's certificate.</summary>
    /// <param name="view">The endpoint.</param>
    /// <returns>None for one not marked local; else whether its certificate is pinned, and still shown.</returns>
    public static EndpointPin PinOf(EndpointView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!view.Endpoint.Local)
        {
            return EndpointPin.None;
        }

        if (view.Endpoint.Pin is null)
        {
            return EndpointPin.Unpinned;
        }

        return view.CertificateRefused ? EndpointPin.Changed : EndpointPin.Pinned;
    }

    /// <summary>Formats a height with a space between the thousands, such as <c>921 404</c>.</summary>
    /// <param name="height">The height.</param>
    /// <returns>The words.</returns>
    public static string HeightText(long height) =>
        height.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    /// <summary>Shortens a block's hash to its first eight and last four characters: <c>00000000…3f2a</c>.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns>The short hash, or the hash as it is when it is short.</returns>
    public static string ShortHash(string hash) => hash is { Length: > 12 } ? $"{hash[..8]}…{hash[^4..]}" : hash;

    /// <summary>
    /// Says what the latest probe answered. Only what was measured is said: the probe at the start asks only for the
    /// height, so the hash is written only when it is known (CLAUDE.md: never show made-up data).
    /// </summary>
    /// <param name="probe">The probe, or <see langword="null"/>.</param>
    /// <returns>The words.</returns>
    private static string AnswerFor(EndpointProbe? probe)
    {
        if (probe?.Height is not { } height)
        {
            return probe?.Problem is { } problem ? "Svarade inte rätt: " + problem : UntriedText;
        }

        var text = "Höjd " + HeightText(height);
        return probe.BlockHash is { } hash ? text + " · " + ShortHash(hash) : text;
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
            ProbeOutcome.Answered when view.Probe.BlocksBehind is > 0 =>
                (EndpointDot.Answered, "Svarar rätt; ett block efter räknas inte som efter"),
            ProbeOutcome.Answered => (EndpointDot.Answered, "Svarar rätt"),
            ProbeOutcome.Behind => (EndpointDot.Behind, "Svarar, men på en lägre höjd"),
            ProbeOutcome.Failed => (EndpointDot.Failed, "Svarade inte rätt: " + view.Probe.Problem),
            ProbeOutcome.NotSent => (EndpointDot.Untried, "Inte provad: " + view.Probe.Problem),
            _ => (EndpointDot.Untried, UntriedText),
        };
    }

    /// <summary>
    /// Gives the mark: a wait the source asked for first, since it says when it comes back; then one that fails; then
    /// one that lags behind, only in Snabbast, since the user's order holds in Prioriterad.
    /// </summary>
    /// <param name="view">The endpoint with its probe.</param>
    /// <param name="mode">The chain's mode.</param>
    /// <param name="timeZone">The time zone a wait's end is shown in.</param>
    /// <returns>The kind, the words and the words on hovering.</returns>
    private static (EndpointNote Kind, string Text, string Tip) NoteFor(
        EndpointView view, SourceMode mode, TimeZoneInfo? timeZone)
    {
        if (!view.Endpoint.Enabled || mode == SourceMode.Off)
        {
            return (EndpointNote.None, string.Empty, string.Empty);
        }

        // A source that asked Chainsaw to wait is left for the others until then, honestly marked; it is not failed.
        if (view.WaitUntil is { } until)
        {
            var local = TimeZoneInfo.ConvertTime(until, timeZone ?? TimeZoneInfo.Local);
            var tip = $"Källan bad Chainsaw vänta till {local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}. "
                + "De andra frågas under tiden.";
            return (EndpointNote.Waiting, WaitingText, tip);
        }

        var probe = view.Probe;
        if (view.FailedSince || probe?.Outcome == ProbeOutcome.Failed)
        {
            return (EndpointNote.Failed, FailedText, DotFor(view).Tip);
        }

        return probe?.Outcome == ProbeOutcome.Behind && mode == SourceMode.Fastest
            ? (EndpointNote.Behind, BehindText(probe), BehindTip)
            : (EndpointNote.None, string.Empty, string.Empty);
    }

    /// <summary>Says how far behind an endpoint is, such as <c>2 block efter</c>.</summary>
    /// <param name="probe">Its probe, marked behind.</param>
    /// <returns>The words.</returns>
    private static string BehindText(EndpointProbe probe) =>
        probe.BlocksBehind is { } blocks
            ? blocks.ToString(CultureInfo.InvariantCulture) + " block efter"
            : "Efter";
}
