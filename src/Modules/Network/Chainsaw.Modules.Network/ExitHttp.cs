using System.Net;
using System.Net.Http.Headers;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Sends requests the way out chosen in the settings and nowhere else. With VPN chosen, a request goes through the
/// connected exit, and without one it is refused before anything is built or sent; it never falls back to Windows.
/// With Windows chosen it goes through the computer's own network, and while the computer has no network it is refused
/// the same way, before anything is built, so that every caller that waits for an exit waits for the network too and
/// no source is marked as failing for a fault of the computer's. Only the address, the method, a neutral User-Agent
/// and, when asked, <c>Accept</c>, a source's API key and a body are sent: the pipeline keeps no cookies, follows no
/// redirects and adds no referer.
/// </summary>
/// <param name="connection">The connection that holds the pipeline through the exit (VPN).</param>
/// <param name="route">The way out chosen, which holds the pipeline through Windows.</param>
/// <param name="presence">Whether the computer has a network, which the way through Windows needs.</param>
/// <param name="options">The limits on requests.</param>
/// <param name="clock">The clock for the request's times.</param>
/// <param name="login">
/// The login to Windows' proxy, told when the proxy asks for a login and when a request goes through; none in some
/// tests.
/// </param>
internal sealed class ExitHttp(
    ExitConnection connection,
    ExitRouteChoice route,
    INetworkPresence presence,
    NetworkOptions options,
    IClock clock,
    WindowsProxyLogin? login = null)
    : IExitHttp
{
    /// <summary>The size of each read from the response body.</summary>
    private const int ReadChunkBytes = 64 * 1024;

    /// <summary>The longest wait a source's <c>Retry-After</c> is read as; a longer one is cut to it.</summary>
    internal static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, options, toOwnNode: false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryLease(out var lease))
        {
            throw new ExitNotConnectedException();
        }

        if (lease.Route == ExitRoute.Windows && !presence.IsAvailable)
        {
            throw ExitNotConnectedException.Offline();
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Stopping);
        limit.CancelAfter(options.RequestTimeout);
        using var message = BuildMessage(request);
        var requestedAt = clock.UtcNow;
        try
        {
            using var response = await lease.Invoker.SendAsync(message, limit.Token).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, options.MaximumResponseBytes, limit.Token).ConfigureAwait(false);
            var receivedAt = clock.UtcNow;
            if (lease.Route == ExitRoute.Windows)
            {
                login?.NoteThrough();
            }

            return new ExitResponse(
                request.Uri,
                lease.ExitId,
                (int)response.StatusCode,
                RawContentType(response),
                body,
                requestedAt,
                receivedAt,
                lease.Route)
            {
                RetryAfter = RetryAfterOf(response, receivedAt),
                ServerDate = response.Headers.Date,
                ETag = response.Headers.ETag?.ToString(),
            };
        }
        catch (ExitException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (lease.Stopping.IsCancellationRequested || exception is ObjectDisposedException)
        {
            throw new ExitException(ExitError.ConnectionLost, lease.Route, exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new ExitException(ExitError.Timeout, lease.Route, exception);
        }
        catch (HttpRequestException exception)
            when (lease.Route == ExitRoute.Windows && WindowsProxyLogin.AsksForLogin(exception))
        {
            // The proxy wants a login it did not get: the source was never reached, and the settings say why.
            login?.NoteLoginAsked();
            throw new ExitException(ExitError.ProxyLoginRequired, lease.Route, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new ExitException(ExitError.RequestFailed, lease.Route, exception);
        }
    }

    /// <summary>
    /// Takes the pipeline of the way out chosen now: through Windows when it is chosen, else through the connected
    /// exit. With VPN chosen and no exit connected there is none, and Windows is never asked.
    /// </summary>
    /// <param name="lease">The pipeline, its exit and the token that stops it.</param>
    /// <returns><see langword="true"/> when the request can leave.</returns>
    private bool TryLease(out ExitLease lease) =>
        route.TryLeaseWindows(out lease) || (route.Current == ExitRoute.Vpn && connection.TryLease(out lease));

    /// <summary>
    /// Refuses a request that would send more than a lookup needs or leave the encrypted channel. A request through
    /// the way out must not be for the computer or a private network; a request to the user's own node must be https
    /// (also in the tests) and must not be for an IP address outside the user's own network.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="options">The limits, with what the tests allow.</param>
    /// <param name="toOwnNode">Whether the request goes to the user's own node (<see cref="ILocalHttp"/>).</param>
    /// <exception cref="ArgumentException">The request is not allowed.</exception>
    internal static void Validate(ExitRequest request, NetworkOptions options, bool toOwnNode)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Uri, nameof(request));
        var uri = request.Uri;
        var plainAllowed = options.AllowPlainHttp && !toOwnNode;
        var schemeAllowed = uri.IsAbsoluteUri
            && (uri.Scheme == Uri.UriSchemeHttps || (plainAllowed && uri.Scheme == Uri.UriSchemeHttp));
        if (!schemeAllowed)
        {
            throw new ArgumentException("The address must be an absolute https address.", nameof(request));
        }

        if (toOwnNode && LocalAddresses.IsOwnNetwork(uri) == false)
        {
            throw new ArgumentException(
                "An own node's address must be on the computer or a private network.", nameof(request));
        }

        if (!toOwnNode && !options.AllowLocalTargets && LocalAddresses.IsLocal(uri))
        {
            throw new ArgumentException(
                "The address must not be on the computer or a private network.", nameof(request));
        }

        if (uri.UserInfo.Length > 0)
        {
            throw new ArgumentException("The address must not carry user information.", nameof(request));
        }

        if (!toOwnNode && request.CertificatePin is not null)
        {
            throw new ArgumentException("Only a request to the own node may carry a certificate pin.", nameof(request));
        }

        if (toOwnNode && request.CertificatePin is not null && !CertificatePin.IsValid(request.CertificatePin))
        {
            throw new ArgumentException("The certificate pin is not a fingerprint.", nameof(request));
        }

        if (!Enum.IsDefined(request.Method))
        {
            throw new ArgumentException("The method is not known.", nameof(request));
        }

        if (request.Method == ExitMethod.Get && !request.Body.IsEmpty)
        {
            throw new ArgumentException("A GET request has no body.", nameof(request));
        }

        if (!request.Body.IsEmpty && !MediaTypeHeaderValue.TryParse(request.ContentType, out _))
        {
            throw new ArgumentException("A body needs a valid media type.", nameof(request));
        }

        if (request.Accept is not null && AcceptedTypes(request.Accept) is null)
        {
            throw new ArgumentException("The media type asked for is not valid.", nameof(request));
        }

        if (request.Credential is { IsWellFormed: false })
        {
            throw new ArgumentException("The key's header is not allowed or its value cannot be sent.", nameof(request));
        }

        if (request.Credential is { InQuery: true } inQuery && HasParameter(uri, inQuery.Header))
        {
            // The address already names the parameter: a second one could be read in its place, or leak the key.
            throw new ArgumentException("The address already has the key's parameter.", nameof(request));
        }
    }

    /// <summary>
    /// Reads the media types asked for: one, or a list split by commas (<c>application/json, text/plain</c>), as the
    /// <c>Accept</c> header allows. A single parse of the whole list fails, which once stopped every Esplora lookup.
    /// </summary>
    /// <param name="accept">The header's value.</param>
    /// <returns>The types, or <see langword="null"/> when one of them is not valid or there are none.</returns>
    internal static IReadOnlyList<MediaTypeWithQualityHeaderValue>? AcceptedTypes(string accept)
    {
        var types = new List<MediaTypeWithQualityHeaderValue>();
        foreach (var part in accept.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!MediaTypeWithQualityHeaderValue.TryParse(part, out var type))
            {
                return null;
            }

            types.Add(type);
        }

        return types.Count == 0 ? null : types;
    }

    /// <summary>Gives the response's media type exactly as the server wrote it.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The raw header value, or <see langword="null"/> when there is none.</returns>
    internal static string? RawContentType(HttpResponseMessage response) =>
        response.Content.Headers.NonValidated.TryGetValues("Content-Type", out var values)
            ? string.Join(", ", values)
            : null;

    /// <summary>Builds the message with only what the request needs.</summary>
    /// <param name="request">The checked request.</param>
    /// <returns>The message, owned by the caller.</returns>
    internal static HttpRequestMessage BuildMessage(ExitRequest request)
    {
        var method = request.Method == ExitMethod.Post ? HttpMethod.Post : HttpMethod.Get;
        var message = new HttpRequestMessage(method, AddressToSend(request))
        {
            // HTTP/1.1 exactly: HTTP/3 runs over UDP (QUIC), which neither the proxy nor the socket guard would carry.
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        message.Headers.TryAddWithoutValidation("User-Agent", NetworkOptions.UserAgent);
        message.Headers.ExpectContinue = false;
        foreach (var type in request.Accept is null ? [] : AcceptedTypes(request.Accept) ?? [])
        {
            message.Headers.Accept.Add(type);
        }

        if (request.Credential is { InQuery: false } credential)
        {
            message.Headers.TryAddWithoutValidation(credential.Header, credential.Value);
        }

        if (request.Method == ExitMethod.Post)
        {
            message.Content = new ReadOnlyMemoryContent(request.Body);
            if (request.ContentType is not null)
            {
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
            }
        }

        return message;
    }

    /// <summary>
    /// Gives the address the message is sent to: the request's own, with a key that goes in the address added as the
    /// last query parameter. Only the message carries it; the response keeps the request's address, which is the one
    /// sealed in a case, so the key never reaches the case.
    /// </summary>
    /// <param name="request">The checked request.</param>
    /// <returns>The address to send to.</returns>
    internal static Uri AddressToSend(ExitRequest request)
    {
        if (request.Credential is not { InQuery: true } credential)
        {
            return request.Uri;
        }

        var builder = new UriBuilder(request.Uri);
        var query = builder.Query.TrimStart('?');
        var parameter = credential.Header + "=" + credential.Value;
        builder.Query = query.Length == 0 ? parameter : query + "&" + parameter;
        return builder.Uri;
    }

    /// <summary>Tells whether an address's query already has a parameter, whatever its case.</summary>
    /// <param name="uri">The address.</param>
    /// <param name="name">The parameter.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    internal static bool HasParameter(Uri uri, string name) =>
        uri.Query.TrimStart('?').Split('&').Any(pair =>
            string.Equals(pair.Split('=')[0], name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads how long the source asks Chainsaw to wait (<c>Retry-After</c>), as a delay or a date counted from when
    /// the response arrived. A date in the past is no wait; a wait longer than <see cref="MaximumRetryAfter"/> is cut
    /// to it, so that a source can never stop a chain for good.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="receivedAt">When it arrived, in UTC.</param>
    /// <returns>The wait, or <see langword="null"/> when the source did not ask for one or it cannot be read.</returns>
    internal static TimeSpan? RetryAfterOf(HttpResponseMessage response, DateTimeOffset receivedAt)
    {
        var header = response.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date is { } date ? date - receivedAt : null);
        return wait is { } found
            ? TimeSpan.FromTicks(Math.Clamp(found.Ticks, 0, MaximumRetryAfter.Ticks))
            : null;
    }

    /// <summary>Reads the whole body, refusing one larger than the limit before and while it is read.</summary>
    /// <param name="response">The response.</param>
    /// <param name="maximum">The largest body accepted, in bytes.</param>
    /// <param name="cancellationToken">The request's limit.</param>
    /// <returns>The body's bytes as they came.</returns>
    /// <exception cref="ExitException"><see cref="ExitError.ResponseTooLarge"/>.</exception>
    internal static async Task<byte[]> ReadBodyAsync(
        HttpResponseMessage response, int maximum, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maximum)
        {
            throw new ExitException(ExitError.ResponseTooLarge);
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var body = new MemoryStream();
            var chunk = new byte[ReadChunkBytes];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (body.Length + read > maximum)
                {
                    throw new ExitException(ExitError.ResponseTooLarge);
                }

                body.Write(chunk, 0, read);
            }

            return body.ToArray();
        }
    }
}
