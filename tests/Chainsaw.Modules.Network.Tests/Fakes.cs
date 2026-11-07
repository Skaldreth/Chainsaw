using System.Net;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>Named secrets in memory instead of the vault, with a switch that behaves like a locked vault.</summary>
internal sealed class FakeSecrets : IVaultSecrets
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public bool Locked { get; set; }

    public IReadOnlyDictionary<string, string> All => _secrets;

    public IReadOnlyList<string> List(string prefix) =>
        Open().Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .ToList();

    public string? Find(string name) => Open().GetValueOrDefault(name);

    public void Save(string name, string value) => Open()[name] = value;

    public bool Remove(string name) => Open().Remove(name);

    private Dictionary<string, string> Open() => Locked ? throw new VaultException(VaultError.Locked) : _secrets;
}

/// <summary>A tunnel that exists only in memory.</summary>
internal sealed class FakeTunnel(IPEndPoint proxy, NetworkCredential credentials) : IExitTunnel
{
    private readonly CancellationTokenSource _stopped = new();

    public IPEndPoint Socks5Proxy => proxy;

    public NetworkCredential Socks5Credentials => credentials;

    public CancellationToken Stopped => _stopped.Token;

    public bool Disposed { get; private set; }

    public bool IsRunning => !Disposed && !_stopped.IsCancellationRequested;

    /// <summary>Makes the tunnel stop on its own, as a dropped VPN would.</summary>
    public void Drop() => _stopped.Cancel();

    /// <summary>When set, stopping the tunnel waits for it, as a slow helper process would.</summary>
    public Task? StopGate { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (StopGate is not null)
        {
            await StopGate;
        }

        Disposed = true;
    }
}

/// <summary>A connector that hands out fake tunnels, or does what a test tells it to.</summary>
internal sealed class FakeConnector : IExitConnector
{
    public static readonly IPEndPoint LoopbackProxy = new(IPAddress.Loopback, 49_150);

    public List<FakeTunnel> Tunnels { get; } = [];

    public List<(ExitInfo Exit, string Configuration)> Calls { get; } = [];

    public IPEndPoint Proxy { get; set; } = LoopbackProxy;

    public NetworkCredential Credentials { get; set; } = new("tunnel-user", "tunnel-password-1");

    public Exception? Failure { get; set; }

    /// <summary>When set, the connector waits for it before it returns, to see the connecting state.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public bool DropAtOnce { get; set; }

    /// <summary>When set, the connector returns no tunnel at all, as a broken plug-in might.</summary>
    public bool ReturnNull { get; set; }

    public async Task<IExitTunnel> ConnectAsync(
        ExitInfo exit, string configuration, CancellationToken cancellationToken)
    {
        Calls.Add((exit, configuration));
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        if (ReturnNull)
        {
            return null!;
        }

        var tunnel = new FakeTunnel(Proxy, Credentials);
        if (DropAtOnce)
        {
            tunnel.Drop();
        }

        Tunnels.Add(tunnel);
        return tunnel;
    }
}

/// <summary>A handler that answers in memory and records what it was asked.</summary>
internal sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<byte[]> Bodies { get; } = [];

    public bool Disposed { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        Requests.Add(request);
        Bodies.Add(request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken));
        return await answer(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>Builds recording handlers instead of real ones and notes which proxy each was for.</summary>
internal sealed class RecordingHandlerFactory : IExitHandlerFactory
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });

    public List<IPEndPoint> Proxies { get; } = [];

    public List<NetworkCredential> Credentials { get; } = [];

    public List<RecordingHandler> Handlers { get; } = [];

    public List<Func<bool>> ProxyChecks { get; } = [];

    public HttpMessageHandler Create(IPEndPoint socks5Proxy, NetworkCredential credentials, Func<bool> proxyIsTunnel)
    {
        ProxyChecks.Add(proxyIsTunnel);
        Proxies.Add(socks5Proxy);
        Credentials.Add(credentials);
        var handler = new RecordingHandler((request, token) => Answer(request, token));
        Handlers.Add(handler);
        return handler;
    }
}

/// <summary>Builds recording handlers for the way out through Windows, and counts them.</summary>
internal sealed class RecordingWindowsHandlerFactory : IWindowsHandlerFactory
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });

    public List<RecordingHandler> Handlers { get; } = [];

    public bool Fails { get; set; }

    public Action? Building { get; set; }

    public HttpMessageHandler Create()
    {
        Building?.Invoke();
        if (Fails)
        {
            throw new System.Security.Cryptography.CryptographicException("The stores cannot be read.");
        }

        var handler = new RecordingHandler((request, token) => Answer(request, token));
        Handlers.Add(handler);
        return handler;
    }
}

