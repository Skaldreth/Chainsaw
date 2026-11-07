namespace Chainsaw.Modules.Cases;

/// <summary>Gives the guard its previous mode and thread back when disposed (see <see cref="CaseDatabase.As"/>).</summary>
/// <param name="guard">The guard.</param>
/// <param name="previousMode">The mode to give back.</param>
/// <param name="previousThread">The thread the previous mode belonged to.</param>
internal readonly struct GuardScope(SqlGuard guard, GuardMode previousMode, int previousThread) : IDisposable
{
    /// <summary>Gives the previous mode and thread back.</summary>
    public void Dispose()
    {
        guard.Mode = previousMode;
        guard.OwnerThread = previousThread;
    }
}
