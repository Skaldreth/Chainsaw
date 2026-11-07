using System.Data.Common;

namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>Reads from an open case inside <see cref="ICase.Read{T}"/> or <see cref="ICase.Write{T}"/>.</summary>
/// <remarks>
/// Commands run under a guard in SQLite that refuses changing sealed tables, the case's own tables and the schema,
/// attaching other databases and changing settings, whatever SQL is given.
/// </remarks>
public interface ICaseReader
{
    /// <summary>Creates a command that runs in the current transaction.</summary>
    /// <param name="sql">The SQL, with parameters for every value (never values written into the text).</param>
    /// <returns>The command; the caller disposes it before the work returns.</returns>
    DbCommand CreateCommand(string sql);
}
