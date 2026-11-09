using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Listens for new blocks on Bitcoin through an Esplora endpoint's websocket where it has one (mempool.space's
/// <c>/api/v1/ws</c>, asked only for <c>blocks</c>), through the way out and nowhere else. An endpoint without one
/// (Blockstream) is passed over for a while and the next is tried; without any the watching simply asks as before.
/// The messages are only a nudge (<see cref="IBlockNews"/>): nothing of them is kept, and the user's own node, which is
/// reached only through <c>ILocalHttp</c>, is not listened to.
/// </summary>
internal sealed class BlockNews : IBlockNews, IDisposable
{
    /// <summary>How long a chain is listened to after it was last kept.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(2);

    /// <summary>How long an endpoint that gave no websocket is passed over.</summary>
    public static readonly TimeSpan PassOver = TimeSpan.FromHours(1);

    /// <summary>How long before a dropped or closed socket is opened again.</summary>
    public static readonly TimeSpan Reconnect = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The shortest time between two nudges: news faster than this (a source that floods) is counted once, and the
    /// minute's ask catches what it held back.
    /// </summary>
    public static readonly TimeSpan MinimumNudge = TimeSpan.FromSeconds(30);

    /// <summary>How many blocks above the last one heard a new block may be; a larger jump is not believed.</summary>
    public const int LargestJump = 3;

    /// <summary>The longest message read; mempool.space's messages about blocks are a few kilobytes.</summary>
    public const int MaximumMessageBytes = 1024 * 1024;

    /// <summary>What is asked: only the blocks, which says nothing about a case.</summary>
    internal const string WantBlocks = """{"action":"want","data":["blocks"]}""";

    /// <summary>Where an Esplora endpoint's websocket is, under its base address.</summary>
    private const string SocketPath = "v1/ws";

    /// <summary>The chains' endpoints.</summary>
    private readonly IChainSources _sources;

    /// <summary>The websockets through the way out, or <see langword="null"/> where there are none.</summary>
    private readonly IExitWebSocket? _sockets;

    /// <summary>The clock.</summary>
    private readonly IClock _clock;

    /// <summary>Waits; the tests' waits are short.</summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>How often a listener looks whether it is still kept.</summary>
    private readonly TimeSpan _lookEvery;

    /// <summary>Bitcoin's name in the tables, for the highest height the chain can have come to.</summary>
    private readonly string _chainName;

    /// <summary>The listeners per chain (guarded by itself).</summary>
    private readonly Dictionary<Chain, Listener> _listeners = [];

    /// <summary>Creates the listener of news.</summary>
    /// <param name="sources">The chains' endpoints.</param>
    /// <param name="sockets">The websockets through the way out, or <see langword="null"/>.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="delay">Waits; <see cref="Task.Delay(TimeSpan, CancellationToken)"/> when none.</param>
    /// <param name="lookEvery">How often a listener looks whether it is kept; 15 seconds when none.</param>
    /// <param name="network">The Bitcoin network the endpoints answer for.</param>
    public BlockNews(
        IChainSources sources,
        IExitWebSocket? sockets,
        IClock clock,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? lookEvery = null,
        BitcoinNetwork network = BitcoinNetwork.Mainnet)
    {
        (_sources, _sockets, _clock) = (sources, sockets, clock);
        _chainName = RawResponseStore.NameOf(network);
        _delay = delay ?? Task.Delay;
        _lookEvery = lookEvery ?? TimeSpan.FromSeconds(15);
    }

    /// <inheritdoc/>
    public long Heard(Chain chain)
    {
        lock (_listeners)
        {
            return _listeners.TryGetValue(chain, out var listener) ? Interlocked.Read(ref listener.HeardCount) : 0;
        }
    }

    /// <inheritdoc/>
    public void Keep(Chain chain)
    {
        if (_sockets is null || chain != Chain.Bitcoin)
        {
            return;
        }

        lock (_listeners)
        {
            var listener = _listeners.TryGetValue(chain, out var known) ? known : _listeners[chain] = new Listener();
            listener.KeptUntil = _clock.UtcNow + KeepFor;
            if (listener.Running is null || listener.Running.IsCompleted)
            {
                listener.Stop?.Dispose();
                listener.Stop = new CancellationTokenSource();
                var stop = listener.Stop.Token;
                listener.Running = Task.Run(() => RunAsync(listener, stop));
            }
        }
    }

