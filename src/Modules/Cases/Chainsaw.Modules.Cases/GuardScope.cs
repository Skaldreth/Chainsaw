namespace Chainsaw.Modules.Cases;

/// <summary>
/// Gives the guard its previous state back when disposed (see <see cref="CaseDatabase.As"/>).
/// </summary>
/// <param name="guard">The guard.</param>
/// <param name="previous">The state to give back.</param>
/// <param name="set">The state this scope set.</param>
internal readonly struct GuardScope(SqlGuard guard, GuardState previous, GuardState set) : IDisposable
{
    /// <summary>
    /// Gives the previous state back, but only when the guard is still as this scope left it; scopes that end out of
    /// order leave the guard <see cref="GuardMode.Idle"/>, refusing everything, never with rights to spare.
    /// </summary>
    public void Dispose() => guard.State = guard.State == set ? previous : default;
}
