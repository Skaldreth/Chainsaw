namespace Chainsaw.App.Graph;

/// <summary>
/// What a drag moved on the surface: the dragged nodes. The surface draws these again in their own visuals while the
/// nodes that stand still keep their drawing; anything else that changes is a full change of the scene
/// (principle 6).
/// </summary>
/// <param name="Nodes">The nodes that moved.</param>
public sealed record GraphMoved(IReadOnlySet<string> Nodes);
