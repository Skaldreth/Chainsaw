using System.Globalization;
using Chainsaw.Modules.Network.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The proxy under Windows on the settings' page Nätverk (sketch PX1a): the proxy Windows is set up with, as it is
/// (Chainsaw changes nothing in Windows), and the row <em>Logga in med Windows-kontot</em>, which only a proxy set by
/// hand gets. Turned on, it folds out what is sent, to whom and what is never sent, since it is a decision about who
/// may learn who the user is. A proxy that asks for a login is said honestly; a proxy found with WPAD or a script gets
/// no switch at all, only why. The background watching follows the choice. It knows nothing of WPF.
/// </summary>
public sealed partial class ProxyLoginViewModel : ObservableObject
{
    /// <summary>What the chip beside a proxy set by hand says.</summary>
    public const string ManualText = "inställd för hand";

    /// <summary>What the chip beside a proxy Windows finds by itself says.</summary>
    public const string AutomaticText = "hittas automatiskt";

    /// <summary>What stands in place of an address that Windows finds by itself.</summary>
    public const string AutomaticAddressText = "WPAD eller skript";

    /// <summary>What the row says when Windows has no proxy.</summary>
    public const string NoProxyText = "Ingen";

    /// <summary>What the line under the proxy says while the computer reaches the internet.</summary>
    public const string ReachesText = "Når internet genom proxyn";

    /// <summary>What it says while the proxy wants a login it does not get.</summary>
    public const string WantsLoginText = "Når inte internet: proxyn vill ha inloggning";

    /// <summary>What it says while the computer does not reach the internet for another reason.</summary>
    public const string OfflineText = "Når inte internet";

    /// <summary>What the background's row says while it logs in as the user.</summary>
    public const string WatchLogsInText = "Loggar in som du";

    /// <summary>What it says while the login is off.</summary>
    public const string WatchWithoutText = "Utan inloggning";

    /// <summary>What it says while the proxy refuses.</summary>
    public const string WatchWaitsText = "Väntar på proxyn";

    /// <summary>What the folded-out rows say is never sent, with its quiet tail.</summary>
    private const string NeverText = "Till källorna, till en proxy från WPAD ";

    /// <summary>The quiet tail of <see cref="NeverText"/>.</summary>
    private const string NeverSubText =
        "eller till en annan adress om proxyn byts i Windows. Då är raden av igen tills du slår på den.";

    /// <summary>The quiet tail of the row that says to whom.</summary>
    private const string ToSubText =
        "och bara när den svarar 407. Proxyn ser vilken källa du frågar, men inte svaret: det är https hela vägen.";

    /// <summary>The proxy and the login.</summary>
    private readonly IWindowsProxy _proxy;

    /// <summary>Whether the computer reaches the internet.</summary>
    private readonly INetworkPresence _presence;

    /// <summary>The time zone the refusal's time is said in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>The proxy's address set by hand, when there is one; the login is turned on for it.</summary>
    private string? _address;

    /// <summary>Creates the rows and follows the login and the network.</summary>
    /// <param name="proxy">The proxy and the login.</param>
    /// <param name="presence">Whether the computer reaches the internet.</param>
    /// <param name="ui">The UI thread, where the changes are shown.</param>
    /// <param name="account">The Windows account, as <c>DOMÄN\namn</c>, said in what is sent.</param>
    /// <param name="zone">The time zone; the computer's when <see langword="null"/>.</param>
    public ProxyLoginViewModel(
        IWindowsProxy proxy, INetworkPresence presence, IUiThread ui, string account, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(ui);
        _proxy = proxy;
        _presence = presence;
        _zone = zone ?? TimeZoneInfo.Local;
        Account = account ?? string.Empty;
        proxy.Changed += (_, _) => ui.Post(Show);
        presence.Changed += (_, _) => ui.Post(Show);
        Show();
    }

    /// <summary>Raised after the user turned the login on or off, with the proxy or <see langword="null"/>.</summary>
    public event EventHandler<string?>? LoginChosen;

    /// <summary>Raised when the user asks for Windows' own proxy settings (<em>Proxy i Windows</em>).</summary>
    public event EventHandler? WindowsSettingsWanted;

    /// <summary>Gets the Windows account said in what is sent.</summary>
    public string Account { get; }

    /// <summary>Gets whether Windows has a proxy at all; without one only the row <em>Proxy</em> says so.</summary>
    [ObservableProperty]
    public partial bool HasProxy { get; private set; }

    /// <summary>Gets the proxy's address, or what stands in its place.</summary>
    [ObservableProperty]
    public partial string AddressText { get; private set; } = NoProxyText;

    /// <summary>Gets the chip beside the address: set by hand or found automatically; empty without a proxy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHow))]
    public partial string HowText { get; private set; } = string.Empty;

    /// <summary>Gets whether the chip is shown.</summary>
    public bool HasHow => HowText.Length > 0;

    /// <summary>Gets whether Windows finds the proxy by itself (the chip is amber, the dashed card says why).</summary>
    [ObservableProperty]
    public partial bool IsAutomatic { get; private set; }

    /// <summary>Gets whether the proxy is set by hand, so that the row <em>Logga in</em> is shown.</summary>
    [ObservableProperty]
    public partial bool CanLogin { get; private set; }

    /// <summary>Gets whether the login is on and applies now.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; private set; }

    /// <summary>Gets the switch's tip.</summary>
    public string SwitchTip => IsOn ? "Stäng av" : "Slå på";

    /// <summary>Gets whether the proxy refused with <c>407</c> since something last went through it.</summary>
    [ObservableProperty]
    public partial bool IsRefused { get; private set; }

