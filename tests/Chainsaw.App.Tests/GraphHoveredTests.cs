using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>
/// What a hover changes in the drawing (principle 6): the node left and the node reached, and whether the hovered line
/// changed; nothing when the pointer stays on the same node and line.
/// </summary>
public sealed class GraphHoveredTests
{
    [Fact]
    public void Moving_from_one_node_to_another_changes_both()
    {
        var hovered = GraphHovered.Between("a", "b", null, null);

        Assert.Equal(["a", "b"], hovered.Nodes.Order());
        Assert.False(hovered.EdgeChanged);
    }

    [Fact]
    public void Moving_from_a_line_onto_empty_surface_changes_only_the_line()
    {
        var hovered = GraphHovered.Between(null, null, "a-b", null);

        Assert.Empty(hovered.Nodes);
        Assert.True(hovered.EdgeChanged);
    }

    [Fact]
    public void Staying_on_the_same_node_changes_nothing()
    {
        var hovered = GraphHovered.Between("a", "a", null, null);

        Assert.Empty(hovered.Nodes);
        Assert.False(hovered.EdgeChanged);
    }
}
