using Chainsaw.App.Graph;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// What the view Etiketter (sketch ET1a) and <em>Nytt → Etikett</em> ask of the graph: a label's node chosen and shown,
/// and the label form opened on the chosen node.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>What the graph says when a label's node is not in it.</summary>
    public const string LabelNotInGraphText = "Etikettens nod finns inte i grafen.";

    /// <summary>What the graph says when <em>Nytt → Etikett</em> has no node to label.</summary>
    public const string ChooseNodeForLabelText = "Välj noden i grafen som etiketten gäller, och tryck L.";

    /// <summary><em>Visa i grafen</em> (G): a node is chosen and shown, the rest drawn faint.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when the node is in the graph.</returns>
    public bool ShowNode(string nodeKey)
    {
        if (Scene?.Node(nodeKey) is not { } node)
        {
            Say(LabelNotInGraphText);
            return false;
        }

        Choose(node.Key, null);
        ShowNodesWanted?.Invoke(this, [node.Key]);
        return true;
    }

    /// <summary>
    /// <em>Nytt → Etikett</em>: the label form opens beside the chosen node, as L does; without a node that can carry a
    /// label the graph says how one is set.
    /// </summary>
    public void NewLabel()
    {
        if (SelectedNode is null || !Forms.CanLabel)
        {
            Say(ChooseNodeForLabelText);
            return;
        }

        if (Forms.Form != InspectorForm.Label)
        {
            Forms.Open(InspectorForm.Label);
        }
    }
}
