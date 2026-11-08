using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// Where the graph's comments (sketch KN1a) read and write the case, through Tracing's contract. Every method does its
/// work off the UI thread (principle 6); what changes a comment is sealed with a line in the journal by the module.
/// </summary>
public interface IGraphComments
{
    /// <summary>Reads the comments in force.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work before it starts.</param>
    /// <returns>The comments, oldest first.</returns>
    Task<IReadOnlyList<GraphComment>> GetAsync(ICase @case, CancellationToken cancellationToken);

    /// <summary>Seals a new comment.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">The comment.</param>
    /// <returns>The sealed comment.</returns>
    Task<GraphComment> AddAsync(ICase @case, GraphCommentDraft draft);

    /// <summary>Seals a comment's new text.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <param name="text">The text.</param>
    /// <returns>The comment, or <see langword="null"/> when it is gone.</returns>
    Task<GraphComment?> EditAsync(ICase @case, string id, string text);

    /// <summary>Seals a comment's thread to another node, or none.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <param name="anchor">The node's key, or <see langword="null"/>.</param>
    /// <returns>The comment, or <see langword="null"/> when it is gone.</returns>
    Task<GraphComment?> AnchorAsync(ICase @case, string id, string? anchor);

    /// <summary>Seals that a comment is removed.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <returns><see langword="false"/> when it was gone already.</returns>
    Task<bool> RemoveAsync(ICase @case, string id);

    /// <summary>Stores where a comment stands and its size (ordinary data).</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <param name="place">Its top left corner.</param>
    /// <param name="size">Its size, or <see langword="null"/> while it grows with its text.</param>
    /// <returns><see langword="false"/> when it is gone.</returns>
    Task<bool> SavePlaceAsync(ICase @case, string id, NodePosition place, CommentSize? size);
}

/// <summary>The graph's way into Tracing's comments (<see cref="IGraphComments"/>).</summary>
/// <param name="comments">The comments.</param>
public sealed class GraphComments(IGraphCommentService comments) : IGraphComments
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<GraphComment>> GetAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.Run(() => comments.GetComments(@case), cancellationToken);

    /// <inheritdoc/>
    public Task<GraphComment> AddAsync(ICase @case, GraphCommentDraft draft) =>
        Task.Run(() => comments.AddComment(@case, draft));

    /// <inheritdoc/>
    public Task<GraphComment?> EditAsync(ICase @case, string id, string text) =>
        Task.Run(() => comments.EditComment(@case, id, text));

    /// <inheritdoc/>
    public Task<GraphComment?> AnchorAsync(ICase @case, string id, string? anchor) =>
        Task.Run(() => comments.AnchorComment(@case, id, anchor));

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(ICase @case, string id) =>
        Task.Run(() => comments.RemoveComment(@case, id));

    /// <inheritdoc/>
    public Task<bool> SavePlaceAsync(ICase @case, string id, NodePosition place, CommentSize? size) =>
        Task.Run(() => comments.SaveCommentPlace(@case, id, place, size));
}

/// <summary>Stands in for the comments where the graph is built without them, such as in some tests.</summary>
public sealed class NoGraphComments : IGraphComments
{
    /// <summary>Gets the one instance.</summary>
    public static NoGraphComments Instance { get; } = new();

    /// <inheritdoc/>
    public Task<IReadOnlyList<GraphComment>> GetAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GraphComment>>([]);

    /// <inheritdoc/>
    public Task<GraphComment> AddAsync(ICase @case, GraphCommentDraft draft) =>
        Task.FromException<GraphComment>(new InvalidOperationException("The graph has no comments."));

    /// <inheritdoc/>
    public Task<GraphComment?> EditAsync(ICase @case, string id, string text) =>
        Task.FromResult<GraphComment?>(null);

    /// <inheritdoc/>
    public Task<GraphComment?> AnchorAsync(ICase @case, string id, string? anchor) =>
        Task.FromResult<GraphComment?>(null);

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(ICase @case, string id) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<bool> SavePlaceAsync(ICase @case, string id, NodePosition place, CommentSize? size) =>
        Task.FromResult(false);
}
