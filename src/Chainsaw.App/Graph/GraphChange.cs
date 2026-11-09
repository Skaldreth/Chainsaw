namespace Chainsaw.App.Graph;

/// <summary>
/// What changed on the graph's surface, as an event to the one frame loop (<see cref="FramePump"/>): the loop
/// gathers the changes of a frame and does the least work they need (principle 6).
/// </summary>
[Flags]
public enum GraphChange
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The viewport moved (a pan, a glide or a zoom): the scene's drawing is moved, not made again.</summary>
    View = 1,

    /// <summary>What stands on the screen over the scene changed (the box, a mark's box): only that is drawn.</summary>
    Overlay = 2,

    /// <summary>What the scene shows changed (a node chosen or hovered, a new graph, a frame of a motion).</summary>
    Scene = 4,

    /// <summary>
    /// The lines' layer changed (the lines, their amounts, the watches' rings): it is drawn again, while the nodes that
    /// stand still keep their drawing.
    /// </summary>
    Lines = 8,

    /// <summary>The loose nodes changed (dragged nodes, or an hourglass that breathes): only they are drawn again.</summary>
    Loose = 16,

    /// <summary>
    /// What stands over the nodes changed (a card's shimmer, the search's ring, the guide's sonar): only that is drawn
    /// again.
    /// </summary>
    OverNodes = 32,

    /// <summary>What a drag changes: the lines, the dragged nodes and what stands over the nodes.</summary>
    Drag = Lines | Loose | OverNodes,
}

/// <summary>
/// The motions of the graph's surface that run on the frames of the screen, in the order the loop steps them in
/// each frame.
/// </summary>
public enum GraphMotion
{
    /// <summary>The glide after a pan let go while moving.</summary>
    Glide,

    /// <summary>The smooth zoom.</summary>
    Zoom,

    /// <summary>The glide to a node found by the search.</summary>
    FoundGlide,

    /// <summary>The scrolling at the edge while a box is drawn.</summary>
    BoxScroll,

    /// <summary>The selection view's blast and the pile's fan, out and back.</summary>
    Blast,

    /// <summary>The watches' rings, the search's ring and the start guide's sonar.</summary>
    Rings,

    /// <summary>The cards that turn, what is fetched on them and the hourglass that breathes.</summary>
    Cards,
}
