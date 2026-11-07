namespace Chainsaw.Modules.Cases;

/// <summary>Gives the guard its previous mode back when disposed (see <see cref="CaseDatabase.As"/>).</summary>
/// <param name="guard">The guard.</param>
/// <param name="previous">The mode to give back.</param>
internal readonly struct GuardScope(SqlGuard guard, GuardMode previous) : IDisposable
{
    /// <summary>Gives the previous mode back.</summary>
    public void Dispose() => guard.Mode = previous;
}
