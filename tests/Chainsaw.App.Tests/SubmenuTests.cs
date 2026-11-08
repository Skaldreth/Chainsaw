using System.Globalization;
using System.Text.RegularExpressions;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

public sealed class SubmenuTableTests
{
    [Fact]
    public void Nytt_has_the_rows_of_PN1_in_their_order_with_their_keys()
    {
        var rows = SubmenuTable.New.Select(r => r.IsLine ? "—" : $"{r.Name} {r.Gesture}");

        Assert.Equal(
            ["Startpunkt S", "Etikett E", "Bevakning B", "Begäran R", "—", "Importera sanktionslista … I", "—",
                "Ärende Ctrl+N"],
            rows);
    }

    [Fact]
    public void Only_Nytt_has_a_submenu()
    {
        Assert.Equal([AppView.New], ViewTable.All.Where(e => SubmenuTable.For(e.View) is not null).Select(e => e.View));
    }

    [Fact]
    public void The_case_row_shows_the_same_shortcut_as_the_window()
    {
        var row = SubmenuTable.New.Single(r => r.Action == SubmenuAction.NewCase);

        Assert.Equal(ViewTable.For(AppView.New).Gesture, row.Gesture);
        Assert.Null(row.Letter);
    }

    [Fact]
    public void Letters_are_unique_so_that_one_key_chooses_one_row()
    {
        var letters = SubmenuTable.New.Select(r => r.Letter).OfType<char>().ToList();

        Assert.Equal(letters.Distinct().Count(), letters.Count);
        Assert.Equal(['S', 'E', 'B', 'R', 'I'], letters);
    }

    [Fact]
    public void Etikett_needs_an_open_case()
    {
        // Etikett opens the label form on the graph's chosen node (sketch ET1a).
        Assert.Equal(
            SubmenuNeed.OpenCase, SubmenuTable.New.Single(r => r.Action == SubmenuAction.Label).Need);
    }
}

public sealed class SubmenuShapeTests
{
    [Fact]
    public void The_menu_is_as_tall_as_its_rows_and_lines()
    {
        // Six rows of 30 px, two lines of 9 px and 4 px above and below, as in the sketch.
        Assert.Equal(4 + (6 * 30) + (2 * 9) + 4, SubmenuShape.MenuHeight(SubmenuTable.New));
    }

    [Fact]
    public void The_tab_reaches_six_pixels_past_the_pill()
    {
        // The button is 7 px into the pill, which is 52 px wide: its right edge is 45 px from the button's left.
        Assert.Equal(45 + 6, SubmenuShape.TabWidth);
        Assert.Equal(SubmenuShape.TabWidth + SubmenuShape.MenuWidth, SubmenuShape.Width);
    }

    [Fact]
    public void The_outline_is_the_sketch_shape()
    {
        Assert.Equal(
            "M19,0 H273 Q281,0 281,8 V198 Q281,206 273,206 H59 Q51,206 51,198 V46 Q51,38 43,38 H19 A19,19 0 0 1 19,0 Z",
            SubmenuShape.Outline(206));
    }

    [Fact]
    public void The_outline_never_folds_over_itself_for_a_short_menu()
    {
        var numbers = Regex.Matches(SubmenuShape.Outline(10), @"V(\d+)").Select(m => int.Parse(m.Groups[1].Value,
            CultureInfo.InvariantCulture)).ToList();

        // The right side goes down to h - r and the inner side up to the tab's bottom + r: never below the tab.
        Assert.True(numbers[0] >= SubmenuShape.TabHeight + SubmenuShape.CornerRadius);
    }

    [Fact]
    public void The_outline_uses_a_point_whatever_the_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            var outline = SubmenuShape.Outline(100.5);

            Assert.Contains("V92.5 Q281,100.5 273,100.5", outline, StringComparison.Ordinal);
            Assert.DoesNotContain("92,5", outline, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}

public sealed class SubmenuViewModelTests
{
    [Fact]
    public void Without_a_case_what_is_not_built_is_toned_and_the_rest_can_be_chosen()
    {
        var menu = Menu(hasCase: false);

        Assert.Equal(["Startpunkt", "Importera sanktionslista …", "Ärende"], Enabled(menu));
        Assert.All(menu.Rows.Where(r => !r.IsEnabled && !r.Row.IsLine), r => Assert.NotNull(r.ToolTip));
    }