/// <summary>Builds recording handlers for the way to the user's own node, and counts them.</summary>
internal sealed class RecordingLocalHandlerFactory : ILocalHandlerFactory
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Answer { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });

    public List<RecordingHandler> Handlers { get; } = [];

    public HttpMessageHandler Create()
    {
        var handler = new RecordingHandler((request, token) => Answer(request, token));
        Handlers.Add(handler);
        return handler;
    }
}

/// <summary>Whether the computer has a network, as the test says; online at first.</summary>
internal sealed class FakePresence : INetworkPresence
{
    public bool IsAvailable { get; set; } = true;

    public event EventHandler<bool>? Changed;

    public void Raise() => Changed?.Invoke(this, IsAvailable);
}

/// <summary>The computer's network as the test says; with a network at first.</summary>
internal sealed class FakeSignals : INetworkSignals
{
    public bool HasNetwork { get; set; } = true;

    public event EventHandler? Changed;

    /// <summary>Sets the network and tells it, as Windows does after a change.</summary>
    public void Set(bool hasNetwork)
    {
        HasNetwork = hasNetwork;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A probe that waits for the test to answer each call, and counts the calls.</summary>
internal sealed class ScriptedProbe : IInternetProbe
{
    private readonly System.Threading.Channels.Channel<Func<bool>> _answers =
        System.Threading.Channels.Channel.CreateUnbounded<Func<bool>>();

    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public void Answer(bool reached) => _answers.Writer.TryWrite(() => reached);

    public void Throw() => _answers.Writer.TryWrite(() => throw new HttpRequestException("probe"));

    public async Task<bool> ReachesInternetAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        var answer = await _answers.Reader.ReadAsync(cancellationToken);
        return answer();
    }
}

/// <summary>A clock that moves only when told to.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 22, 0, 0, TimeSpan.Zero);
}

/// <summary>The module's parts wired together with fakes, the way the container wires them.</summary>
internal sealed class NetworkHarness
{
    public NetworkHarness(
        NetworkOptions? options = null, IExitHandlerFactory? handlers = null, bool failingWindows = false)
    {
        WindowsHandlers.Fails = failingWindows;
        Options = options ?? new NetworkOptions();
        Records = new ExitRecords(Secrets);
        Connection = new ExitConnection(Records, Connector, handlers ?? Handlers);
        Store = new ExitStore(Records, Connection);
        Route = new ExitRouteChoice(Options, WindowsHandlers);
        Http = new ExitHttp(Connection, Route, Presence, Options, Clock);
        Connection.StatusChanged += (_, status) =>
        {
            lock (_events)
            {
                _events.Add(status);
            }
        };
    }

    public const string Configuration =
        "[Interface]\nPrivateKey = test-private-key-not-real\nDNS = 10.64.0.1\n";

    public FakeSecrets Secrets { get; } = new();

    public FakeConnector Connector { get; } = new();

    public RecordingHandlerFactory Handlers { get; } = new();

    public RecordingWindowsHandlerFactory WindowsHandlers { get; } = new();

    public ExitRouteChoice Route { get; }

    public FakeClock Clock { get; } = new();

    public FakePresence Presence { get; } = new();

    public NetworkOptions Options { get; }

    public ExitRecords Records { get; }

    public ExitConnection Connection { get; }

    public ExitStore Store { get; }

    public ExitHttp Http { get; }

    private readonly List<ExitStatus> _events = [];

    /// <summary>Gets a copy of the events raised so far; they may come from another thread.</summary>
    public IReadOnlyList<ExitStatus> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public void ClearEvents()
    {
        lock (_events)
        {
            _events.Clear();
        }
    }

    /// <summary>Waits until a condition holds, for work that finishes on the thread pool.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(5);
        }
    }

    /// <summary>Adds an exit and selects it.</summary>
    public async Task<ExitInfo> SelectedExitAsync(string name = "Exit A")
    {
        var exit = Store.Add(name, "Stockholm", ExitKind.WireGuard, Configuration);
        await Store.SelectAsync(exit.Id);
        return exit;
    }

    /// <summary>Adds, selects and connects an exit.</summary>
    public async Task<ExitInfo> ConnectedExitAsync(string name = "Exit A")
    {
        var exit = await SelectedExitAsync(name);
        Assert.Equal(ExitState.Connected, (await Connection.ConnectAsync()).State);
        return exit;
    }
}
