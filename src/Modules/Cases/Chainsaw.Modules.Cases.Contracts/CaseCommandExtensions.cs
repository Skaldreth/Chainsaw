using System.Data.Common;

namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>Small helpers for the commands that <see cref="ICaseReader.CreateCommand"/> gives.</summary>
public static class CaseCommandExtensions
{
    /// <summary>Adds a named parameter with a value.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter's name as written in the SQL, for example <c>$address</c>.</param>
    /// <param name="value">The value; <see langword="null"/> becomes SQL NULL.</param>
    /// <returns>The same command, so that parameters can be chained.</returns>
    public static DbCommand AddParameter(this DbCommand command, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(command);
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return command;
    }
}
