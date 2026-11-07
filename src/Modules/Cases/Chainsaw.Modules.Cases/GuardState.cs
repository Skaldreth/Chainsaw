namespace Chainsaw.Modules.Cases;

/// <summary>Who may run SQL on a case's connection: the mode, its thread and, when sealing, the table.</summary>
/// <param name="Mode">The mode; the default is <see cref="GuardMode.Idle"/>.</param>
/// <param name="Thread">The managed thread the mode belongs to.</param>
/// <param name="SealingTable">The sealed table that <see cref="GuardMode.Sealing"/> may insert into.</param>
/// <param name="OwnTables">
/// The sealed tables of the module whose step runs in <see cref="GuardMode.Migrating"/>, which only it may create.
/// </param>
internal readonly record struct GuardState(
    GuardMode Mode, int Thread, string? SealingTable, IReadOnlyCollection<string>? OwnTables = null);
