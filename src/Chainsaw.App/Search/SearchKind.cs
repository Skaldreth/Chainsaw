namespace Chainsaw.App.Search;

/// <summary>
/// What a hit in the command bar's search is (sketch KR1a). The order is the groups' order under <em>Bästa
/// träff</em>: Etiketter, Adresser, Tx, Kluster and Guide.
/// </summary>
public enum SearchKind
{
    /// <summary>A label on actors in the graph.</summary>
    Label,

    /// <summary>An address in the graph.</summary>
    Address,

    /// <summary>A transaction the case has fetched, on a line in the graph or not.</summary>
    Transaction,

    /// <summary>A cluster: addresses that Clustering concludes have the same owner.</summary>
    Cluster,

    /// <summary>A section of the Guide.</summary>
    Guide,
}

/// <summary>Reads <see cref="SearchKind"/>.</summary>
public static class SearchKinds
{
    /// <summary>Gets every kind in the groups' order.</summary>
    public static IReadOnlyList<SearchKind> InOrder { get; } =
        [SearchKind.Label, SearchKind.Address, SearchKind.Transaction, SearchKind.Cluster, SearchKind.Guide];

    /// <summary>Gives the group's name, as its heading in the list.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>For example <c>Etiketter</c>.</returns>
    public static string GroupName(this SearchKind kind) => kind switch
    {
        SearchKind.Label => "Etiketter",
        SearchKind.Address => "Adresser",
        SearchKind.Transaction => "Tx",
        SearchKind.Cluster => "Kluster",
        _ => "Guide",
    };

    /// <summary>Gives the kind's name, written small beside the best hit.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>For example <c>Etikett</c>.</returns>
    public static string Singular(this SearchKind kind) => kind switch
    {
        SearchKind.Label => "Etikett",
        SearchKind.Address => "Adress",
        SearchKind.Transaction => "Tx",
        SearchKind.Cluster => "Kluster",
        _ => "Guide",
    };

    /// <summary>
    /// Gives how much the kind weighs when two hits match equally well: what the user named (a label) before what
    /// the chain names (an address, a tx), and the case before the Guide.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A small weight; higher wins.</returns>
    public static int Weight(this SearchKind kind) => kind switch
    {
        SearchKind.Label => 5,
        SearchKind.Cluster => 4,
        SearchKind.Address => 3,
        SearchKind.Transaction => 2,
        _ => 1,
    };
}
