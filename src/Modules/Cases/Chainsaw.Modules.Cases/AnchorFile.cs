namespace Chainsaw.Modules.Cases;

/// <summary>A case's anchors on this computer as read from or written to its anchor file.</summary>
/// <param name="Anchors">How far each chain had come at the last save.</param>
/// <param name="Generation">
/// How many times the file has been written, counting from 1; 0 when there is no file (<see cref="None"/>).
/// </param>
internal sealed record AnchorFile(IReadOnlyList<ChainAnchor> Anchors, long Generation)
{
    /// <summary>Gets the anchors of a case that has no anchor file on this computer.</summary>
    public static AnchorFile None { get; } = new([], 0);
}
