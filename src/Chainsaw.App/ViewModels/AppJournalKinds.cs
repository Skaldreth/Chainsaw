namespace Chainsaw.App.ViewModels;

/// <summary>The journal codes the application writes itself.</summary>
public static class AppJournalKinds
{
    /// <summary>
    /// A start point on a chain that is not built yet (for example TRON) was written when the case was created. It
    /// cannot be traced yet, so it is kept in the journal rather than dropped; the details give the chain and value.
    /// </summary>
    public const string StartPointNoted = "app.start-point-noted";
}
