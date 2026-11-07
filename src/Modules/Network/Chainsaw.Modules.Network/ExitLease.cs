using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>What one request needs from the connected exit, taken at once so that it is consistent.</summary>
/// <param name="ExitId">The exit the request leaves through, or <see cref="Guid.Empty"/> through Windows.</param>
/// <param name="Invoker">The pipeline through the exit's proxy.</param>
/// <param name="Stopping">Cancelled when the exit is disconnected, or when Windows is left as the way out.</param>
/// <param name="Route">The sort of way out the request takes.</param>
internal readonly record struct ExitLease(
    Guid ExitId, HttpMessageInvoker Invoker, CancellationToken Stopping, ExitRoute Route);