    [Fact]
    public void A_row_that_needs_a_case_is_toned_without_one_and_says_so()
    {
        var menu = new SubmenuViewModel([new(SubmenuAction.Label, "Etikett", "E", SubmenuNeed.OpenCase)]);

        Assert.False(menu.Rows[0].IsEnabled);
        Assert.Equal(SubmenuTable.NeedsCaseText, menu.Rows[0].ToolTip);

        menu.Update(hasCase: true);

        Assert.True(menu.Rows[0].IsEnabled);
        Assert.Null(menu.Rows[0].ToolTip);
    }

    [Fact]
    public void Lines_are_never_chosen_and_need_no_tooltip()
    {
        var lines = Menu(hasCase: true).Rows.Where(r => r.Row.IsLine).ToList();

        Assert.Equal(2, lines.Count);
        Assert.All(lines, r => Assert.False(r.IsEnabled));
        Assert.All(lines, r => Assert.Null(r.ToolTip));
    }

    [Fact]
    public void Opening_marks_the_first_row_that_can_be_chosen()
    {
        var menu = Menu(hasCase: true);

        menu.Toggle();

        Assert.True(menu.IsOpen);
        Assert.Equal("Startpunkt", menu.Marked?.Row.Name);
    }

    [Fact]
    public void The_arrows_skip_lines_and_toned_rows_and_go_round()
    {
        var menu = Menu(hasCase: true);
        menu.Open();

        menu.MoveNext();
        Assert.Equal("Etikett", menu.Marked?.Row.Name);
        menu.MoveNext();
        Assert.Equal("Bevakning", menu.Marked?.Row.Name);
        menu.MoveNext();
        Assert.Equal("Begäran", menu.Marked?.Row.Name);
        menu.MoveNext();
        Assert.Equal("Importera sanktionslista …", menu.Marked?.Row.Name);
        menu.MoveNext();
        Assert.Equal("Ärende", menu.Marked?.Row.Name);
        menu.MoveNext();
        Assert.Equal("Startpunkt", menu.Marked?.Row.Name);
        menu.MovePrevious();
        Assert.Equal("Ärende", menu.Marked?.Row.Name);
        menu.MoveFirst();
        Assert.Equal("Startpunkt", menu.Marked?.Row.Name);
        menu.MoveLast();
        Assert.Equal("Ärende", menu.Marked?.Row.Name);
        Assert.Single(menu.Rows, r => r.IsMarked);
    }

    [Fact]
    public void Enter_chooses_the_marked_row_and_closes_with_the_keyboard_back_on_the_button()
    {
        var menu = Menu(hasCase: true);
        var (chosen, closed) = Listen(menu);
        menu.Open();
        menu.MoveLast();

        menu.ChooseMarked();

        Assert.False(menu.IsOpen);
        Assert.Equal([SubmenuAction.NewCase], chosen);
        Assert.Equal([true], closed);
        Assert.Null(menu.Marked);
    }

