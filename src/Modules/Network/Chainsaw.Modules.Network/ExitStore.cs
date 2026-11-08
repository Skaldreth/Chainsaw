using System.Net;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>The exits in the vault and the program's selected exit.</summary>
/// <param name="records">The exits as secrets in the vault.</param>
/// <param name="connection">The connection, which is disconnected before its exit is deselected or removed.</param>
internal sealed class ExitStore(ExitRecords records, ExitConnection connection) : IExitStore
{
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

        if (!NamesDnsServer(configuration))
        {
            throw new ArgumentException(
                "The configuration must name a DNS server by its IP address (DNS =) under [Interface], so that names "
                + "are resolved inside the tunnel.",
                nameof(configuration));
        }

        var exit = new ExitInfo(Guid.NewGuid(), name.Trim(), place.Trim(), kind);
        records.Save(exit.Id, new StoredExit(exit.Name, exit.Place, kind, configuration));
        return exit;
    }

    /// <inheritdoc />
    public Task<ExitInfo> DescribeAsync(Guid id, string name, string place)
    {
        RequireText(name, ExitRules.MaximumNameLength, allowEmpty: false, nameof(name));
        RequireText(place, ExitRules.MaximumPlaceLength, allowEmpty: true, nameof(place));

        // In turn with a removal, which takes the exit away under the same lock: a removed exit is never written back.
        return connection.ChangeExitsAsync(
            leaveConnected: _ => true,
            change: () =>
            {
                var found = records.Find(id) ?? throw new ArgumentException("No exit has the id.", nameof(id));
                var exit = found.Exit with { Name = name.Trim(), Place = place.Trim() };
                records.Save(id, new StoredExit(exit.Name, exit.Place, exit.Kind, found.Configuration));
                return exit;
            });
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

                records.RemoveSighting(id);
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

    /// <summary>
    /// Tells whether a WireGuard file names a DNS server by its IP address under <c>[Interface]</c>, for example
    /// <c>DNS = 10.64.0.1</c>. A value that is not an address is a search domain in wg-quick and names no server. The
    /// helper checks the whole file when it starts the tunnel; this only catches the missing server early.
    /// </summary>
    /// <param name="configuration">The WireGuard file.</param>
    /// <returns><see langword="true"/> when a server is named.</returns>
    internal static bool NamesDnsServer(string configuration)
    {
        var section = string.Empty;
        foreach (var raw in configuration.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim();
            if (line.StartsWith('['))
            {
                section = line.Trim('[', ']', ' ', '\t');
                continue;
            }

            var parts = line.Split('=', 2);
            var isDns = parts.Length == 2
                && section.Equals("Interface", StringComparison.OrdinalIgnoreCase)
                && parts[0].Trim().Equals("DNS", StringComparison.OrdinalIgnoreCase);
            if (isDns && parts[1].Split(',').Any(item => IsAddress(item.Trim())))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tells whether a text is an IP address in the forms the helper accepts: four plain decimal numbers for IPv4 (not
    /// the old short, octal or hex forms such as <c>10</c>, <c>010.0.0.1</c> or <c>0x0a.0.0.1</c>, which the helper
    /// ignores), or IPv6.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is an address.</returns>
    private static bool IsAddress(string text)
    {
        // Brackets and a port are accepted by .NET but not by the helper.
        if (text.Contains('[', StringComparison.Ordinal) || !IPAddress.TryParse(text, out var address))
        {
            return false;
        }

        return address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || address.ToString() == text;
    }
}
