using System.Collections.Concurrent;
using System.Threading.Channels;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// News of new blocks by websocket (round 3): an Esplora endpoint's <c>/v1/ws</c> asked only for blocks, a new block
/// counted once, the recent blocks a socket starts with never counted, an endpoint without a websocket passed over for
/// the next, and nothing at all without a way out. Scripted sockets, never the network.
/// </summary>
public sealed class BlockNewsTests : IDisposable
{
    private const string Mempool = "https://mempool.example/api/";

    private readonly SourceHarness _harness = new();

    private readonly ScriptedSockets _sockets = new();

    private readonly BlockNews _news;

    public BlockNewsTests()
    {
        _news = new BlockNews(_harness.Sources, _sockets, _harness.Clock, Pause, TimeSpan.FromMilliseconds(20));
    }

    public void Dispose()
    {
        _news.Dispose();
        _harness.Dispose();
    }

    [Theory]
    [InlineData("https://mempool.space/api/", "wss://mempool.space/api/v1/ws")]
    [InlineData("https://node.example:8443/esplora/api/", "wss://node.example:8443/esplora/api/v1/ws")]
    public void The_websocket_is_under_the_endpoint_with_wss(string endpoint, string expected)
    {
        Assert.Equal(new Uri(expected), BlockNews.SocketAddress(new Uri(endpoint)));
    }

    [Theory]
    [InlineData("""{"block":{"id":"00ab","height":921405}}""", 921_405L, true)]
    [InlineData("""{"blocks":[{"height":921400},{"height":921404}],"mempoolInfo":{}}""", 921_404L, false)]
    public void A_message_about_blocks_gives_its_height(string message, long height, bool isNew)
    {
        Assert.Equal((height, isNew), BlockNews.HeightOf(message));
    }

    [Theory]
    [InlineData("""{"mempoolInfo":{"size":3}}""")]
    [InlineData("""{"block":{"height":"921405"}}""")]
    [InlineData("""[1,2]""")]
    [InlineData("not json")]
    public void Any_other_message_says_nothing(string message)
    {
        Assert.Null(BlockNews.HeightOf(message));
    }

    [Fact]
    public async Task Each_new_block_is_heard_once_and_only_blocks_are_asked_for()
    {
        await UseOnlyAsync(Mempool);
        var socket = _sockets.Open(Mempool);
        socket.Say("""{"blocks":[{"height":921400}]}""");
        socket.Say("""{"block":{"height":921401}}""");

        _news.Keep(Chain.Bitcoin);
        await Until(() => _news.Heard(Chain.Bitcoin) == 1);
        _harness.Clock.UtcNow += BlockNews.MinimumNudge;
        socket.Say("""{"block":{"height":921401}}""");
        socket.Say("""{"mempoolInfo":{"size":3}}""");
        socket.Say("""{"block":{"height":921402}}""");

        await Until(() => _news.Heard(Chain.Bitcoin) == 2);
        Assert.Equal([BlockNews.WantBlocks], socket.Sent);
        Assert.Equal(new Uri("wss://mempool.example/api/v1/ws"), Assert.Single(_sockets.Asked.Distinct()));
    }

    [Fact]
    public async Task A_flood_is_one_nudge_and_a_jump_or_an_impossible_height_is_not_believed()
    {
        await UseOnlyAsync(Mempool);
        var socket = _sockets.Open(Mempool);
        socket.Say("""{"blocks":[{"height":921400}]}""");
        socket.Say("""{"block":{"height":921401}}""");
        socket.Say("""{"block":{"height":921402}}""");
        socket.Say("""{"block":{"height":921403}}""");

        _news.Keep(Chain.Bitcoin);
        await Until(() => socket.Received == 4);
        Assert.Equal(1, _news.Heard(Chain.Bitcoin));

        _harness.Clock.UtcNow += BlockNews.MinimumNudge;
        socket.Say("""{"block":{"height":921410}}""");
        socket.Say("""{"block":{"height":99999999}}""");
        socket.Say("""{"block":{"height":921404}}""");
        await Until(() => _news.Heard(Chain.Bitcoin) == 2);
    }

    [Fact]
    public async Task An_endpoint_without_a_websocket_is_passed_over_for_the_next()
    {
        await UseOnlyAsync(Fixtures.Base, Mempool);
        var socket = _sockets.Open(Mempool);
        socket.Say("""{"block":{"height":921401}}""");

        _news.Keep(Chain.Bitcoin);

        await Until(() => _news.Heard(Chain.Bitcoin) == 1);
        Assert.Equal(
            [new Uri("wss://esplora.test/api/v1/ws"), new Uri("wss://mempool.example/api/v1/ws")],
            _sockets.Asked.Distinct());
    }

