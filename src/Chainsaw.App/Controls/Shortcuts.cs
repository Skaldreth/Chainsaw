using System.Windows.Input;

namespace Chainsaw.App.Controls;

/// <summary>Turns a shortcut as it is shown (<c>Ctrl+1</c>, <c>Ctrl+,</c>, <c>F1</c>) into a key gesture.</summary>
public static class Shortcuts
{
    /// <summary>The keys whose shown name is not the name of their <see cref="Key"/>.</summary>
    private static readonly Dictionary<string, Key> ShownKeys = new(StringComparer.Ordinal)
    {
        [","] = Key.OemComma,
        ["0"] = Key.D0, ["1"] = Key.D1, ["2"] = Key.D2, ["3"] = Key.D3, ["4"] = Key.D4,
        ["5"] = Key.D5, ["6"] = Key.D6, ["7"] = Key.D7, ["8"] = Key.D8, ["9"] = Key.D9,
    };

    /// <summary>Reads a shortcut as it is shown.</summary>
    /// <param name="shown">For example <c>Ctrl+4</c>.</param>
    /// <returns>The gesture, or <see langword="null"/> when the text is not a shortcut.</returns>
    public static KeyGesture? TryParse(string shown)
    {
        var plus = shown.LastIndexOf('+');
        var keyText = plus < 0 ? shown : shown[(plus + 1)..];
        var withCtrl = plus >= 0 && shown.StartsWith("Ctrl", StringComparison.Ordinal);
        var modifiers = withCtrl ? ModifierKeys.Control : ModifierKeys.None;
        if (!ShownKeys.TryGetValue(keyText, out var key) && !Enum.TryParse(keyText, out key))
        {
            return null;
        }

        return new KeyGesture(key, modifiers);
    }
}
