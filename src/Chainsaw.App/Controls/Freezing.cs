using System.Windows;

namespace Chainsaw.App.Controls;

/// <summary>Freezes what the drawn controls share between renders and threads.</summary>
internal static class Freezing
{
    /// <summary>Freezes a dash style, shape, ease or other freezable, so that it is shared and never watched.</summary>
    /// <typeparam name="T">The freezable's type.</typeparam>
    /// <param name="value">The freezable.</param>
    /// <returns>The same freezable, frozen.</returns>
    public static T Frozen<T>(T value)
        where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
