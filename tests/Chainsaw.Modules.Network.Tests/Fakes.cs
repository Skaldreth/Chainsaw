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

    /// <summary>Makes the tunnel stop on its own, as a dropped VPN would.</summary>
    public void Drop() => _stopped.Cancel();

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
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

    public HttpMessageHandler Create(IPEndPoint socks5Proxy, NetworkCredential credentials)
    {
        Proxies.Add(socks5Proxy);
        Credentials.Add(credentials);
        var handler = new RecordingHandler((request, token) => Answer(request, token));
        Handlers.Add(handler);
        return handler;
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
    public NetworkHarness(NetworkOptions? options = null, IExitHandlerFactory? handlers = null)
    {
        Options = options ?? new NetworkOptions();
        Records = new ExitRecords(Secrets);
        Connection = new ExitConnection(Records, Connector, handlers ?? Handlers);
        Store = new ExitStore(Records, Connection);
        Http = new ExitHttp(Connection, Options, Clock);
        Connection.StatusChanged += (_, status) => Events.Add(status);
    }

    public const string Configuration = "[Interface]\nPrivateKey = test-private-key-not-real\n";

    public FakeSecrets Secrets { get; } = new();

    public FakeConnector Connector { get; } = new();

    public RecordingHandlerFactory Handlers { get; } = new();

    public FakeClock Clock { get; } = new();

    public NetworkOptions Options { get; }

    public ExitRecords Records { get; }

    public ExitConnection Connection { get; }

    public ExitStore Store { get; }

    public ExitHttp Http { get; }

    public List<ExitStatus> Events { get; } = [];

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
