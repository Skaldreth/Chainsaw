using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>One option of a row's segmented choice in the settings (sketch ST1a), such as <em>15 min</em>.</summary>
/// <param name="label">The option's words.</param>
/// <param name="value">What choosing it sets.</param>
public sealed partial class SettingsChoice(string label, object value) : ObservableObject
{
    /// <summary>Gets the option's words.</summary>
    public string Label { get; } = label;

    /// <summary>Gets what choosing it sets.</summary>
    public object Value { get; } = value;

    /// <summary>Gets what the option does, shown on hovering when the option is only an icon; empty for none.</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>Gets whether it is the option chosen.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Marks the option among several that holds a value, and no other.</summary>
    /// <param name="choices">The row's options.</param>
    /// <param name="value">The value chosen.</param>
    public static void Mark(IEnumerable<SettingsChoice> choices, object value)
    {
        foreach (var choice in choices)
        {
            choice.IsSelected = Equals(choice.Value, value);
        }
    }
}
