using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The proxy under Windows on the page Nätverk (sketch PX1a) and the own node's certificate on the page Källor
/// (sketch KL3b): what the rows say for each kind of proxy, the switch only for a proxy set by hand, the refusal said
/// honestly, and the pin's box with the fingerprint as a picture, the two side by side when it changed, and the
/// question before the new one is trusted.
/// </summary>
public sealed class ProxyAndPinTests
{
    private const string Proxy = "proxy.lan.corp:8080";

    private static readonly string Old = new('A', CertificatePin.Length);

    private static readonly string New = "1F" + new string('E', CertificatePin.Length - 2);

    private readonly TestProxy _proxy = new();

    private readonly TestNetwork _network = new();

    private readonly TestClock _clock = new();

    [Fact]
    public void A_proxy_set_by_hand_gets_the_switch_off_by_default()
    {
        _proxy.Setting = new WindowsProxySetting(WindowsProxyKind.Manual, Proxy);

        var rows = Rows();

        Assert.True(rows.HasProxy);
        Assert.True(rows.CanLogin);
        Assert.False(rows.IsOn);
        Assert.False(rows.IsAutomatic);
        Assert.Equal(Proxy, rows.AddressText);
        Assert.Equal(ProxyLoginViewModel.ManualText, rows.HowText);
        Assert.Empty(rows.Tells);
        Assert.Equal(ProxyLoginViewModel.ReachesText, rows.NetText);
        Assert.Equal(ProxyLoginViewModel.WatchWithoutText, rows.WatchText);
    }

    [Fact]
    public void Turned_on_it_folds_out_what_is_sent_to_whom_and_what_never_and_is_kept()
    {
        _proxy.Setting = new WindowsProxySetting(WindowsProxyKind.Manual, Proxy);
        var rows = Rows();
        string? kept = "unset";
        rows.LoginChosen += (_, proxy) => kept = proxy;

        rows.Toggle();

        Assert.True(rows.IsOn);
        Assert.Equal(Proxy, _proxy.LoginFor);
        Assert.Equal(Proxy, kept);
        Assert.Equal(["Skickas", "Till", "Aldrig"], rows.Tells.Select(tell => tell.Label));
        Assert.Contains(@"LAN\anna.lind", rows.Tells[0].Text, StringComparison.Ordinal);
        Assert.Contains("lösenordet lämnar aldrig datorn", rows.Tells[0].SubText, StringComparison.Ordinal);
        Assert.Contains(Proxy, rows.Tells[1].Text, StringComparison.Ordinal);
        Assert.Contains("WPAD", rows.Tells[2].Text, StringComparison.Ordinal);
        Assert.Equal(ProxyLoginViewModel.WatchLogsInText, rows.WatchText);

        rows.Toggle();

        Assert.False(rows.IsOn);
        Assert.Null(_proxy.LoginFor);
        Assert.Null(kept);
    }

    [Fact]
    public void A_proxy_found_automatically_gets_no_switch_and_a_click_does_nothing()
    {
        _proxy.Setting = new WindowsProxySetting(WindowsProxyKind.Automatic, null);
        var rows = Rows();

        rows.Toggle();

        Assert.True(rows.IsAutomatic);
        Assert.False(rows.CanLogin);
        Assert.False(rows.IsOn);
        Assert.Null(_proxy.LoginFor);
        Assert.Equal(ProxyLoginViewModel.AutomaticText, rows.HowText);
    }

    [Fact]
    public void Without_a_proxy_only_the_row_says_so()
    {
        var rows = Rows();

        Assert.False(rows.HasProxy);
        Assert.False(rows.CanLogin);
        Assert.Equal(ProxyLoginViewModel.NoProxyText, rows.AddressText);
        Assert.False(rows.HasHow);
    }

    [Fact]
    public void A_proxy_that_asks_for_a_login_is_said_with_the_time_and_the_background_waits()
    {
        _proxy.Setting = new WindowsProxySetting(WindowsProxyKind.Manual, Proxy);
        var rows = Rows();

        _proxy.Ask(new DateTimeOffset(2026, 11, 8, 9, 41, 0, TimeSpan.Zero));

        Assert.True(rows.IsRefused);
        Assert.Contains("407 kl. 09:41", rows.RefusedText, StringComparison.Ordinal);
        Assert.Equal(ProxyLoginViewModel.WantsLoginText, rows.NetText);
        Assert.False(rows.NetIsGood);
        Assert.True(rows.WatchWaits);
        Assert.Equal(ProxyLoginViewModel.WatchWaitsText, rows.WatchText);
    }

