using System.Net;
using System.Net.Sockets;
using System.Text;
using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The login to Windows' proxy with the Windows account (sketch PX1a): only to a proxy set by hand, only to the very
/// address it was turned on for, only with Kerberos or NTLM, never to a proxy found with WPAD or a script and never to
/// a source; a proxy that asks for a login is said, and turning the login off builds the way out anew.
/// </summary>
public sealed class ProxyLoginTests
{
    private const string Proxy = "proxy.lan.corp:8080";

    private static readonly Uri ProxyUri = new("http://proxy.lan.corp:8080/");

    private readonly FakeProxyReader _reader = new()
    {
        Setting = new WindowsProxySetting(WindowsProxyKind.Manual, Proxy),
    };

    private readonly FakeClock _clock = new();

    [Theory]
    [InlineData("proxy.lan.corp:8080", "proxy.lan.corp:8080")]
    [InlineData("Proxy.LAN.corp:8080", "proxy.lan.corp:8080")]
    [InlineData("http://proxy.lan.corp:3128", "proxy.lan.corp:3128")]
    [InlineData("proxy", "proxy:80")]
    [InlineData("http=web:80;https=secure.lan:8443", "secure.lan:8443")]
    [InlineData("http=web:80 https=secure.lan:8443", "secure.lan:8443")]
    [InlineData("ftp=files:21;proxy.lan:8080", "proxy.lan:8080")]
    [InlineData("[fd00::5]:8080", "[fd00::5]:8080")]
    public void The_proxy_for_https_is_read_from_Windows_list(string list, string expected) =>
        Assert.Equal(expected, WindowsProxyText.HttpsProxyOf(list));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http=web:80;ftp=files:21")]
    public void A_list_without_a_proxy_for_https_has_none(string? list) =>
        Assert.Null(WindowsProxyText.HttpsProxyOf(list));

    [Fact]
    public void Automatic_detection_or_a_script_makes_the_setting_automatic_even_with_a_proxy_set_by_hand()
    {
        Assert.Equal(WindowsProxyKind.Automatic, WindowsProxyText.Setting(true, null, Proxy).Kind);
        Assert.Equal(
            WindowsProxyKind.Automatic, WindowsProxyText.Setting(false, "http://wpad/wpad.dat", Proxy).Kind);
        Assert.Null(WindowsProxyText.Setting(true, null, Proxy).Address);
        Assert.Equal(
            new WindowsProxySetting(WindowsProxyKind.Manual, Proxy), WindowsProxyText.Setting(false, null, Proxy));
        Assert.Equal(WindowsProxySetting.None, WindowsProxyText.Setting(false, " ", null));
    }

    [Fact]
    public void The_login_is_off_by_default_and_then_gives_the_pipeline_no_credentials()
    {
        var login = Login();
        var handler = new WindowsHandlerFactory(new NetworkOptions(), login).CreateSockets();

        Assert.Null(login.LoginFor);
        Assert.False(login.IsLoginOn());
        Assert.Null(login.ProxyCredentials());
        Assert.Null(handler.DefaultProxyCredentials);
        Assert.Null(handler.Credentials);
        handler.Dispose();
    }

    [Theory]
    [InlineData("Negotiate")]
    [InlineData("NTLM")]
    [InlineData("ntlm")]
    [InlineData("Kerberos")]
    public void Turned_on_the_proxy_set_by_hand_gets_the_Windows_account_for_a_scheme_without_the_password(
        string scheme)
    {
        var login = Login();
        login.AllowLogin(Proxy);

        var credentials = login.ProxyCredentials()!;

        Assert.True(login.IsLoginOn());
        Assert.Same(CredentialCache.DefaultNetworkCredentials, credentials.GetCredential(ProxyUri, scheme));
    }

    [Theory]
    [InlineData("Basic")]
    [InlineData("Digest")]
    [InlineData("Bearer")]
    public void A_scheme_that_would_send_or_need_a_password_gets_nothing(string scheme)
    {
        var login = Login();
        login.AllowLogin(Proxy);

        Assert.Null(login.ProxyCredentials()!.GetCredential(ProxyUri, scheme));
    }

    [Theory]
    [InlineData("http://proxy.lan.corp:3128/")]
    [InlineData("http://other.lan.corp:8080/")]
    [InlineData("https://chain.example/api/")]
    [InlineData("http://evil.example:8080/")]
    public void Any_other_address_than_the_proxy_it_was_turned_on_for_gets_nothing(string address)
    {
        var login = Login();
        login.AllowLogin(Proxy);

        Assert.Null(login.ProxyCredentials()!.GetCredential(new Uri(address), "Negotiate"));
    }

