namespace Chainsaw.App.Settings;

/// <summary>
/// Which suppliers of attribution are on (the user's decision 2026-11-08: several sources at once, each with its own
/// switch on the page Källor). Off by default. The page switches them; the settings write the names to
/// <c>settings.json</c> (<see cref="AppSettings.AttributionProviders"/>), and whoever asks the suppliers, such as the
/// graph, reads <see cref="Enabled"/> and passes it to <c>IAttributionProviders.LookupAsync</c>.
/// </summary>
public sealed class AttributionSwitches
{
    /// <summary>Guards the names, which the graph may read from another thread.</summary>
    private readonly Lock _gate = new();

    /// <summary>The names of the suppliers that are on, compared without regard to case.</summary>
    private readonly HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the switches as the settings' file says.</summary>
    /// <param name="enabled">The names of the suppliers that are on.</param>
    public AttributionSwitches(IEnumerable<string>? enabled = null)
    {
        foreach (var name in enabled ?? [])
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _enabled.Add(name.Trim());
            }
        }
    }

    /// <summary>Raised after a supplier is switched on or off.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the names of the suppliers that are on, in a fixed order, as a copy.</summary>
    public IReadOnlyList<string> Enabled
    {
        get
        {
            lock (_gate)
            {
                return [.. _enabled.Order(StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>Tells whether a supplier is on.</summary>
    /// <param name="name">The supplier's name, such as <c>chainabuse</c>.</param>
    /// <returns><see langword="true"/> when it is on.</returns>
    public bool IsOn(string name)
    {
        lock (_gate)
        {
            return _enabled.Contains(name);
        }
    }

    /// <summary>Switches a supplier on or off, and says so when it changed.</summary>
    /// <param name="name">The supplier's name.</param>
    /// <param name="on">Whether it is to be on.</param>
    public void Set(string name, bool on)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        bool changed;
        lock (_gate)
        {
            changed = on ? _enabled.Add(name) : _enabled.Remove(name);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