    /// <inheritdoc/>
    public void Release(Chain chain)
    {
        lock (_listeners)
        {
            if (_listeners.TryGetValue(chain, out var listener))
            {
                listener.KeptUntil = DateTimeOffset.MinValue;
                listener.Stop?.Cancel();
            }
        }
    }

    /// <summary>Stops every listener.</summary>
    public void Dispose()
    {
        lock (_listeners)
        {
            foreach (var listener in _listeners.Values)
            {
                listener.KeptUntil = DateTimeOffset.MinValue;
                listener.Stop?.Cancel();
            }
        }
    }

    /// <summary>Gives the address of an Esplora endpoint's websocket: its base address with <c>wss</c>.</summary>
    /// <param name="address">The endpoint's base address, ending in <c>/</c>.</param>
    /// <returns>The websocket's address.</returns>
    internal static Uri SocketAddress(Uri address)
    {
        var builder = new UriBuilder(new Uri(address, SocketPath))
        {
            Scheme = Uri.UriSchemeWss,
            Port = address.IsDefaultPort ? -1 : address.Port,
        };
        return builder.Uri;
    }

    /// <summary>
    /// Reads the height a message says a new block has, or the highest of the recent blocks it lists.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The height and whether it is a new block, or <see langword="null"/> when it says nothing of blocks.</returns>
    internal static (long Height, bool IsNew)? HeightOf(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("block", out var block) && HeightIn(block) is { } height)
            {
                return (height, true);
            }

