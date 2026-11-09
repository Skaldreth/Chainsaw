using System.Net.Http.Headers;
using System.Text;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Asks whether the internet answers, the way Windows' own network indicator (NCSI) does: an <c>https</c> request to a
/// known target whose answer is known, through exactly the pipeline of the way out through Windows
/// (<see cref="IWindowsHandlerFactory"/>: the computer's proxy, no cookies, no redirects), but not through
/// <see cref="Contracts.IExitHttp"/>, which refuses everything while the computer is offline.
/// </summary>
/// <remarks>
/// <para>
/// The advice was to ping <c>1.1.1.1</c>. It is deliberately not ICMP: in an organisation's network ICMP is often
/// blocked and everything goes through a proxy, so a ping would fail where every lookup works, and Chainsaw would show
/// <em>Offline</em> and refuse them all. Through the proxy, the probe asks the internet the same way the lookups do.
/// </para>
/// <para>
/// A Wi-Fi with a login page (captive portal) answers with a redirect, a certificate for another name or a page of its
/// own, so none of them counts; only a 2xx with the expected content does. Nothing about the case is sent: the targets
/// are fixed and the request carries only the generic User-Agent.
/// </para>
/// </remarks>
/// <param name="handlers">Builds the pipeline of the way out through Windows.</param>
/// <param name="time">Times the probe out; the tests move it by hand.</param>
/// <param name="login">
/// The login to Windows' proxy, told when the proxy asks for a login, so that the settings can say why the computer
/// counts as offline (sketch PX1a), and when the probe goes through; none in some tests.
/// </param>
internal sealed class InternetProbe(IWindowsHandlerFactory handlers, TimeProvider time, WindowsProxyLogin? login = null)
    : IInternetProbe
{
    /// <summary>The exact body of <see cref="WindowsConnectTest"/>.</summary>
    internal const string ConnectTestContent = "Microsoft Connect Test";

    /// <summary>The largest body that is read; both answers are far smaller, a login page often is not.</summary>
    private const int LargestBody = 4096;

    /// <summary>Cloudflare's trace on the address the advice named, which answers lines such as <c>ip=…</c>.</summary>
    internal static readonly Uri CloudflareTrace = new("https://1.1.1.1/cdn-cgi/trace");

    /// <summary>Windows' own NCSI target, asked when Cloudflare does not answer.</summary>
    internal static readonly Uri WindowsConnectTest = new("https://www.msftconnecttest.com/connecttest.txt");

    /// <summary>How long each target may take, from sending to the last byte.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>The keys that start a line of Cloudflare's real trace.</summary>
    private static readonly string[] TraceKeys = ["fl=", "ip="];

    /// <summary>The targets in the order they are asked, each with how its answer is recognised.</summary>
    private static readonly ProbeTarget[] Targets =
    [
        new(CloudflareTrace, IsTrace),
        new(WindowsConnectTest, IsConnectTest),
    ];

    /// <inheritdoc />
    public async Task<bool> ReachesInternetAsync(CancellationToken cancellationToken)
    {
        foreach (var target in Targets)
        {
            if (await AnswersAsync(target, cancellationToken).ConfigureAwait(false))
            {
                login?.NoteThrough();
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells whether a body is Cloudflare's trace: a line starts with one of its keys.</summary>
    /// <param name="body">The body.</param>
    /// <returns><see langword="true"/> for the real trace.</returns>
    internal static bool IsTrace(string body) =>
        body.Split('\n').Any(line => TraceKeys.Any(key => line.StartsWith(key, StringComparison.Ordinal)));

    /// <summary>Tells whether a body is exactly Windows' connect test.</summary>
    /// <param name="body">The body.</param>
    /// <returns><see langword="true"/> for the exact text.</returns>
    internal static bool IsConnectTest(string body) =>
        string.Equals(body, ConnectTestContent, StringComparison.Ordinal);

    /// <summary>
    /// Asks one target with a fresh pipeline, so that a new network or proxy is used at once. Every failure on the way
    /// (no answer, a refused certificate, a redirect, another body, the time running out) counts as not reached.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="cancellationToken">Stops the probe, when Chainsaw closes.</param>
    /// <returns><see langword="true"/> when the target answered 2xx with its expected body.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    private async Task<bool> AnswersAsync(ProbeTarget target, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            using var client = new HttpClient(handlers.Create(), disposeHandler: true)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Address);
            request.Headers.UserAgent.ParseAdd(NetworkOptions.UserAgent);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await ReadBodyAsync(response.Content, linked.Token).ConfigureAwait(false);
            return body is not null && target.Recognises(body);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
                                          && !cancellationToken.IsCancellationRequested)
        {
            if (WindowsProxyLogin.AsksForLogin(exception))
            {
                login?.NoteLoginAsked();
            }

            return false;
        }
    }

    /// <summary>Reads a body of at most <see cref="LargestBody"/> bytes as UTF-8.</summary>
    /// <param name="content">The body.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>The text, or <see langword="null"/> when the body is larger.</returns>
    private static async Task<string?> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[LargestBody + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            return length > LargestBody ? null : Encoding.UTF8.GetString(buffer, 0, length);
        }
    }

    /// <summary>A target and how its answer is recognised.</summary>
    /// <param name="Address">Where it is asked.</param>
    /// <param name="Recognises">Tells whether a body is the expected one.</param>
    private sealed record ProbeTarget(Uri Address, Func<string, bool> Recognises);
}
