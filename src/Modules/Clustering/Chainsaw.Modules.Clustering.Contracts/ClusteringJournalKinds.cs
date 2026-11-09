namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>The journal codes the Clustering module writes.</summary>
public static class ClusteringJournalKinds
{
    /// <summary>The analyses ran over transactions and stored new conclusions.</summary>
    public const string Analysed = "clustering.analysed";

    /// <summary>The account heuristics ran over a chain's transfers and sealed new conclusions.</summary>
    public const string AccountsAnalysed = "clustering.accounts-analysed";

    /// <summary>The user split two addresses that a heuristic had merged.</summary>
    public const string Split = "clustering.split";

    /// <summary>The user undid a split, so that the heuristics may merge the two addresses again.</summary>
    public const string SplitUndone = "clustering.split-undone";

    /// <summary>The user merged addresses that a heuristic proposed have the same owner (<c>user v1</c>).</summary>
    public const string Merged = "clustering.merged";

    /// <summary>The user undid a merge of their own, so that the addresses are no longer merged by it.</summary>
    public const string MergeUndone = "clustering.merge-undone";

    /// <summary>A version of an analysis was drawn back, so that its conclusions no longer count.</summary>
    public const string VersionWithdrawn = "clustering.version-withdrawn";

    /// <summary>A withdrawal of a version was taken back, so that the version runs and counts again.</summary>
    public const string WithdrawalTakenBack = "clustering.withdrawal-taken-back";

    /// <summary>The user decided about a conclusion a run held back: let it stand or let it go (sketch HB1a).</summary>
    public const string HeldBackDecided = "clustering.held-back-decided";
}
