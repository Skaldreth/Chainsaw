using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>The graph's way into the trash (sketch SO1a), off the UI thread, so that the tests can stand in for it.</summary>
public interface ITrashSource
{
    /// <summary>Reads what lies in the trash.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The throws, oldest first.</returns>
    Task<IReadOnlyList<TrashThrow>> ReadAsync(ICase @case, CancellationToken cancellationToken);

    /// <summary>Throws nodes in the trash, sealed with a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="members">What is thrown and what follows, as a tree.</param>
    /// <returns>The throw.</returns>
    Task<TrashThrow> ThrowAsync(ICase @case, IReadOnlyList<TrashMember> members);

    /// <summary>Puts a member with its branch, or a whole throw, back.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="throwSequence">The throw.</param>
    /// <param name="key">The member, or <see langword="null"/> for the whole throw.</param>
    /// <returns>What was put back.</returns>
    Task<IReadOnlyList<TrashMember>> RestoreAsync(ICase @case, long throwSequence, string? key);

    /// <summary>Reads every throw and putting back of one thing, for its history (sketch AN1b).</summary>
    /// <param name="case">The open case.</param>
    /// <param name="key">The thing's key in the trash.</param>
    /// <param name="cancellationToken">Stops the work before it starts.</param>
    /// <returns>The records, oldest first.</returns>
    Task<IReadOnlyList<TrashRecord>> HistoryAsync(ICase @case, string key, CancellationToken cancellationToken);
}

/// <summary>The trash through the Tracing module, each call off the UI thread.</summary>
/// <param name="trash">The module's trash.</param>
public sealed class TrashSource(IGraphTrash trash) : ITrashSource
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<TrashThrow>> ReadAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.Run(() => trash.GetThrows(@case), cancellationToken);

    /// <inheritdoc/>
    public Task<TrashThrow> ThrowAsync(ICase @case, IReadOnlyList<TrashMember> members) =>
        Task.Run(() => trash.Throw(@case, members));

    /// <inheritdoc/>
    public Task<IReadOnlyList<TrashMember>> RestoreAsync(ICase @case, long throwSequence, string? key) =>
        Task.Run(() => trash.Restore(@case, throwSequence, key));

    /// <inheritdoc/>
    public Task<IReadOnlyList<TrashRecord>> HistoryAsync(
        ICase @case, string key, CancellationToken cancellationToken) =>
        Task.Run(() => trash.GetHistory(@case, key), cancellationToken);
}
