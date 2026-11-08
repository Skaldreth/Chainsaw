using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>Where the box of the own node's certificate stands (sketch KL3b).</summary>
public enum NodePinState
{
    /// <summary>The certificate is being read from the node.</summary>
    Reading,

    /// <summary>Not pinned: the certificate the node shows, with <em>Fäst certifikatet</em>.</summary>
    Unpinned,

    /// <summary>Pinned and shown: the pinned certificate, with <em>Ta bort fästet …</em>.</summary>
    Pinned,

    /// <summary>The node shows another certificate than the pinned one: the two side by side.</summary>
    Changed,

    /// <summary>The node did not answer and nothing is pinned, so there is nothing to pin.</summary>
    Unreachable,
}

/// <summary>The colour of a fingerprint's picture, its words and the box's state.</summary>
public enum NodePinTone
{
    /// <summary>Toned: an old pin beside the new certificate.</summary>
    Faded,

    /// <summary>Green: pinned.</summary>
    Good,

    /// <summary>Red: another certificate than the pinned one.</summary>
    Bad,

    /// <summary>Amber: not pinned yet.</summary>
    Amber,
}

/// <summary>
/// A certificate's fingerprint as a picture: a grid of eight by four cells, one per byte, its lightness from the
/// byte, so that two certificates compare by eye before a digit is read (sketch KL3b).
/// </summary>
/// <param name="Cells">The cells' colours, as <c>#RRGGBB</c>, by row.</param>
/// <param name="Label">The quiet words under it, such as <em>Visar nu</em>.</param>
/// <param name="Short">The fingerprint's first and last two bytes.</param>
/// <param name="Tone">Its colour.</param>
public sealed record FingerprintArt(IReadOnlyList<string> Cells, string Label, string Short, NodePinTone Tone)
{
    /// <summary>The eight tones a cell can have, from the dark of the surface to the cream of the text.</summary>
    private static readonly string[] Tones =
        ["#33373A", "#404346", "#545A5E", "#6B7276", "#868D91", "#A3AAAD", "#C9CDCF", "#FBF6F0"];

    /// <summary>Draws a fingerprint.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="label">The words under it.</param>
    /// <param name="tone">Its colour.</param>
    /// <returns>The picture.</returns>
    public static FingerprintArt Of(string fingerprint, string label, NodePinTone tone) => new(
        [.. CertificatePin.Bytes(fingerprint).Select(value => Tones[value >> 5])],
        label,
        CertificatePin.Short(fingerprint),
        tone);
}

/// <summary>
/// The box at the pin of the own node's row on the page Källor (sketch KL3b): the certificate as a picture and its
/// fingerprint, who issued it and how long it is valid; <em>Fäst certifikatet</em> when it is not pinned, <em>Ta bort
/// fästet …</em> when it is, and when the node shows another certificate the pinned and the new side by side, with
/// <em>Lita på det nya …</em>, which asks first. The pin is kept in the program's data with when and by whom, never in
/// a case. It knows nothing of WPF.
/// </summary>
public sealed partial class NodePinViewModel : ObservableObject
{
    /// <summary>The box's note when the node shows another certificate.</summary>
    public const string ChangedNote =
        "Inga uppslag går till noden förrän du har bestämt dig. De andra endpointarna används under tiden.";

    /// <summary>The words beside the two pictures when they differ.</summary>
    public const string PairText = "Ett nytt certifikat på noden, eller någon annan på nätet.";

    /// <summary>The question before the new certificate is trusted.</summary>
    public const string TrustQuestion =
        "Lita på det nya? Gör det bara om du själv har bytt certifikatet på noden och bilden stämmer med det noden "
        + "visar. Det gamla sparas, med tid och vem som bytte.";

    /// <summary>The question before the pin is removed.</summary>
    public const string UnpinQuestion =
        "Ta bort fästet? Noden kontrolleras då mot rötterna i Windows igen, och ett självsignerat certifikat vägras.";

    /// <summary>What the box says when the node did not answer and nothing is pinned.</summary>
    public const string UnreachableNote =
        "Noden svarar inte, så det finns inget certifikat att fästa. Försök igen sedan.";

    /// <summary>The Swedish culture the dates are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>The chains' endpoints, where the pin is kept.</summary>
    private readonly IChainSources _sources;

    /// <summary>Gives the operator's name, who pins.</summary>
    private readonly Func<string> _operator;

    /// <summary>The clock, for <em>i dag</em>.</summary>
    private readonly IClock _clock;

    /// <summary>The time zone the times are written in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>The certificate the node showed, when it answered.</summary>
    private NodeCertificate? _shown;

    /// <summary>The pin when the box was read.</summary>
    private EndpointCertificate? _pin;

