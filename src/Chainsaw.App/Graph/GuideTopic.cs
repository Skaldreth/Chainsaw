using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>The Guide's sections that the graph's <em>Varför?</em> links to (the texts in <c>docs/guide</c>).</summary>
public enum GuideTopic
{
    /// <summary><em>Startpunkter</em>.</summary>
    StartPoints,

    /// <summary><em>Spårningsmetoder: FIFO och haircut</em>.</summary>
    Methods,

    /// <summary><em>Varför ett spår tar slut</em>.</summary>
    TrailEnds,

    /// <summary><em>Etiketter och varifrån de kommer</em>.</summary>
    Labels,

    /// <summary>The common-input-ownership heuristic.</summary>
    CommonInputOwnership,

    /// <summary>The change-address heuristic.</summary>
    ChangeAddress,

    /// <summary>Peel chains.</summary>
    PeelChain,

    /// <summary>CoinJoin.</summary>
    CoinJoin,

    /// <summary><em>UTXO-modellen</em>.</summary>
    Utxo,

    /// <summary><em>Grafen och bildspråket</em>.</summary>
    Graph,
}

/// <summary>Reads <see cref="GuideTopic"/>.</summary>
public static class GuideTopics
{
    /// <summary>Gives the section's file in <c>docs/guide</c>, without <c>.md</c>.</summary>
    /// <param name="topic">The section.</param>
    /// <returns>The file's name.</returns>
    public static string FileName(this GuideTopic topic) => topic switch
    {
        GuideTopic.StartPoints => "startpunkter",
        GuideTopic.Methods => "sparningsmetoder",
        GuideTopic.TrailEnds => "sparet-tar-slut",
        GuideTopic.Labels => "etiketter",
        GuideTopic.CommonInputOwnership => "common-input-ownership",
        GuideTopic.ChangeAddress => "vaxeladress",
        GuideTopic.PeelChain => "peel-chain",
        GuideTopic.CoinJoin => "coinjoin",
        GuideTopic.Graph => "grafen",
        _ => "utxo-modellen",
    };

    /// <summary>Gives the section's title, as the link says it.</summary>
    /// <param name="topic">The section.</param>
    /// <returns>The title, in Swedish.</returns>
    public static string Title(this GuideTopic topic) => topic switch
    {
        GuideTopic.StartPoints => "Startpunkter",
        GuideTopic.Methods => "Spårningsmetoder",
        GuideTopic.TrailEnds => "Varför ett spår tar slut",
        GuideTopic.Labels => "Etiketter",
        GuideTopic.CommonInputOwnership => "Gemensamma inputs",
        GuideTopic.ChangeAddress => "Växeladress",
        GuideTopic.PeelChain => "Peel chain",
        GuideTopic.CoinJoin => "CoinJoin",
        GuideTopic.Graph => "Grafen och bildspråket",
        _ => "UTXO-modellen",
    };

    /// <summary>Gives the section that explains a heuristic.</summary>
    /// <param name="heuristic">The heuristic's name, from <see cref="HeuristicNames"/>.</param>
    /// <returns>The section, or <see langword="null"/> for a heuristic the Guide does not explain.</returns>
    public static GuideTopic? ForHeuristic(string heuristic) => heuristic switch
    {
        HeuristicNames.CommonInputOwnership => GuideTopic.CommonInputOwnership,
        HeuristicNames.ChangeAddress => GuideTopic.ChangeAddress,
        HeuristicNames.PeelChain => GuideTopic.PeelChain,
        HeuristicNames.CoinJoin => GuideTopic.CoinJoin,
        _ => null,
    };
}