    [Fact]
    public void A_login_whose_proxy_moved_in_Windows_is_turned_off_when_the_page_is_shown()
    {
        _proxy.Setting = new WindowsProxySetting(WindowsProxyKind.Manual, Proxy);
        _proxy.AllowLogin(Proxy);
        var rows = Rows();
        string? kept = "unset";
        rows.LoginChosen += (_, proxy) => kept = proxy;
        _proxy.Setting = new WindowsProxySetting(WindowsProxyKind.Manual, "other.lan:3128");

        rows.Refresh();

        Assert.Null(_proxy.LoginFor);
        Assert.Null(kept);
        Assert.False(rows.IsOn);
    }

    [Fact]
    public async Task An_unpinned_node_shows_its_certificate_as_a_picture_and_can_be_pinned()
    {
        var sources = new PinSources { Shows = Old };
        var box = Box(sources);
        string? said = null;
        box.Pinned += (_, words) => said = words;

        await box.LoadAsync();

        Assert.Equal(NodePinState.Unpinned, box.State);
        var art = Assert.Single(box.Arts);
        Assert.Equal(32, art.Cells.Count);
        Assert.Equal(CertificatePin.Short(Old), art.Short);
        Assert.Equal("Inte betrott av Windows", box.StatusText);
        Assert.Equal("Självsignerat", box.WhenText);
        Assert.Equal("esplora.lan, självsignerat", box.IssuerText);
        Assert.True(box.CanPin);
        Assert.Equal(CertificatePin.Grouped(Old), box.CopyText);

        await box.PinAsync();

        Assert.Equal((Old, "Anna Lind"), (sources.Pin!.Fingerprint, sources.Pin.PinnedBy));
        Assert.Equal(NodePinState.Pinned, box.State);
        Assert.Equal("Fäst", box.StatusText);
        Assert.NotNull(said);
    }

    [Fact]
    public async Task A_changed_certificate_stands_beside_the_pinned_and_is_trusted_only_after_the_question()
    {
        var sources = new PinSources { Shows = New };
        sources.Pin = new EndpointCertificate(Old, _clock.UtcNow.AddDays(-1), "Anna Lind");
        var box = Box(sources);

        await box.LoadAsync();

        Assert.Equal(NodePinState.Changed, box.State);
        Assert.True(box.IsPair);
        Assert.Equal([NodePinTone.Faded, NodePinTone.Bad], box.Arts.Select(art => art.Tone));
        Assert.NotEqual(box.Arts[0].Cells, box.Arts[1].Cells);
        Assert.Equal(NodePinViewModel.ChangedNote, box.Note);
        Assert.True(box.CanTrust);
        Assert.False(box.CanPin);

        box.AskTrust();

        Assert.True(box.IsConfirming);
        Assert.Equal(NodePinViewModel.TrustQuestion, box.Question);
        Assert.Equal(Old, sources.Pin.Fingerprint);

        await box.ConfirmAsync();

        Assert.Equal(New, sources.Pin.Fingerprint);
        Assert.Equal(NodePinState.Pinned, box.State);
        Assert.False(box.IsConfirming);
    }

    [Fact]
    public async Task Removing_the_pin_asks_first_and_cancelling_keeps_it()
    {
        var sources = new PinSources { Shows = Old };
        sources.Pin = new EndpointCertificate(Old, _clock.UtcNow, "Anna Lind");
        var box = Box(sources);
        await box.LoadAsync();

        box.AskUnpin();
        box.CancelConfirm();

        Assert.NotNull(sources.Pin);
        box.AskUnpin();
        await box.ConfirmAsync();

        Assert.Null(sources.Pin);
        Assert.Equal(NodePinState.Unpinned, box.State);
    }

    [Fact]
    public async Task A_node_that_does_not_answer_and_has_no_pin_offers_nothing_to_pin()
    {
        var box = Box(new PinSources { Shows = null });

        await box.LoadAsync();

        Assert.Equal(NodePinState.Unreachable, box.State);
        Assert.False(box.CanPin);
        Assert.Empty(box.Arts);
    }