    /// <summary>Creates the box for an endpoint marked local.</summary>
    /// <param name="sources">The chains' endpoints.</param>
    /// <param name="chain">The endpoint's chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="host">The node's host, as the box names it.</param>
    /// <param name="operatorName">Gives the operator's name, kept with the pin.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="zone">The time zone; the computer's when <see langword="null"/>.</param>
    public NodePinViewModel(
        IChainSources sources,
        Chain chain,
        Guid id,
        string host,
        Func<string> operatorName,
        IClock clock,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(operatorName);
        ArgumentNullException.ThrowIfNull(clock);
        _sources = sources;
        _operator = operatorName;
        _clock = clock;
        _zone = zone ?? TimeZoneInfo.Local;
        Chain = chain;
        Id = id;
        Host = host ?? string.Empty;
    }

    /// <summary>Raised after the pin was changed, with the words the toaster says.</summary>
    public event EventHandler<string>? Pinned;

    /// <summary>Gets the endpoint's chain.</summary>
    public Chain Chain { get; }

    /// <summary>Gets the endpoint.</summary>
    public Guid Id { get; }

    /// <summary>Gets the node's host.</summary>
    public string Host { get; }

    /// <summary>Gets where the box stands.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingle), nameof(IsPair), nameof(CanPin), nameof(CanTrust))]
    [NotifyPropertyChangedFor(nameof(CanUnpin), nameof(IsReading))]
    public partial NodePinState State { get; private set; } = NodePinState.Reading;

    /// <summary>Gets whether the certificate is being read.</summary>
    public bool IsReading => State == NodePinState.Reading;

    /// <summary>Gets whether one certificate is shown, with who issued it and how long it is valid.</summary>
    public bool IsSingle => State is NodePinState.Unpinned or NodePinState.Pinned;

    /// <summary>Gets whether the pinned and the new certificate stand side by side.</summary>
    public bool IsPair => State == NodePinState.Changed;

    /// <summary>Gets whether <em>Fäst certifikatet</em> is offered.</summary>
    public bool CanPin => State == NodePinState.Unpinned;

    /// <summary>Gets whether <em>Lita på det nya …</em> is offered.</summary>
    public bool CanTrust => State == NodePinState.Changed && !IsConfirming;

    /// <summary>Gets whether <em>Ta bort fästet …</em> is offered.</summary>
    public bool CanUnpin => State == NodePinState.Pinned && !IsConfirming;

    /// <summary>Gets whether the box asks before trusting the new certificate or removing the pin.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTrust), nameof(CanUnpin))]
    public partial bool IsConfirming { get; private set; }

    /// <summary>Gets the question asked.</summary>
    [ObservableProperty]
    public partial string Question { get; private set; } = string.Empty;

    /// <summary>Gets the confirming button's words.</summary>
    [ObservableProperty]
    public partial string ConfirmText { get; private set; } = string.Empty;

    /// <summary>Gets the pictures: one, or the pinned and the new.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FingerprintArt> Arts { get; private set; } = [];

    /// <summary>Gets the state's words at the top of the box.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Läser certifikatet …";

    /// <summary>Gets the state's colour.</summary>
    [ObservableProperty]
    public partial NodePinTone StatusTone { get; private set; } = NodePinTone.Faded;

    /// <summary>Gets the quiet words at the right of the state: when and by whom, or that it is self-signed.</summary>
    [ObservableProperty]
    public partial string WhenText { get; private set; } = string.Empty;

    /// <summary>Gets who issued the certificate.</summary>
    [ObservableProperty]
    public partial string IssuerText { get; private set; } = string.Empty;

    /// <summary>Gets how long it is valid.</summary>
    [ObservableProperty]
    public partial string ValidText { get; private set; } = string.Empty;

    /// <summary>Gets the note under the pictures.</summary>
    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    /// <summary>Gets the whole fingerprint in pairs, for <em>Kopiera</em>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopy))]
    public partial string CopyText { get; private set; } = string.Empty;

    /// <summary>Gets whether there is a fingerprint to copy.</summary>
    public bool HasCopy => CopyText.Length > 0;

    /// <summary>Gets what went wrong last, in plain words.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether something went wrong.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>
    /// Reads the pin and the certificate the node shows, off the UI thread for the node, and shows them; only the TLS
    /// handshake is made, straight to the node in the user's own network.
    /// </summary>
    /// <returns>A task that completes when the box is shown.</returns>
    public async Task LoadAsync()
    {
        State = NodePinState.Reading;
        _pin = PinNow();
        try
        {
            _shown = await _sources.ReadCertificateAsync(Chain, Id).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ExitException or ArgumentException)
        {
            _shown = null;
        }

        Show();
    }

