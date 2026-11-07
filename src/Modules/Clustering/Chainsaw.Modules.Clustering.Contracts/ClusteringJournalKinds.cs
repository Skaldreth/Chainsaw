namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>The journal codes the Clustering module writes.</summary>
public static class ClusteringJournalKinds
{
    /// <summary>The analyses ran over transactions and stored new conclusions.</summary>
    public const string Analysed = "clustering.analysed";

    /// <summary>The user split two addresses that a heuristic had merged.</summary>
    public const string Split = "clustering.split";

    /// <summary>The user undid a split, so that the heuristics may merge the two addresses again.</summary>
    public const string SplitUndone = "clustering.split-undone";
}