    [Fact]
    public void A_letter_chooses_its_row_directly()
    {
        var menu = Menu(hasCase: true);
        var (chosen, _) = Listen(menu);
        menu.Open();

        Assert.True(menu.ChooseLetter('i'));

        Assert.Equal([SubmenuAction.ImportSanctionList], chosen);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void The_letter_of_a_toned_row_is_swallowed_and_the_menu_stays_open()
    {
        var menu = Menu(hasCase: false);
        var (chosen, _) = Listen(menu);
        menu.Open();

        Assert.True(menu.ChooseLetter('B'));

        Assert.Empty(chosen);
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void A_letter_no_row_has_is_left_alone()
    {
        var menu = Menu(hasCase: true);
        menu.Open();

        Assert.False(menu.ChooseLetter('X'));
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void A_toned_row_cannot_be_clicked_or_pointed_at()
    {
        var menu = Menu(hasCase: false);
        var (chosen, _) = Listen(menu);
        menu.Open();
        var watch = menu.Rows.Single(r => r.Row.Action == SubmenuAction.Watch);

        menu.Point(watch);
        menu.Choose(watch);

        Assert.Empty(chosen);
        Assert.False(watch.IsMarked);
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void Nothing_is_chosen_while_the_menu_is_closed()
    {
        var menu = Menu(hasCase: true);
        var (chosen, _) = Listen(menu);

        Assert.True(menu.ChooseLetter('S'));
        menu.ChooseMarked();

        Assert.Empty(chosen);
    }

    [Fact]
    public void Escape_closes_with_the_keyboard_back_and_dismissing_leaves_it_where_it_went()
    {
        var menu = Menu(hasCase: true);
        var (chosen, closed) = Listen(menu);

        menu.Open();
        menu.Close();
        menu.Open();
        menu.Dismiss();
        menu.Dismiss();

        Assert.Equal([true, false], closed);
        Assert.Empty(chosen);
    }

    [Fact]
    public void Pressing_the_button_again_folds_the_menu_in()
    {
        var menu = Menu(hasCase: true);
        var (_, closed) = Listen(menu);

        menu.Toggle();
        menu.Toggle();

        Assert.False(menu.IsOpen);
        Assert.Equal([true], closed);
    }

    [Fact]
    public void Closing_the_case_while_open_moves_the_mark_off_a_row_that_is_toned()
    {
        var menu = new SubmenuViewModel(
        [
            new(SubmenuAction.Label, "Etikett", "E", SubmenuNeed.OpenCase),
            new(SubmenuAction.NewCase, "Ärende", "Ctrl+N", SubmenuNeed.Nothing),
        ]);
        menu.Update(hasCase: true);
        menu.Open();
        Assert.Equal("Etikett", menu.Marked?.Row.Name);

        menu.Update(hasCase: false);

        Assert.Equal("Ärende", menu.Marked?.Row.Name);
    }

    [Fact]
    public void The_height_and_outline_come_from_the_rows()
    {
        var menu = Menu(hasCase: true);

        Assert.Equal(SubmenuShape.MenuHeight(SubmenuTable.New), menu.Height);
        Assert.Equal(SubmenuShape.Outline(menu.Height), menu.Outline);
    }

    private static SubmenuViewModel Menu(bool hasCase)
    {
        var menu = new SubmenuViewModel(SubmenuTable.New);
        menu.Update(hasCase);
        return menu;
    }

    private static List<string> Enabled(SubmenuViewModel menu) =>
        menu.Rows.Where(r => r.IsEnabled).Select(r => r.Row.Name).ToList();

    private static (List<SubmenuAction> Chosen, List<bool> Closed) Listen(SubmenuViewModel menu)
    {
        var chosen = new List<SubmenuAction>();
        var closed = new List<bool>();
        menu.Chosen += (_, action) => chosen.Add(action);
        menu.Closed += (_, back) => closed.Add(back);
        return (chosen, closed);
    }
}

public sealed class ShellSubmenuTests
{
    [Fact]
    public void Pressing_Nytt_folds_out_its_menu_instead_of_opening_the_panel()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var nytt = rig.Shell.Top.Single();

        rig.Shell.PressCommand.Execute(nytt);

        Assert.Same(nytt, rig.Shell.OpenSubmenu);
        Assert.False(rig.NewCase.IsOpen);
    }

    [Fact]
    public void Pressing_a_view_without_a_menu_shows_the_view()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Session.Open(new TestCase("2026-0117"));

        rig.Shell.PressCommand.Execute(rig.Shell.Work.Single(i => i.Entry.View == AppView.Timeline));

        Assert.Equal(AppView.Timeline, rig.Shell.Current);
        Assert.Null(rig.Shell.OpenSubmenu);
    }

    [Fact]
    public void The_shortcut_Ctrl_N_opens_the_panel_directly_and_closes_the_menu()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var nytt = rig.Shell.Top.Single();
        rig.Shell.PressCommand.Execute(nytt);
        var closed = new List<bool>();
        nytt.Submenu!.Closed += (_, back) => closed.Add(back);

        rig.Shell.ShowCommand.Execute(AppView.New);

        Assert.True(rig.NewCase.IsOpen);
        Assert.Null(rig.Shell.OpenSubmenu);
        Assert.Equal([false], closed);
    }

    [Fact]
    public void Showing_another_view_closes_the_menu()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Shell.PressCommand.Execute(rig.Shell.Top.Single());

        rig.Shell.ShowCommand.Execute(AppView.Flow);

        Assert.Null(rig.Shell.OpenSubmenu);
    }