    /// <summary><em>Fäst certifikatet</em>: pins the certificate the node shows.</summary>
    /// <returns>A task that completes when the pin is written.</returns>
    [RelayCommand]
    public Task PinAsync() =>
        _shown is { } shown && CanPin
            ? KeepAsync(shown.Fingerprint, $"Certifikatet för {Host} är fäst")
            : Task.CompletedTask;

    /// <summary><em>Lita på det nya …</em>: asks before the new certificate is trusted.</summary>
    [RelayCommand]
    public void AskTrust() => Ask(CanTrust, TrustQuestion, "Lita på det nya");

    /// <summary><em>Ta bort fästet …</em>: asks before the pin is removed.</summary>
    [RelayCommand]
    public void AskUnpin() => Ask(CanUnpin, UnpinQuestion, "Ta bort fästet");

    /// <summary><em>Avbryt</em>: leaves the question.</summary>
    [RelayCommand]
    public void CancelConfirm()
    {
        IsConfirming = false;
        Question = ConfirmText = string.Empty;
    }

    /// <summary>Answers the question: trusts the new certificate, or removes the pin.</summary>
    /// <returns>A task that completes when the change is written.</returns>
    [RelayCommand]
    public async Task ConfirmAsync()
    {
        if (!IsConfirming)
        {
            return;
        }

        var trusting = State == NodePinState.Changed;
        CancelConfirm();
        if (trusting && _shown is { } shown)
        {
            await KeepAsync(shown.Fingerprint, $"Det nya certifikatet för {Host} är fäst").ConfigureAwait(true);
        }
        else if (!trusting)
        {
            await ChangeAsync(
                () => _sources.UnpinAsync(Chain, Id, Operator()), $"Fästet för {Host} är borttaget")
                .ConfigureAwait(true);
        }
    }

    /// <summary>Marks the fingerprint as copied; the view puts it on the clipboard.</summary>
    public void MarkCopied() => ErrorText = string.Empty;

    /// <summary>Asks a question, when the action is offered.</summary>
    /// <param name="offered">Whether the action is offered now.</param>
    /// <param name="question">The question.</param>
    /// <param name="confirm">The confirming button's words.</param>
    private void Ask(bool offered, string question, string confirm)
    {
        if (!offered)
        {
            return;
        }

        (Question, ConfirmText) = (question, confirm);
        IsConfirming = true;
    }

    /// <summary>Pins a fingerprint and shows the box anew.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="said">What the toaster says.</param>
    /// <returns>A task that completes when the pin is written.</returns>
    private Task KeepAsync(string fingerprint, string said) =>
        ChangeAsync(() => _sources.PinAsync(Chain, Id, fingerprint, Operator()), said);

    /// <summary>Changes the pin, shows the box anew and says it, or says why it could not be written.</summary>
    /// <param name="change">The change.</param>
    /// <param name="said">What the toaster says.</param>
    /// <returns>A task that completes when the change is written.</returns>
    private async Task ChangeAsync(Func<Task> change, string said)
    {
        ErrorText = string.Empty;
        try
        {
            await change().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            // The change holds in memory until Chainsaw closes; only the file could not be written.
            ErrorText = SourcesViewModel.SaveFailedText;
        }

        _pin = PinNow();
        Show();
        Pinned?.Invoke(this, said);
    }

    /// <summary>Gives the operator's name, or the Windows user's when the profile has none.</summary>
    /// <returns>The name.</returns>
    private string Operator()
    {
        var name = _operator();
        return string.IsNullOrWhiteSpace(name) ? Environment.UserName : name.Trim();
    }

    /// <summary>Reads the endpoint's pin as the sources hold it now.</summary>
    /// <returns>The pin, or <see langword="null"/>.</returns>
    private EndpointCertificate? PinNow() =>
        _sources.Get(Chain).Endpoints.FirstOrDefault(view => view.Endpoint.Id == Id)?.Endpoint.Pin;

    /// <summary>Shows the box from the pin and the certificate the node showed.</summary>
    private void Show()
    {
        CopyText = (_shown?.Fingerprint ?? _pin?.Fingerprint) is { } copy ? CertificatePin.Grouped(copy) : string.Empty;
        State = (_pin, _shown) switch
        {
            (null, null) => NodePinState.Unreachable,
            (null, _) => NodePinState.Unpinned,
            ({ } pin, { } shown) when pin.Fingerprint != shown.Fingerprint => NodePinState.Changed,
            _ => NodePinState.Pinned,
        };
        switch (State)
        {
            case NodePinState.Unpinned:
                ShowUnpinned(_shown!);
                break;
            case NodePinState.Changed:
                ShowChanged(_pin!, _shown!);
                break;
            case NodePinState.Pinned:
                ShowPinned(_pin!);
                break;
            default:
                (Arts, StatusText, StatusTone, WhenText, Note) =
                    ([], "Noden svarar inte", NodePinTone.Amber, string.Empty, UnreachableNote);
                break;
        }

        ShowDetails();
    }

