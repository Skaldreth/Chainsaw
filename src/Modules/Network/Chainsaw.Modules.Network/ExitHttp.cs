using System.Net;
using System.Net.Http.Headers;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Sends requests the way out chosen in the settings and nowhere else. With VPN chosen, a request goes through the
/// connected exit, and without one it is refused before anything is built or sent; it never falls back to Windows.
/// With Windows chosen it goes through the computer's own network. Only the address, the method, a neutral User-Agent
/// and, when asked, <c>Accept</c> and a body are sent: the pipeline keeps no cookies, follows no redirects and adds no
/// referer.
/// </summary>
/// <param name="connection">The connection that holds the pipeline through the exit (VPN).</param>
/// <param name="route">The way out chosen, which holds the pipeline through Windows.</param>
/// <param name="options">The limits on requests.</param>
/// <param name="clock">The clock for the request's times.</param>
internal sealed class ExitHttp(ExitConnection connection, ExitRouteChoice route, NetworkOptions options, IClock clock)
    : IExitHttp
{
    /// <summary>The size of each read from the response body.</summary>
    private const int ReadChunkBytes = 64 * 1024;

    /// <inheritdoc />
    public async Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryLease(out var lease))
        {
            throw new ExitNotConnectedException();
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Stopping);
        limit.CancelAfter(options.RequestTimeout);
        using var message = BuildMessage(request);
        var requestedAt = clock.UtcNow;
        try
        {
            using var response = await lease.Invoker.SendAsync(message, limit.Token).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, limit.Token).ConfigureAwait(false);
            return new ExitResponse(
                request.Uri,
                lease.ExitId,
                (int)response.StatusCode,
                RawContentType(response),
                body,
                requestedAt,
                clock.UtcNow,
                lease.Route);
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
            throw new ExitException(ExitError.ConnectionLost, exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new ExitException(ExitError.Timeout, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new ExitException(ExitError.RequestFailed, exception);
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

    /// <summary>Refuses a request that would send more than a lookup needs or leave the encrypted channel.</summary>
    /// <param name="request">The request.</param>
    /// <exception cref="ArgumentException">The request is not allowed.</exception>
    private void Validate(ExitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Uri, nameof(request));
        var uri = request.Uri;
        var schemeAllowed = uri.IsAbsoluteUri
            && (uri.Scheme == Uri.UriSchemeHttps || (options.AllowPlainHttp && uri.Scheme == Uri.UriSchemeHttp));
        if (!schemeAllowed)
        {
            throw new ArgumentException("The address must be an absolute https address.", nameof(request));
        }

        if (!options.AllowLocalTargets && LocalTargets.IsLocal(uri))
        {
            throw new ArgumentException(
                "The address must not be on the computer or a private network.", nameof(request));
        }

        if (uri.UserInfo.Length > 0)
        {
            throw new ArgumentException("The address must not carry user information.", nameof(request));
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

        if (request.Accept is not null && !MediaTypeWithQualityHeaderValue.TryParse(request.Accept, out _))
        {
            throw new ArgumentException("The media type asked for is not valid.", nameof(request));
        }
    }

    /// <summary>Gives the response's media type exactly as the server wrote it.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The raw header value, or <see langword="null"/> when there is none.</returns>
    private static string? RawContentType(HttpResponseMessage response) =>
        response.Content.Headers.NonValidated.TryGetValues("Content-Type", out var values)
            ? string.Join(", ", values)
            : null;

    /// <summary>Builds the message with only what the request needs.</summary>
    /// <param name="request">The checked request.</param>
    /// <returns>The message, owned by the caller.</returns>
    private static HttpRequestMessage BuildMessage(ExitRequest request)
    {
        var method = request.Method == ExitMethod.Post ? HttpMethod.Post : HttpMethod.Get;
        var message = new HttpRequestMessage(method, request.Uri)
        {
            // HTTP/1.1 exactly: HTTP/3 runs over UDP (QUIC), which neither the proxy nor the socket guard would carry.
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        message.Headers.TryAddWithoutValidation("User-Agent", NetworkOptions.UserAgent);
        message.Headers.ExpectContinue = false;
        if (request.Accept is not null)
        {
            message.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(request.Accept));
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

    /// <summary>Reads the whole body, refusing one larger than the limit before and while it is read.</summary>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">The request's limit.</param>
    /// <returns>The body's bytes as they came.</returns>
    /// <exception cref="ExitException"><see cref="ExitError.ResponseTooLarge"/>.</exception>
    private async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var maximum = options.MaximumResponseBytes;
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