    [Fact]
    public void The_menu_follows_whether_a_case_is_open()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var menu = rig.Shell.Top.Single().Submenu!;
        var startPoint = menu.Rows.Single(r => r.Row.Action == SubmenuAction.StartPoint);
        Assert.True(startPoint.IsEnabled);
        Assert.False(menu.Rows.Single(r => r.Row.Action == SubmenuAction.Label).IsEnabled);

        rig.Session.Open(new TestCase("2026-0117"));

        Assert.True(startPoint.IsEnabled);
        Assert.True(menu.Rows.Single(r => r.Row.Action == SubmenuAction.Label).IsEnabled);
    }

    [Fact]
    public void Etikett_shows_the_graph_and_asks_for_the_label_form()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Session.Open(new TestCase("2026-0117"));
        rig.Shell.ShowCommand.Execute(AppView.Monitoring);
        var wanted = 0;
        rig.Shell.LabelWanted += (_, _) => wanted++;

        Choose(rig, 'E');

        Assert.Equal(AppView.Graph, rig.Shell.Current);
        Assert.Equal(1, wanted);
    }

    [Fact]
    public void Startpunkt_without_a_case_opens_Nytt_arende()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var wanted = 0;
        rig.Shell.CommandBarWanted += (_, _) => wanted++;

        Choose(rig, 'S');

        Assert.True(rig.NewCase.IsOpen);
        Assert.Equal(0, wanted);
    }

    [Fact]
    public void Startpunkt_with_a_case_puts_the_keyboard_in_the_command_bar_and_says_what_to_write()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Session.Open(new TestCase("2026-0117"));
        var wanted = 0;
        rig.Shell.CommandBarWanted += (_, _) => wanted++;

        Choose(rig, 'S');

        Assert.Equal(1, wanted);
        Assert.False(rig.NewCase.IsOpen);
        Assert.Contains("startpunkt i 2026-0117", rig.Shell.CommandAnswer, StringComparison.Ordinal);
        Assert.False(rig.Shell.IsAnswerError);
    }

    [Fact]
    public void Startpunkt_never_replaces_what_is_written_in_the_bar()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Session.Open(new TestCase("2026-0117"));
        rig.Shell.CommandText = "bc1q";

        Choose(rig, 'S');

        Assert.Equal("bc1q", rig.Shell.CommandText);
    }

    [Fact]
    public void Arende_opens_Nytt_arende()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var menu = rig.Shell.Top.Single().Submenu!;
        rig.Shell.PressCommand.Execute(rig.Shell.Top.Single());
        menu.MoveLast();

        menu.ChooseMarked();

        Assert.True(rig.NewCase.IsOpen);
    }

    [Fact]
    public async Task Importing_a_sanction_list_reads_the_chosen_file_through_Attribution_and_says_what_it_holds()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "<sdnList />");
            rig.Picker.Path = file;

            var reply = await rig.Sanctions.ImportAsync();

            Assert.Equal("<sdnList />", System.Text.Encoding.UTF8.GetString(rig.SanctionLists.Imported.Single()));
            Assert.False(reply!.IsError);
            Assert.Equal("OFAC SDN publicerad 2026-11-01 är importerad: 5 adresser, 3 för Bitcoin.", reply.Text);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task A_cancelled_dialog_imports_nothing_and_says_nothing()
    {
        var rig = new GateRig(VaultState.Unlocked);

        Assert.Null(await rig.Sanctions.ImportAsync());
        Assert.Equal(1, rig.Picker.Asked);
        Assert.Empty(rig.SanctionLists.Imported);
    }

    [Fact]
    public async Task A_file_that_is_not_a_list_is_said_in_Swedish()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var file = Path.GetTempFileName();
        try
        {
            rig.Picker.Path = file;
            rig.SanctionLists.Failure = new SanctionListException(SanctionListError.NotSdnList);

            var reply = await rig.Sanctions.ImportAsync();

            Assert.True(reply!.IsError);
            Assert.StartsWith("Filen är ingen sanktionslista som Chainsaw läser", reply.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_said_without_its_folder()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Picker.Path = Path.Combine(Path.GetTempPath(), "saknas-" + Guid.NewGuid() + ".xml");

        var reply = await rig.Sanctions.ImportAsync();

        Assert.True(reply!.IsError);
        Assert.StartsWith("Filen gick inte att läsa: saknas-", reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Skipped_addresses_are_said()
    {
        var list = new SanctionListInfo(
            "OFAC SDN", "ab", new DateOnly(2026, 1, 2), DateTimeOffset.UnixEpoch, new Dictionary<string, int>(), 2);

        Assert.Equal(
            "OFAC SDN publicerad 2026-01-02 är importerad: 0 adresser, 0 för Bitcoin. "
                + "2 ogiltiga adresser hoppades över.",
            SanctionListImport.Describe(list));
    }

    [Fact]
    public void Every_import_failure_has_a_Swedish_text()
    {
        Assert.All(
            Enum.GetValues<SanctionListError>(),
            error => Assert.NotEqual("Sanktionslistan gick inte att läsa.", SanctionListImport.Explain(error)));
    }

    [Fact]
    public async Task The_import_from_the_menu_shows_its_answer_above_the_bar()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var file = Path.GetTempFileName();
        try
        {
            rig.Picker.Path = file;

            Choose(rig, 'I');
            await Until(() => rig.Shell.CommandAnswer.Length > 0);

            Assert.StartsWith("OFAC SDN publicerad", rig.Shell.CommandAnswer, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Pressing_Nytt_behind_the_open_panel_opens_no_hidden_menu()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Shell.ShowCommand.Execute(AppView.New);

        rig.Shell.PressCommand.Execute(rig.Shell.Top.Single());

        Assert.Null(rig.Shell.OpenSubmenu);
    }

    [Fact]
    public void Opening_the_panel_any_way_closes_the_menu()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Shell.PressCommand.Execute(rig.Shell.Top.Single());

        rig.NewCase.Show();

        Assert.Null(rig.Shell.OpenSubmenu);
    }

    [Fact]
    public async Task The_same_list_again_says_that_Chainsaw_already_had_it()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var file = Path.GetTempFileName();
        try
        {
            rig.Picker.Path = file;
            await rig.Sanctions.ImportAsync();

            var reply = await rig.Sanctions.ImportAsync();

            Assert.StartsWith("OFAC SDN publicerad 2026-11-01 fanns redan:", reply!.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task A_failure_Attribution_did_not_name_is_still_said()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var file = Path.GetTempFileName();
        try
        {
            rig.Picker.Path = file;
            rig.SanctionLists.Failure = new InvalidOperationException("x");

            var reply = await rig.Sanctions.ImportAsync();

            Assert.True(reply!.IsError);
            Assert.Equal(SanctionListImport.FailedText, reply.Text);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void Choose(GateRig rig, char letter)
    {
        rig.Shell.PressCommand.Execute(rig.Shell.Top.Single());
        Assert.True(rig.Shell.Top.Single().Submenu!.ChooseLetter(letter));
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}

public sealed class AutoLockHoldTests
{
    [Fact]
    public void A_dialog_holds_the_automatic_lock_and_the_time_starts_again_after_it()
    {
        var clock = new TestClock();
        var autoLock = new AutoLock(clock, TimeSpan.FromMinutes(15));
        var hold = autoLock.Hold();

        clock.UtcNow += TimeSpan.FromMinutes(30);
        Assert.False(autoLock.IsDue());

        hold.Dispose();
        hold.Dispose();
        Assert.False(autoLock.IsDue());

        clock.UtcNow += TimeSpan.FromMinutes(15);
        Assert.True(autoLock.IsDue());
    }
}
