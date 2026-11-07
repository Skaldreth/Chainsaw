using System.Text.RegularExpressions;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>The exits in the vault and the program's selected exit.</summary>
/// <param name="records">The exits as secrets in the vault.</param>
/// <param name="connection">The connection, which is disconnected before its exit is deselected or removed.</param>
internal sealed partial class ExitStore(ExitRecords records, ExitConnection connection) : IExitStore
{
    /// <summary>A WireGuard line that names a DNS server, for example <c>DNS = 10.64.0.1</c>.</summary>
    private static readonly Regex DnsLine = DnsLinePattern();

    /// <inheritdoc />
    public IReadOnlyList<ExitInfo> List() => records.List();

    /// <inheritdoc />
    public ExitInfo Add(string name, string place, ExitKind kind, string configuration)
    {
        RequireText(name, ExitRules.MaximumNameLength, allowEmpty: false, nameof(name));
        RequireText(place, ExitRules.MaximumPlaceLength, allowEmpty: true, nameof(place));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("The sort of exit is not known.", nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(configuration) || configuration.Length > ExitRules.MaximumConfigurationLength)
        {
            // The message names the rule only; the configuration holds a private key and is never part of a message.
            throw new ArgumentException(
                $"The configuration must not be blank or longer than {ExitRules.MaximumConfigurationLength} characters.",
                nameof(configuration));
        }

        if (!DnsLine.IsMatch(configuration))
        {
            throw new ArgumentException(
                "The configuration must name a DNS server (DNS =), so that names are resolved inside the tunnel.",
                nameof(configuration));
        }

        var exit = new ExitInfo(Guid.NewGuid(), name.Trim(), place.Trim(), kind);
        records.Save(exit.Id, new StoredExit(exit.Name, exit.Place, kind, configuration));
        return exit;
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid id) =>
        connection.ChangeExitsAsync(
            leaveConnected: active => active != id,
            change: () =>
            {
                if (records.SelectedId() == id)
                {
                    records.SetSelected(null);
                }

                return records.Remove(id);
            });

    /// <inheritdoc />
    public ExitInfo? FindSelected() => records.FindSelected()?.Exit;

    /// <inheritdoc />
    public Task SelectAsync(Guid? id)
    {
        // Checked before anything is disconnected, and again inside, in case the exit is removed meanwhile.
        RequireKnown(id);
        return connection.ChangeExitsAsync(
            leaveConnected: active => active == id,
            change: () =>
            {
                RequireKnown(id);
                records.SetSelected(id);
                return true;
            });
    }

    /// <summary>Refuses an id that no stored exit has.</summary>
    /// <param name="id">The exit, or <see langword="null"/> for none, which is always allowed.</param>
    /// <exception cref="ArgumentException">No exit has the id.</exception>
    private void RequireKnown(Guid? id)
    {
        if (id is { } exit && records.Find(exit) is null)
        {
            throw new ArgumentException("No exit has the id.", nameof(id));
        }
    }

    /// <summary>Checks a piece of text that describes an exit.</summary>
    /// <param name="value">The text.</param>
    /// <param name="maximumLength">The longest allowed length.</param>
    /// <param name="allowEmpty">Whether empty (or blank) text is allowed.</param>
    /// <param name="parameterName">The parameter's name for the exception.</param>
    /// <exception cref="ArgumentException">The text is not allowed.</exception>
    private static void RequireText(string value, int maximumLength, bool allowEmpty, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Trim().Length > maximumLength)
        {
            throw new ArgumentException($"The text is blank or longer than {maximumLength} characters.", parameterName);
        }
    }

    /// <summary>Builds the pattern for <see cref="DnsLine"/>.</summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^[ \t]*DNS[ \t]*=[ \t]*\S", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DnsLinePattern();
}