    [Fact]
    public void The_pin_on_a_row_follows_the_endpoint()
    {
        var endpoint = new SourceEndpoint(Guid.NewGuid(), Chain.Bitcoin, "esplora.lan",
            new Uri("https://esplora.lan/api/"), "esplora", null, true) { Local = true };
        var pinned = endpoint with { Pin = new EndpointCertificate(Old, _clock.UtcNow, "Anna Lind") };

        Assert.Equal(EndpointPin.None, EndpointRowViewModel.PinOf(
            new EndpointView(endpoint with { Local = false }, null, false, false)));
        Assert.Equal(EndpointPin.Unpinned, EndpointRowViewModel.PinOf(new EndpointView(endpoint, null, false, false)));
        Assert.Equal(EndpointPin.Pinned, EndpointRowViewModel.PinOf(new EndpointView(pinned, null, false, false)));
        Assert.Equal(EndpointPin.Changed, EndpointRowViewModel.PinOf(
            new EndpointView(pinned, null, true, false) { CertificateRefused = true }));
    }

    /// <summary>Creates the proxy's rows.</summary>
    private ProxyLoginViewModel Rows() =>
        new(_proxy, _network, new InlineUi(), @"LAN\anna.lind", TimeZoneInfo.Utc);

    /// <summary>Creates the box of the own node.</summary>
    private NodePinViewModel Box(PinSources sources) =>
        new(sources, Chain.Bitcoin, PinSources.Id, "esplora.lan", () => "Anna Lind", _clock, TimeZoneInfo.Utc);

    /// <summary>The proxy and the login in memory.</summary>
    private sealed class TestProxy : IWindowsProxy
    {
        public WindowsProxySetting Setting { get; set; } = WindowsProxySetting.None;

        public string? LoginFor { get; private set; }

        public DateTimeOffset? LoginAskedAt { get; private set; }

        public event EventHandler? Changed;

        public WindowsProxySetting Read() => Setting;

        public bool IsLoginOn() =>
            LoginFor is not null && Setting.Kind == WindowsProxyKind.Manual && Setting.Address == LoginFor;

        public void AllowLogin(string? proxy)
        {
            LoginFor = proxy;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Ask(DateTimeOffset at)
        {
            LoginAskedAt = at;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>One own node with its pin, in memory.</summary>
    private sealed class PinSources : IChainSources
    {
        public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-00000000000a");

        public string? Shows { get; set; }

        public EndpointCertificate? Pin { get; set; }

        public IReadOnlyList<Chain> Chains => [Chain.Bitcoin];

        public event EventHandler<Chain>? Changed
        {
            add { }
            remove { }
        }

        public event EventHandler<SourceEndpoint>? CertificateRefused
        {
            add { }
            remove { }
        }

        public IReadOnlyList<SourceApiInfo> Apis(Chain chain) => [];

        public ChainSourcesView Get(Chain chain) => new(chain, SourceMode.Fastest,
        [
            new EndpointView(
                new SourceEndpoint(Id, chain, "esplora.lan", new Uri("https://esplora.lan/api/"), "esplora", null, true)
                {
                    Local = true,
                    Pin = Pin,
                },
                null, false, false),
        ], null);

        public Task SetModeAsync(Chain chain, SourceMode mode) => Task.CompletedTask;

        public (SourceEndpoint Endpoint, Task Saved) Add(
            Chain chain, Uri address, string api, string? apiKey, bool local = false) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Chain chain, Guid id) => Task.CompletedTask;

        public Task SetEnabledAsync(Chain chain, Guid id, bool enabled) => Task.CompletedTask;

        public Task<NodeCertificate> ReadCertificateAsync(
            Chain chain, Guid id, CancellationToken cancellationToken = default)
        {
            if (Shows is null)
            {
                throw new ExitException(ExitError.RequestFailed);
            }

            var now = new DateTimeOffset(2026, 3, 14, 0, 0, 0, TimeSpan.Zero);
            return Task.FromResult(new NodeCertificate(
                Shows, "CN=esplora.lan", "CN=esplora.lan", now, now.AddYears(2), TrustedByWindows: false));
        }

        public Task PinAsync(Chain chain, Guid id, string fingerprint, string by)
        {
            Pin = new EndpointCertificate(fingerprint, DateTimeOffset.UnixEpoch, by);
            return Task.CompletedTask;
        }

        public Task UnpinAsync(Chain chain, Guid id, string by)
        {
            Pin = null;
            return Task.CompletedTask;
        }

        public Task MoveAsync(Chain chain, Guid id, int steps) => Task.CompletedTask;

        public Task<ProbeRun> ProbeAsync(Chain chain, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProbeRun.NotSent);

        public Task<EndpointProbe> TryAsync(
            Chain chain, Uri address, string api, string? apiKey, bool local = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
