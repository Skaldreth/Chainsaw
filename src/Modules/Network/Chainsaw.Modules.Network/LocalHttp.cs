using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Sends requests to the user's own node on the computer or a private network, and nowhere else (decided 2026-11-07).
/// The request never takes the way out chosen in the settings: it is not a lookup at an external source. The address
/// is checked first (only https, no IP address outside the user's own network), and the pipeline connects only to
/// addresses in the user's own network, so that a name that points to the internet is refused before anything is sent.
/// </summary>
/// <param name="handlers">Builds the pipeline.</param>
/// <param name="options">The limits on requests.</param>
/// <param name="clock">The clock for the request's times.</param>
internal sealed class LocalHttp(ILocalHandlerFactory handlers, NetworkOptions options, IClock clock)
    : ILocalHttp, IDisposable
{
    /// <summary>Guards the pipeline.</summary>
    private readonly Lock _gate = new();

    /// <summary>The pipeline, built when it is first used; <see langword="null"/> before and after dispose.</summary>
    private HttpMessageInvoker? _invoker;

    /// <summary>Whether the module has been disposed, after which nothing is sent.</summary>
    private bool _disposed;

    /// <inheritdoc />
    public async Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        ExitHttp.Validate(request, options, toOwnNode: true);
        cancellationToken.ThrowIfCancellationRequested();
        var invoker = Invoker();
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
        catch (ExitException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new ExitException(ExitError.Timeout, exception);
        }
        catch (Exception exception) when (NotLocal(exception))
        {
            throw new ExitException(ExitError.NotLocal, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or ObjectDisposedException)
        {
            throw new ExitException(ExitError.RequestFailed, exception);
        }
    }

    /// <summary>Frees the pipeline when the application ends.</summary>
    public void Dispose()
    {
        HttpMessageInvoker? invoker;
        lock (_gate)
        {
            _disposed = true;
            invoker = _invoker;
            _invoker = null;
        }

        invoker?.Dispose();
    }

    /// <summary>Tells whether a failure was the refusal of a name that points to the internet.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns><see langword="true"/> when the connection was refused for that reason.</returns>
    private static bool NotLocal(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is ExitException { Error: ExitError.NotLocal })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gives the pipeline, building it the first time.</summary>
    /// <returns>The pipeline.</returns>
    /// <exception cref="ExitException">It could not be built, or the module has been disposed.</exception>
    private HttpMessageInvoker Invoker()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ExitException(ExitError.RequestFailed);
            }

            if (_invoker is not null)
            {
                return _invoker;
            }

            try
            {
                return _invoker = new HttpMessageInvoker(handlers.Create(), disposeHandler: true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new ExitException(ExitError.RequestFailed, exception);
            }
        }
    }
}