    [Fact]
    public async Task Without_a_way_out_nothing_is_heard_and_the_endpoint_is_not_blamed()
    {
        await UseOnlyAsync(Mempool);
        _sockets.NoWayOut = true;

        _news.Keep(Chain.Bitcoin);
        await Until(() => _sockets.Asked.Count >= 2);

        Assert.Equal(0, _news.Heard(Chain.Bitcoin));
        Assert.All(_sockets.Asked, asked => Assert.Equal(new Uri("wss://mempool.example/api/v1/ws"), asked));
    }

    [Fact]
    public async Task Released_or_off_nothing_is_listened_to()
    {
        await UseOnlyAsync(Mempool);
        var socket = _sockets.Open(Mempool);
        _news.Keep(Chain.Bitcoin);
        await Until(() => socket.Sent.Count == 1);

        _news.Release(Chain.Bitcoin);

        await Until(() => socket.Closed);
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Off);
        var asked = _sockets.Asked.Count;
        _news.Keep(Chain.Bitcoin);
        await Task.Delay(100);
        Assert.Equal(asked, _sockets.Asked.Count);
    }

    [Fact]
    public void Ethereum_and_a_module_without_websockets_never_listen()
    {
        var without = new BlockNews(_harness.Sources, null, _harness.Clock);
        without.Keep(Chain.Bitcoin);
        _news.Keep(Chain.Ethereum);

        Assert.Equal(0, without.Heard(Chain.Bitcoin));
        Assert.Equal(0, _news.Heard(Chain.Ethereum));
        Assert.Empty(_sockets.Asked);
    }

    /// <summary>Makes Bitcoin's endpoints exactly these, in this order.</summary>
    /// <param name="addresses">The endpoints' addresses.</param>
    /// <returns>A task that completes when they are set.</returns>
    private async Task UseOnlyAsync(params string[] addresses)
    {
        foreach (var endpoint in _harness.Sources.Get(Chain.Bitcoin).Endpoints.ToList())
        {
            await _harness.Sources.RemoveAsync(Chain.Bitcoin, endpoint.Endpoint.Id);
        }

        foreach (var address in addresses)
        {
            await _harness.Sources.Add(Chain.Bitcoin, new Uri(address), "esplora", null).Saved;
        }
    }

    /// <summary>Waits a little, whatever the wait asked for, so that the loops turn quickly.</summary>
    private static Task Pause(TimeSpan wait, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);

    /// <summary>Waits until a condition holds, at most five seconds.</summary>
    private static async Task Until(Func<bool> condition)
    {
        for (var round = 0; round < 500 && !condition(); round++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    /// <summary>Websockets that exist only for the endpoints a test opens; any other is refused like a 404.</summary>
    private sealed class ScriptedSockets : IExitWebSocket
    {
        private readonly ConcurrentDictionary<Uri, ScriptedSocket> _open = new();

        public ConcurrentQueue<Uri> Asked { get; } = new();

        public bool NoWayOut { get; set; }

        public ScriptedSocket Open(string endpoint)
        {
            var socket = new ScriptedSocket();
            _open[BlockNews.SocketAddress(new Uri(endpoint))] = socket;
            return socket;
        }

        public Task<IExitSocket> ConnectAsync(Uri address, CancellationToken cancellationToken = default)
        {
            Asked.Enqueue(address);
            if (NoWayOut)
            {
                throw new ExitNotConnectedException();
            }

            return _open.TryGetValue(address, out var socket)
                ? Task.FromResult<IExitSocket>(socket)
                : throw new ExitException(ExitError.RequestFailed);
        }
    }

    /// <summary>A socket that gives the messages a test says, and then waits.</summary>
    private sealed class ScriptedSocket : IExitSocket
    {
        private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();

        public List<string> Sent { get; } = [];

        public bool Closed { get; private set; }

        public void Say(string message) => _messages.Writer.TryWrite(message);

        public Task SendAsync(string text, CancellationToken cancellationToken = default)
        {
            Sent.Add(text);
            return Task.CompletedTask;
        }

        public int Received => _received;

        private int _received;

        public async Task<string?> ReceiveAsync(int maximumBytes, CancellationToken cancellationToken = default)
        {
            var message = await _messages.Reader.ReadAsync(cancellationToken);
            Interlocked.Increment(ref _received);
            return message;
        }

        public ValueTask DisposeAsync()
        {
            Closed = true;
            return ValueTask.CompletedTask;
        }
    }
}
