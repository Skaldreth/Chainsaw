using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Chainsaw.App.Navigation;

namespace Chainsaw.App.Tests;

public sealed partial class ViewTableTests
{
    [Fact]
    public void The_rail_follows_the_work_from_new_to_settings()
    {
        var names = ViewTable.All.Select(e => e.Name);

        Assert.Equal(
            ["Nytt", "Graf", "Tidslinje", "Flöde", "Exponering", "Begäran", "Bevakning", "Etiketter",
             "Guide", "Inställningar"],
            names);
    }

    [Fact]
    public void Search_is_not_a_view_but_a_shortcut_to_the_command_bar()
    {
        Assert.DoesNotContain(ViewTable.All, e => e.Name == "Sök");
        Assert.Equal("Ctrl+F", ViewTable.SearchGesture);
        Assert.Equal("Ctrl+K", ViewTable.CommandGesture);
    }

    [Fact]
    public void Every_view_has_its_own_shortcut()
    {
        var gestures = ViewTable.All.Select(e => e.Gesture)
            .Append(ViewTable.CommandGesture)
            .Append(ViewTable.SearchGesture);

        Assert.Equal(gestures.Count(), gestures.Distinct().Count());
    }

    [Fact]
    public void Every_view_is_in_the_table_once()
    {
        Assert.Equal(Enum.GetValues<AppView>(), ViewTable.All.Select(e => e.View).Order());
    }

    [Fact]
    public void The_tooltip_names_the_view_and_its_shortcut()
    {
        Assert.Equal("Graf · Ctrl+1", ViewTable.For(AppView.Graph).ToolTip);
    }

    [Fact]
    public void Every_icon_the_table_names_is_in_the_theme()
    {
        var icons = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Chainsaw.App", "Themes", "Icons.xaml"));
        var keys = IconKey().Matches(icons).Select(m => m.Groups[1].Value).ToHashSet();

        Assert.All(ViewTable.All, e => Assert.Contains(e.Icon, keys));
    }

    private static string RepositoryRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [GeneratedRegex("x:Key=\"(Icon\\.[A-Za-z]+)\"")]
    private static partial Regex IconKey();
}
