using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Sends requests to the user's own node on the computer or a private network, and nowhere else (decided 2026-11-07).
/// The request never takes the way out chosen in the settings: it is not a lookup at an external source. The address
/// is checked first (only https, no IP address outside the user's own network), and the pipeline connects only to
/// addresses in the user's own network, so that a name that points to the internet is refused before anything is sent.
/// A request with a pinned certificate (sketch KL3b) takes a pipeline of its own that accepts that certificate and no
/// other, so that a pin never loosens the check of a node without one.
/// </summary>
/// <param name="handlers">Builds the pipeline.</param>
/// <param name="options">The limits on requests.</param>
/// <param name="clock">The clock for the request's times.</param>
internal sealed class LocalHttp(ILocalHandlerFactory handlers, NetworkOptions options, IClock clock)
    : ILocalHttp, IDisposable
{
    /// <summary>Guards the pipeline.</summary>
    private readonly Lock _gate = new();

    /// <summary>The key of the pipeline without a pin.</summary>
    private const string Unpinned = "";

    /// <summary>The pipelines by pin (<see cref="Unpinned"/> for none), each built when it is first used.</summary>
    private readonly Dictionary<string, HttpMessageInvoker> _invokers = new(StringComparer.Ordinal);

    /// <summary>Whether the module has been disposed, after which nothing is sent.</summary>
    private bool _disposed;

    /// <inheritdoc />
    public async Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        ExitHttp.Validate(request, options, toOwnNode: true);
        cancellationToken.ThrowIfCancellationRequested();
        var invoker = Invoker(request.CertificatePin ?? Unpinned);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(options.RequestTimeout);
        using var message = ExitHttp.BuildMessage(request);
        var requestedAt = clock.UtcNow;
        try
        {
            using var response = await invoker.SendAsync(message, limit.Token).ConfigureAwait(false);
            var body = await ExitHttp.ReadBodyAsync(response, options.MaximumResponseBytes, limit.Token)
                .ConfigureAwait(false);
            var receivedAt = clock.UtcNow;

            // No exit and no way out: the response says that it came from the user's own node.
            return new ExitResponse(
                request.Uri, Guid.Empty, (int)response.StatusCode, ExitHttp.RawContentType(response), body,
                requestedAt, receivedAt, ExitRoute.Vpn)
            {
                IsLocal = true,
                RetryAfter = ExitHttp.RetryAfterOf(response, receivedAt),
            };
        }
        catch (Exception exception) when (Translate(exception, cancellationToken) is { } translated)
        {
            throw translated;
        }
    }

    /// <inheritdoc />
    public async Task<NodeCertificate> ReadCertificateAsync(Uri address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ExitHttp.Validate(ExitRequest.Get(address), options, toOwnNode: true);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await handlers.ReadCertificateAsync(
                new DnsEndPoint(address.IdnHost, address.Port), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (Translate(exception, cancellationToken) is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>
    /// Gives the failure of a request to the node as the user is told it: the refusals Chainsaw made itself (a name
    /// outside the user's own network, a certificate other than the pinned one) by their reason wherever they were
    /// wrapped, a time limit as such, and everything else on the way as a failed request.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="cancellationToken">The caller's token: the caller's own cancellation passes unchanged.</param>
    /// <returns>The failure to throw, or <see langword="null"/> to let the original pass.</returns>
    private static Exception? Translate(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (OwnRefusal(exception) is { } refusal)
        {
            return ReferenceEquals(refusal, exception) ? null : new ExitException(refusal.Error, exception);
        }

        return exception switch
        {
            OperationCanceledException => new ExitException(ExitError.Timeout, exception),
            HttpRequestException or IOException or ObjectDisposedException or AuthenticationException or SocketException
                => new ExitException(ExitError.RequestFailed, exception),
            _ => null,
        };
    }

    /// <summary>Frees the pipelines when the application ends.</summary>
    public void Dispose()
    {
        HttpMessageInvoker[] invokers;
        lock (_gate)
        {
            _disposed = true;
            invokers = [.. _invokers.Values];
            _invokers.Clear();
        }

        foreach (var invoker in invokers)
        {
            invoker.Dispose();
        }
    }

    /// <summary>
    /// Finds a refusal Chainsaw made itself on the way (a name outside the user's own network, a certificate other
    /// than the pinned one), however the pipeline wrapped it.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The refusal, or <see langword="null"/> when the failure was another.</returns>
    private static ExitException? OwnRefusal(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is ExitException { Error: ExitError.NotLocal or ExitError.CertificateChanged } refusal)
            {
                return refusal;
            }
        }

        return exception as ExitException;
    }

    /// <summary>Gives the pipeline for a pin, building it the first time.</summary>
    /// <param name="pin">The pin, or <see cref="Unpinned"/>.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="ExitException">It could not be built, or the module has been disposed.</exception>
    private HttpMessageInvoker Invoker(string pin)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ExitException(ExitError.RequestFailed);
            }

            if (_invokers.TryGetValue(pin, out var invoker))
            {
                return invoker;
            }

            try
            {
                var built = new HttpMessageInvoker(
                    handlers.Create(pin.Length == 0 ? null : pin), disposeHandler: true);
                _invokers[pin] = built;
                return built;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new ExitException(ExitError.RequestFailed, exception);
            }
        }
    }
}
