namespace Chainsaw.Modules.Cases;

/// <summary>
/// Gives the guard its previous mode and thread back when disposed (see <see cref="CaseDatabase.As"/>).
/// </summary>
/// <param name="guard">The guard.</param>
/// <param name="previous">The mode and thread to give back.</param>
/// <param name="set">The mode and thread this scope set.</param>
internal readonly struct GuardScope(
    SqlGuard guard, (GuardMode Mode, int Thread) previous, (GuardMode Mode, int Thread) set) : IDisposable
{
    /// <summary>
    /// Gives the previous mode and thread back, but only when the guard is still as this scope left it; scopes that
    /// end out of order leave the guard <see cref="GuardMode.Idle"/>, refusing everything, never with rights to spare.
    /// </summary>
    public void Dispose()
    {
        var undone = guard.Mode == set.Mode && guard.OwnerThread == set.Thread;
        guard.Mode = undone ? previous.Mode : GuardMode.Idle;
        guard.OwnerThread = undone ? previous.Thread : 0;
    }
}