    /// <summary>Gets what the red box says about the refusal.</summary>
    [ObservableProperty]
    public partial string RefusedText { get; private set; } = string.Empty;

    /// <summary>Gets the line under the proxy: whether the computer reaches the internet through it.</summary>
    [ObservableProperty]
    public partial string NetText { get; private set; } = string.Empty;

    /// <summary>Gets whether the line is green.</summary>
    [ObservableProperty]
    public partial bool NetIsGood { get; private set; }

    /// <summary>Gets what the background's row says.</summary>
    [ObservableProperty]
    public partial string WatchText { get; private set; } = WatchWithoutText;

    /// <summary>Gets whether the background waits for the proxy (amber).</summary>
    [ObservableProperty]
    public partial bool WatchWaits { get; private set; }

    /// <summary>Gets the folded-out rows: what is sent, to whom and what never.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ProxyTell> Tells { get; private set; } = [];

    /// <summary>Reads Windows' setting anew when the page is shown, and turns off a login whose proxy moved.</summary>
    public void Refresh()
    {
        if (_proxy.LoginFor is not null && !_proxy.IsLoginOn())
        {
            // The proxy was changed in Windows: the row is off again until the user turns it on (sketch PX1a).
            _proxy.AllowLogin(null);
            LoginChosen?.Invoke(this, null);
        }

        Show();
    }

    /// <summary>The switch: turns the login on for the proxy set by hand, or off.</summary>
    [RelayCommand]
    public void Toggle()
    {
        var turnOn = !IsOn;
        if (turnOn && (!CanLogin || _address is null))
        {
            return;
        }

        var proxy = turnOn ? _address : null;
        _proxy.AllowLogin(proxy);
        LoginChosen?.Invoke(this, proxy);
        Show();
    }

    /// <summary><em>Proxy i Windows</em>: asks for Windows' own proxy settings.</summary>
    [RelayCommand]
    public void OpenWindowsSettings() => WindowsSettingsWanted?.Invoke(this, EventArgs.Empty);

    /// <summary>Shows the setting, the login, the refusal and the network as they are now.</summary>
    private void Show()
    {
        var setting = _proxy.Read();
        _address = setting.Kind == WindowsProxyKind.Manual ? setting.Address : null;
        HasProxy = setting.Kind != WindowsProxyKind.None;
        IsAutomatic = setting.Kind == WindowsProxyKind.Automatic;
        CanLogin = _address is not null;
        AddressText = setting.Kind switch
        {
            WindowsProxyKind.Manual => setting.Address ?? NoProxyText,
            WindowsProxyKind.Automatic => AutomaticAddressText,
            _ => NoProxyText,
        };
        HowText = setting.Kind switch
        {
            WindowsProxyKind.Manual => ManualText,
            WindowsProxyKind.Automatic => AutomaticText,
            _ => string.Empty,
        };
        IsOn = CanLogin && _proxy.IsLoginOn();
        OnPropertyChanged(nameof(SwitchTip));
        ShowRefusal();
        Tells = IsOn ? TellsFor(_address!) : [];
    }

    /// <summary>Shows the refusal, the network line and the background's row.</summary>
    private void ShowRefusal()
    {
        var askedAt = HasProxy ? _proxy.LoginAskedAt : null;
        IsRefused = askedAt is not null;
        var time = askedAt is { } at
            ? TimeZoneInfo.ConvertTime(at, _zone).ToString("HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;
        RefusedText = IsOn
            ? $"Proxyn godtog inte inloggningen kl. {time}, så inga uppslag görs."
            : $"Proxyn kräver inloggning. Den svarade 407 kl. {time}, så inga uppslag görs.";
        (NetText, NetIsGood) = (IsRefused, _presence.IsAvailable) switch
        {
            (true, _) => (WantsLoginText, false),
            (false, true) => (ReachesText, true),
            _ => (OfflineText, false),
        };
        WatchWaits = IsRefused;
        WatchText = (IsRefused, IsOn) switch
        {
            (true, _) => WatchWaitsText,
            (false, true) => WatchLogsInText,
            _ => WatchWithoutText,
        };
    }

    /// <summary>Gives the folded-out rows for a proxy.</summary>
    /// <param name="address">The proxy.</param>
    /// <returns>What is sent, to whom and what never.</returns>
    private IReadOnlyList<ProxyTell> TellsFor(string address) =>
    [
        new(ProxyTellKind.Sent, "Skickas",
            $"En Kerberos-biljett eller ett NTLM-svar som visar att du är {Account}. ",
            "Windows räknar ut det; lösenordet lämnar aldrig datorn."),
        new(ProxyTellKind.To, "Till", $"Bara {address}, ", ToSubText),
        new(ProxyTellKind.Never, "Aldrig", NeverText, NeverSubText),
    ];
}

/// <summary>Which of the folded-out rows a row is, for its icon.</summary>
public enum ProxyTellKind
{
    /// <summary>What is sent: the key.</summary>
    Sent,

    /// <summary>To whom: the proxy.</summary>
    To,

    /// <summary>What is never sent: the shield with a cross, toned.</summary>
    Never,
}

/// <summary>A folded-out row of the login (sketch PX1a).</summary>
/// <param name="Kind">Which row, for its icon.</param>
/// <param name="Label">The quiet label to the left.</param>
/// <param name="Text">What it says.</param>
/// <param name="SubText">The quiet tail after it.</param>
public sealed record ProxyTell(ProxyTellKind Kind, string Label, string Text, string SubText);