    /// <summary>Shows a certificate that is not pinned.</summary>
    /// <param name="shown">The certificate the node shows.</param>
    private void ShowUnpinned(NodeCertificate shown)
    {
        Arts = [FingerprintArt.Of(shown.Fingerprint, "Visar nu", NodePinTone.Amber)];
        StatusText = shown.TrustedByWindows ? "Betrott av Windows" : "Inte betrott av Windows";
        StatusTone = NodePinTone.Amber;
        WhenText = shown.SelfSigned ? "Självsignerat" : string.Empty;
        Note = shown.TrustedByWindows
            ? $"Fäst det för att lita på just det här certifikatet för {Host}, och på inget annat."
            : "Noden vägras tills certifikatet är fäst. Jämför bilden och fingeravtrycket med det noden själv visar.";
    }

    /// <summary>Shows a pinned certificate that the node still shows (or that could not be read now).</summary>
    /// <param name="pin">The pin.</param>
    private void ShowPinned(EndpointCertificate pin)
    {
        Arts = [FingerprintArt.Of(pin.Fingerprint, "Fäst " + Day(pin.PinnedAt), NodePinTone.Good)];
        StatusText = "Fäst";
        StatusTone = NodePinTone.Good;
        WhenText = $"{DayAndTime(pin.PinnedAt)} · {pin.PinnedBy}";
        Note = _shown is null
            ? $"Noden svarar inte just nu. Chainsaw litar bara på det fästa certifikatet för {Host}."
            : $"Chainsaw litar på just det här certifikatet för {Host}, och på inget annat.";
    }

    /// <summary>Shows the pinned and the new certificate side by side.</summary>
    /// <param name="pin">The pin.</param>
    /// <param name="shown">The certificate the node shows now.</param>
    private void ShowChanged(EndpointCertificate pin, NodeCertificate shown)
    {
        Arts =
        [
            FingerprintArt.Of(pin.Fingerprint, "Fäst " + Day(pin.PinnedAt), NodePinTone.Faded),
            FingerprintArt.Of(shown.Fingerprint, "Visar nu", NodePinTone.Bad),
        ];
        StatusText = "Certifikatet har bytts";
        StatusTone = NodePinTone.Bad;
        WhenText = DayAndTime(_clock.UtcNow);
        Note = ChangedNote;
    }

    /// <summary>Shows who issued the certificate shown and how long it is valid.</summary>
    private void ShowDetails()
    {
        if (_shown is not { } shown)
        {
            (IssuerText, ValidText) = (string.Empty, string.Empty);
            return;
        }

        var issuer = CommonName(shown.Issuer);
        IssuerText = shown.SelfSigned ? $"{issuer}, självsignerat" : issuer;
        ValidText = $"{Date(shown.NotBefore)} – {Date(shown.NotAfter)}";
    }

    /// <summary>Gives a distinguished name's common name, or the whole name when it has none.</summary>
    /// <param name="name">The name, such as <c>CN=esplora.lan, O=…</c>.</param>
    /// <returns>The common name.</returns>
    internal static string CommonName(string name)
    {
        try
        {
            var common = new X500DistinguishedName(name).EnumerateRelativeDistinguishedNames()
                .FirstOrDefault(part => part.GetSingleElementType().Value == "2.5.4.3")?.GetSingleElementValue();
            return string.IsNullOrWhiteSpace(common) ? name : common;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException
                                              or InvalidOperationException)
        {
            return name;
        }
    }

    /// <summary>Gives a day, such as <c>8 nov</c>, or <c>i dag</c>.</summary>
    /// <param name="at">The time, in UTC.</param>
    /// <returns>The words.</returns>
    private string Day(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, _zone);
        return local.Date == TimeZoneInfo.ConvertTime(_clock.UtcNow, _zone).Date
            ? "i dag"
            : local.ToString("d MMM", Swedish).TrimEnd('.');
    }

    /// <summary>Gives a day and a time, such as <c>8 nov 10:42</c> or <c>i dag 10:47</c>.</summary>
    /// <param name="at">The time, in UTC.</param>
    /// <returns>The words.</returns>
    private string DayAndTime(DateTimeOffset at) =>
        $"{Day(at)} {TimeZoneInfo.ConvertTime(at, _zone).ToString("HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>Gives a date, such as <c>14 mar 2026</c>.</summary>
    /// <param name="at">The time, in UTC.</param>
    /// <returns>The words.</returns>
    private string Date(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, _zone).ToString("d MMM yyyy", Swedish)
            .Replace(".", string.Empty, StringComparison.Ordinal);
}