    [Fact]
    public void A_proxy_found_with_WPAD_never_gets_the_account_even_at_the_same_address()
    {
        var login = Login();
        login.AllowLogin(Proxy);
        var credentials = login.ProxyCredentials()!;

        // Windows switches to automatic detection after the login was turned on: the gate reads it at every question.
        _reader.Setting = new WindowsProxySetting(WindowsProxyKind.Automatic, null);

        Assert.False(login.IsLoginOn());
        Assert.Null(credentials.GetCredential(ProxyUri, "Negotiate"));
        Assert.Null(credentials.GetCredential(ProxyUri, "NTLM"));
    }

    [Fact]
    public void A_proxy_changed_in_Windows_turns_the_login_off_until_it_is_turned_on_again()
    {
        var login = Login();
        login.AllowLogin(Proxy);
        var credentials = login.ProxyCredentials()!;

        _reader.Setting = new WindowsProxySetting(WindowsProxyKind.Manual, "other.lan.corp:8080");

        Assert.False(login.IsLoginOn());
        Assert.Null(credentials.GetCredential(ProxyUri, "Negotiate"));
        Assert.Null(credentials.GetCredential(new Uri("http://other.lan.corp:8080/"), "Negotiate"));
    }

    [Fact]
    public void Without_a_proxy_in_Windows_nothing_is_given()
    {
        var login = Login();
        login.AllowLogin(Proxy);
        _reader.Setting = WindowsProxySetting.None;

        Assert.Null(login.ProxyCredentials()!.GetCredential(ProxyUri, "Negotiate"));
    }

    [Fact]
    public void A_setting_that_cannot_be_read_gives_nothing()
    {
        var login = new WindowsProxyLogin(new ThrowingReader(), new NetworkOptions { ProxyLoginFor = Proxy }, _clock);

        Assert.Equal(WindowsProxySetting.None, login.Read());
        Assert.Null(login.ProxyCredentials()!.GetCredential(ProxyUri, "Negotiate"));
    }

