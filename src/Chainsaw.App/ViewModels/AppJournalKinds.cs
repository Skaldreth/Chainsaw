namespace Chainsaw.App.ViewModels;

/// <summary>The journal codes the application writes itself.</summary>
public static class AppJournalKinds
{
    /// <summary>
    /// A start point on a chain that is not built yet (for example TRON) was written when the case was created. It
    /// cannot be traced yet, so it is kept in the journal rather than dropped; the details give the chain and value.
    /// </summary>
    public const string StartPointNoted = "app.start-point-noted";

    /// <summary>
    /// The raw responses Nytt ärende fetched for its start points before the case existed (sketch NC2a) were sealed
    /// in the case when it was created, so that everything the panel showed is in it (principle 1); the details give
    /// how many.
    /// </summary>
    public const string PrefetchSealed = "app.prefetch-sealed";
}