            if (root.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                var highest = blocks.EnumerateArray().Select(HeightIn).Max();
                return highest is { } top ? (top, false) : null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads a block's height from its object.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The height, or <see langword="null"/>.</returns>
    private static long? HeightIn(JsonElement block) =>
        block.ValueKind == JsonValueKind.Object && block.TryGetProperty("height", out var height)
            && height.ValueKind == JsonValueKind.Number && height.TryGetInt64(out var value) && value > 0
            ? value
            : null;

    /// <summary>Listens while the chain is kept: one endpoint at a time, the next when one gives no websocket.</summary>
    /// <param name="listener">The chain's listener.</param>
    /// <param name="stop">Stops it at once.</param>
    /// <returns>A task that completes when it is no longer kept.</returns>
    private async Task RunAsync(Listener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested && _clock.UtcNow < listener.KeptUntil)
        {
            var wait = _lookEvery;
            try
            {
                if (NextEndpoint(listener) is { } endpoint)
                {
                    wait = await ListenAsync(listener, endpoint, stop).ConfigureAwait(false);
                }

                await _delay(wait, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Listening must never take anything down; the watching asks as before meanwhile.
            }
        }
    }

    /// <summary>
    /// Opens an endpoint's websocket, asks for the blocks and counts each new one until the chain is no longer kept or
    /// the socket goes.
    /// </summary>
    /// <param name="listener">The chain's listener.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="stop">Stops it at once.</param>
    /// <returns>How long to wait before the next attempt.</returns>
    private async Task<TimeSpan> ListenAsync(Listener listener, SourceEndpoint endpoint, CancellationToken stop)
    {
        IExitSocket socket;
        try
        {
            socket = await _sockets!.ConnectAsync(SocketAddress(endpoint.Address), stop).ConfigureAwait(false);
        }
        catch (ExitNotConnectedException)
        {
            // No way out now: nothing was sent, and the endpoint is not to blame.
            return _lookEvery;
        }
        catch (ExitException)
        {
            // No websocket there (Blockstream answers 404), or it did not answer: the next endpoint is tried.
            listener.PassedOver[endpoint.Id] = _clock.UtcNow + PassOver;
            return TimeSpan.Zero;
        }

        await using (socket.ConfigureAwait(false))
        {
            using var kept = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var watchdog = WatchKeptAsync(listener, kept);
            try
            {
                await socket.SendAsync(WantBlocks, kept.Token).ConfigureAwait(false);
                while (await socket.ReceiveAsync(MaximumMessageBytes, kept.Token).ConfigureAwait(false) is { } text)
                {
                    Count(listener, text);
                }
            }
            catch (ExitException)
            {
                // The socket broke or the way out went: opened again after a while.
            }
            catch (OperationCanceledException) when (kept.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                // No longer kept: the loop ends.
            }
            finally
            {
                await kept.CancelAsync().ConfigureAwait(false);
                await watchdog.ConfigureAwait(false);
            }
        }

        return Reconnect;
    }

    /// <summary>
    /// Counts a message's new block: once per height, never for the recent blocks a socket starts with, never a height
    /// the chain cannot have come to or one more than <see cref="LargestJump"/> blocks above the last heard, and at
    /// most once per <see cref="MinimumNudge"/>, so that a source can neither flood the watching nor lead it astray.
    /// </summary>
    /// <param name="listener">The chain's listener.</param>
    /// <param name="message">The message.</param>
    private void Count(Listener listener, string message)
    {
        var now = _clock.UtcNow;
        if (HeightOf(message) is not { } found || found.Height <= listener.Highest
            || found.Height > ChainLimits.HighestPlausible(_chainName, now)
            || (found.IsNew && listener.Highest > 0 && found.Height > listener.Highest + LargestJump))
        {
            return;
        }

        listener.Highest = found.Height;
        if (found.IsNew && now - listener.LastNudge >= MinimumNudge)
        {
            listener.LastNudge = now;
            Interlocked.Increment(ref listener.HeardCount);
        }
    }

    /// <summary>Cancels the socket's reading when the chain is no longer kept.</summary>
    /// <param name="listener">The chain's listener.</param>
    /// <param name="kept">Cancelled when it is no longer kept, or when the socket is done.</param>
    /// <returns>A task that completes when either has happened.</returns>
    private async Task WatchKeptAsync(Listener listener, CancellationTokenSource kept)
    {
        try
        {
            while (!kept.IsCancellationRequested)
            {
                await _delay(_lookEvery, kept.Token).ConfigureAwait(false);
                if (_clock.UtcNow >= listener.KeptUntil)
                {
                    await kept.CancelAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The socket is done.
        }
    }

    /// <summary>
    /// Gives the next endpoint to listen to: Bitcoin's endpoints that are on, speak Esplora, are on the internet (the
    /// user's own node is not listened to) and are not passed over; one in step with the chain first.
    /// </summary>
    /// <param name="listener">The chain's listener.</param>
    /// <returns>The endpoint, or <see langword="null"/> when there is none.</returns>
    private SourceEndpoint? NextEndpoint(Listener listener)
    {
        var view = _sources.Get(Chain.Bitcoin);
        if (view.Mode == SourceMode.Off)
        {
            return null;
        }

        var now = _clock.UtcNow;
        return view.Endpoints
            .Where(each => each.Endpoint is { Enabled: true, Local: false, Api: EsploraApi.Code }
                && listener.PassedOver.GetValueOrDefault(each.Endpoint.Id) <= now)
            .OrderBy(each => each.Probe is { Outcome: ProbeOutcome.Answered } && !each.FailedSince ? 0 : 1)
            .Select(each => each.Endpoint)
            .FirstOrDefault();
    }

    /// <summary>What is known about listening to one chain.</summary>
    private sealed class Listener
    {
        /// <summary>How many new blocks have been heard of; read and written with <see cref="Interlocked"/>.</summary>
        public long HeardCount;

        /// <summary>Gets or sets until when the chain is kept.</summary>
        public DateTimeOffset KeptUntil { get; set; }

        /// <summary>Gets or sets the highest block heard of.</summary>
        public long Highest { get; set; }

        /// <summary>Gets or sets when the last nudge was counted.</summary>
        public DateTimeOffset LastNudge { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Gets until when each endpoint that gave no websocket is passed over.</summary>
        public Dictionary<Guid, DateTimeOffset> PassedOver { get; } = [];

        /// <summary>Gets or sets what stops the listening at once.</summary>
        public CancellationTokenSource? Stop { get; set; }

        /// <summary>Gets or sets the listening, while it runs.</summary>
        public Task? Running { get; set; }
    }
}