    [Fact]
    public void The_pipeline_through_Windows_gives_the_gate_only_for_the_systems_proxy_never_to_a_source()
    {
        var login = new WindowsProxyLogin(_reader, new NetworkOptions { ProxyLoginFor = Proxy }, _clock);
        using var handler = new WindowsHandlerFactory(new NetworkOptions(), login).CreateSockets();

        Assert.NotNull(handler.DefaultProxyCredentials);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.DefaultProxyCredentials!.GetCredential(new Uri("https://chain.example/"), "Negotiate"));
    }

    [Fact]
    public void Turning_the_login_on_off_or_moving_it_is_told_and_an_address_that_cannot_be_read_is_refused()
    {
        var login = Login();
        var choices = 0;
        var changes = 0;
        login.ChoiceChanged += (_, _) => choices++;
        login.Changed += (_, _) => changes++;

        login.AllowLogin(Proxy);
        login.AllowLogin("PROXY.lan.corp:8080");
        login.AllowLogin(null);

        Assert.Equal(2, choices);
        Assert.Equal(2, changes);
        Assert.Throws<ArgumentException>(() => login.AllowLogin("http://"));
    }

    [Fact]
    public async Task Turning_the_login_off_builds_the_way_through_Windows_anew()
    {
        var login = Login();
        login.AllowLogin(Proxy);
        var harness = new NetworkHarness(login: login);
        harness.Route.Choose(ExitRoute.Windows);
        await harness.Http.SendAsync(ExitRequest.Get(new Uri("https://chain.example/a")));

        login.AllowLogin(null);
        await harness.Http.SendAsync(ExitRequest.Get(new Uri("https://chain.example/b")));

        Assert.Equal(2, harness.WindowsHandlers.Handlers.Count);
    }

    [Fact]
    public async Task A_proxy_that_asks_for_a_login_is_said_and_the_source_is_never_reached()
    {
        var login = Login();
        var harness = new NetworkHarness(login: login);
        _clock.UtcNow = new DateTimeOffset(2026, 11, 8, 9, 41, 0, TimeSpan.Zero);
        harness.Route.Choose(ExitRoute.Windows);
        harness.WindowsHandlers.Answer = (_, _) => throw new HttpRequestException(
            HttpRequestError.ProxyTunnelError, "407", statusCode: HttpStatusCode.ProxyAuthenticationRequired);

        var refused = await Assert.ThrowsAsync<ExitException>(
            () => harness.Http.SendAsync(ExitRequest.Get(new Uri("https://chain.example/a"))));

        Assert.Equal(ExitError.ProxyLoginRequired, refused.Error);
        Assert.Equal(_clock.UtcNow, login.LoginAskedAt);

        harness.WindowsHandlers.Answer = (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        await harness.Http.SendAsync(ExitRequest.Get(new Uri("https://chain.example/b")));

        Assert.Null(login.LoginAskedAt);
    }

    [Fact]
    public async Task The_probe_notes_a_proxy_that_asks_for_a_login()
    {
        var login = Login();
        var handlers = new RecordingWindowsHandlerFactory
        {
            Answer = (_, _) => throw new HttpRequestException(
                HttpRequestError.ProxyTunnelError, "407", statusCode: HttpStatusCode.ProxyAuthenticationRequired),
        };
        var probe = new InternetProbe(handlers, TimeProvider.System, login);

        Assert.False(await probe.ReachesInternetAsync(default));
        Assert.NotNull(login.LoginAskedAt);
    }

    [Fact]
    public void A_failure_other_than_407_is_not_a_request_for_a_login()
    {
        Assert.False(WindowsProxyLogin.AsksForLogin(new HttpRequestException("down")));
        Assert.False(WindowsProxyLogin.AsksForLogin(
            new HttpRequestException("403", null, HttpStatusCode.Forbidden)));
        Assert.True(WindowsProxyLogin.AsksForLogin(new IOException("wrapped", new HttpRequestException(
            "407", null, HttpStatusCode.ProxyAuthenticationRequired))));
    }

    [Fact]
    public void The_module_offers_the_proxy_and_starts_with_the_login_read_from_the_settings()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Chainsaw.SharedKernel.IClock>(_clock);
        services.AddSingleton<Chainsaw.Modules.Security.Contracts.IVaultSecrets>(new FakeSecrets());
        services.AddSingleton<IWindowsProxyReader>(_reader);
        services.AddNetworkModule(options => options.ProxyLoginFor = Proxy);
        using var provider = services.BuildServiceProvider();

        var proxy = provider.GetRequiredService<IWindowsProxy>();
        Assert.Same(provider.GetRequiredService<WindowsProxyLogin>(), proxy);
        Assert.Equal(Proxy, proxy.LoginFor);
        Assert.True(proxy.IsLoginOn());
    }

    /// <summary>
    /// The real pipeline against a proxy on loopback that refuses every tunnel with 407 and offers Negotiate, NTLM and
    /// Basic: the credentials are asked only with the proxy's own address, and a gate that answers nothing sends no
    /// Proxy-Authorization at all.
    /// </summary>
    [Fact]
    public async Task The_pipeline_asks_the_credentials_with_the_proxys_address_and_sends_nothing_they_refuse()
    {
        using var proxy = new RefusingProxy();
        var asked = new RecordingCredentials();
        using var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy(proxy.Address) { Credentials = asked },
            UseCookies = false,
        };
        using var client = new HttpClient(handler);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("https://chain.example/api/")));

        Assert.True(WindowsProxyLogin.AsksForLogin(failure));
        Assert.NotEmpty(asked.Questions);
        Assert.All(asked.Questions, question => Assert.Equal(
            WindowsProxyText.AddressOf(proxy.Address), WindowsProxyText.AddressOf(question.Uri)));
        Assert.DoesNotContain(proxy.Requests, request => request.Contains(
            "Proxy-Authorization", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Creates the login with the test's reader and clock, off.</summary>
    /// <returns>The login.</returns>
    private WindowsProxyLogin Login() => new(_reader, new NetworkOptions(), _clock);

    /// <summary>A reader that cannot read the setting.</summary>
    private sealed class ThrowingReader : IWindowsProxyReader
    {
        public WindowsProxySetting Read() => throw new InvalidOperationException("cannot read");
    }

    /// <summary>Credentials that record every question and answer none.</summary>
    private sealed class RecordingCredentials : ICredentials
    {
        public List<(Uri Uri, string Scheme)> Questions { get; } = [];

        public NetworkCredential? GetCredential(Uri uri, string authType)
        {
            lock (Questions)
            {
                Questions.Add((uri, authType));
            }

            return null;
        }
    }

    /// <summary>A proxy on loopback that answers every request with 407 and records what it was sent.</summary>
    private sealed class RefusingProxy : IDisposable
    {
        private const string Refusal =
            "HTTP/1.1 407 Proxy Authentication Required\r\n"
            + "Proxy-Authenticate: Negotiate\r\nProxy-Authenticate: NTLM\r\nProxy-Authenticate: Basic realm=\"x\"\r\n"
            + "Content-Length: 0\r\nConnection: close\r\n\r\n";

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        private readonly CancellationTokenSource _stop = new();

        public RefusingProxy()
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _ = ServeAsync();
        }

        public Uri Address { get; }

        public List<string> Requests { get; } = [];

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var stream = client.GetStream();
                    var text = await ReadHeadAsync(stream);
                    lock (Requests)
                    {
                        Requests.Add(text);
                    }

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(Refusal), _stop.Token);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException
                                              or ObjectDisposedException or IOException)
            {
                // Stopped.
            }
        }

        private async Task<string> ReadHeadAsync(NetworkStream stream)
        {
            var head = new StringBuilder();
            var buffer = new byte[1024];
            while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, _stop.Token);
                if (read == 0)
                {
                    break;
                }

                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            return head.ToString();
        }
    }
}
