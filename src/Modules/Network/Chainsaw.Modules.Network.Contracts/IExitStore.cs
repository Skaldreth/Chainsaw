namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The exits Chainsaw knows, and the one the program uses. The exits and their configurations are kept encrypted in
/// the vault, so every member needs an unlocked vault and throws the vault's own exception (locked) otherwise.
/// </summary>
public interface IExitStore
{
    /// <summary>Lists the exits, ordered by name.</summary>
    /// <returns>The exits, without their configurations.</returns>
    IReadOnlyList<ExitInfo> List();

    /// <summary>Adds an exit and stores its configuration in the vault.</summary>
    /// <param name="name">The name; not blank, at most <see cref="ExitRules.MaximumNameLength"/> characters.</param>
    /// <param name="place">The country or city; may be empty, at most <see cref="ExitRules.MaximumPlaceLength"/>.</param>
    /// <param name="kind">The sort of tunnel.</param>
    /// <param name="configuration">
    /// The tunnel's configuration, for WireGuard the text of the <c>.conf</c> file; not blank, at most
    /// <see cref="ExitRules.MaximumConfigurationLength"/> characters, and with a <c>DNS =</c> line, so that names are
    /// resolved inside the tunnel. It holds the private key and is never logged.
    /// </param>
    /// <returns>The new exit with its id.</returns>
    /// <exception cref="ArgumentException">A value is not allowed.</exception>
    ExitInfo Add(string name, string place, ExitKind kind, string configuration);

    /// <summary>
    /// Gives an exit a new name and place (sketch UT3b); its configuration and selection stay. It runs in turn with
    /// <see cref="RemoveAsync"/>, so that an exit removed meanwhile is never written back.
    /// </summary>
    /// <param name="id">The exit.</param>
    /// <param name="name">The name; not blank, at most <see cref="ExitRules.MaximumNameLength"/> characters.</param>
    /// <param name="place">
    /// The country or city; may be empty, at most <see cref="ExitRules.MaximumPlaceLength"/>.
    /// </param>
    /// <returns>The exit as it is now.</returns>
    /// <exception cref="ArgumentException">A value is not allowed, or no exit has the id.</exception>
    Task<ExitInfo> DescribeAsync(Guid id, string name, string place);

    /// <summary>
    /// Removes an exit, its configuration and its latest check. When it is the selected exit, the selection is cleared and the exit is
    /// disconnected first.
    /// </summary>
    /// <param name="id">The exit.</param>
    /// <returns><see langword="true"/> when an exit was removed.</returns>
    Task<bool> RemoveAsync(Guid id);

    /// <summary>Finds the program's selected exit.</summary>
    /// <returns>The exit, or <see langword="null"/> when none is selected.</returns>
    ExitInfo? FindSelected();

    /// <summary>
    /// Selects the exit the program uses, or clears the selection. A connected exit that is no longer selected is
    /// disconnected first, so a request can never leave through an exit the user has not chosen.
    /// </summary>
    /// <param name="id">The exit, or <see langword="null"/> to select none.</param>
    /// <exception cref="ArgumentException">No exit has the id.</exception>
    Task SelectAsync(Guid? id);
}
