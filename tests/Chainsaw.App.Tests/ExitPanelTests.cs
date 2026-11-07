using Chainsaw.App.Exits;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

public sealed class ExitPanelTests
{
    private static readonly ExitInfo Stockholm = new(Guid.NewGuid(), "Stockholm", string.Empty, ExitKind.WireGuard);

    private static readonly ExitInfo Amsterdam = new(Guid.NewGuid(), "Amsterdam", string.Empty, ExitKind.WireGuard);

    [Fact]
    public async Task Opening_lists_the_exits_with_the_chosen_one_and_their_state()
    {
        var rig = new Rig();
        rig.Store.Selected = Amsterdam.Id;
        rig.Connection.Set(new ExitStatus(ExitState.Failed, Amsterdam.Id, ExitFailure.NoAnswer));

        await rig.OpenAsync();

        Assert.True(rig.Panel.IsOpen);
        Assert.Equal(["Amsterdam", "Stockholm"], rig.Panel.Rows.Select(row => row.Name));
        var amsterdam = rig.Row("Amsterdam");
        Assert.True(amsterdam.IsChosen);
        Assert.Equal(ExitRowState.Failed, amsterdam.State);
        Assert.Equal("Servern svarar inte", amsterdam.Word);
        Assert.Equal(ExitRowState.Off, rig.Row("Stockholm").State);
        Assert.False(rig.Row("Stockholm").HasWord);
    }

    [Fact]
    public async Task The_route_is_green_with_the_exit_while_it_is_connected_and_red_without()
    {
        var rig = new Rig();
        await rig.OpenAsync();

        Assert.False(rig.Panel.IsLive);
        Assert.Equal(ExitPanelViewModel.NoRouteText, rig.Panel.RouteText);

        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));

        Assert.True(rig.Panel.IsLive);
        Assert.Equal("Stockholm", rig.Panel.RouteText);
        Assert.Equal(ExitRowViewModel.ConnectedText, rig.Row("Stockholm").Word);

        rig.Connection.Set(new ExitStatus(ExitState.Connecting, Amsterdam.Id, null));

        Assert.False(rig.Panel.IsLive);
        Assert.Equal(ExitPanelViewModel.NoRouteText, rig.Panel.RouteText);
        Assert.Equal(ExitRowState.Connecting, rig.Row("Amsterdam").State);
        Assert.Equal(ExitRowState.Off, rig.Row("Stockholm").State);
    }

    [Fact]
    public async Task The_power_button_chooses_and_connects_an_exit()
    {
        var rig = new Rig();
        await rig.OpenAsync();
        var row = rig.Row("Stockholm");

        await rig.Panel.PowerAsync(row);

        Assert.Equal(Stockholm.Id, rig.Store.Selected);
        Assert.Equal(1, rig.Connection.Connects);
        Assert.True(row.IsChosen);
        Assert.False(rig.Row("Amsterdam").IsChosen);
        Assert.Equal(ExitRowState.Connected, row.State);
        Assert.Equal(ExitRowViewModel.DisconnectText, row.ActionText);
    }

    [Fact]
    public async Task The_power_button_of_a_connected_exit_disconnects_it()
    {
        var rig = new Rig();
        await rig.OpenAsync();
        var row = rig.Row("Stockholm");
        await rig.Panel.PowerAsync(row);

        await rig.Panel.PowerAsync(row);

        Assert.Equal(1, rig.Connection.Disconnects);
        Assert.Equal(ExitRowState.Off, row.State);
        Assert.False(rig.Panel.IsLive);
    }

    [Fact]
    public async Task A_connect_replaced_while_it_chooses_its_exit_never_connects()
    {
        var rig = new Rig();
        await rig.OpenAsync();
        var choosing = new TaskCompletionSource();
        rig.Store.SelectGate = choosing.Task;

        var connect = rig.Panel.PowerAsync(rig.Row("Stockholm"));
        rig.Connection.Set(new ExitStatus(ExitState.Connecting, Stockholm.Id, null));
        var disconnect = rig.Panel.PowerAsync(rig.Row("Stockholm"));
        choosing.SetResult();
        await Task.WhenAll(connect, disconnect);

        Assert.Equal(0, rig.Connection.Connects);
        Assert.Equal(1, rig.Connection.Disconnects);
    }

    [Fact]
    public async Task A_failure_to_connect_is_shown_on_the_row_not_thrown()
    {
        var rig = new Rig();
        rig.Connection.Outcome = id => new ExitStatus(ExitState.Failed, id, ExitFailure.TunnelUnavailable);
        await rig.OpenAsync();

        await rig.Panel.PowerAsync(rig.Row("Stockholm"));

        Assert.Equal(ExitRowState.Failed, rig.Row("Stockholm").State);
        Assert.Equal("Tunneln finns inte", rig.Row("Stockholm").Word);
        Assert.False(rig.Panel.HasError);
    }

    [Fact]
    public async Task A_locked_vault_is_said_in_plain_words()
    {
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Store.Failure = new VaultException(VaultError.Locked);

        await rig.Panel.PowerAsync(rig.Row("Stockholm"));

        Assert.Equal(ExitPanelViewModel.LockedText, rig.Panel.ErrorText);
        Assert.Equal(0, rig.Connection.Connects);
    }

    [Fact]
    public async Task Exits_that_cannot_be_read_are_said()
    {
        var rig = new Rig();
        rig.Store.Failure = new VaultException(VaultError.Locked);

        await rig.OpenAsync();

        Assert.Equal(ExitPanelViewModel.LockedText, rig.Panel.ErrorText);
        Assert.Empty(rig.Panel.Rows);
    }

    [Fact]
    public async Task An_empty_vault_says_so()
    {
        var rig = new Rig();
        rig.Store.Known.Clear();

        await rig.OpenAsync();

        Assert.True(rig.Panel.IsEmpty);
    }

    [Fact]
    public async Task A_valid_file_shows_its_name_and_passes_the_check()
    {
        using var file = new TempFile("se-got-02.conf", WireGuardCheckTests.Valid);
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Picker.WireGuardPath = file.Path;

        await rig.Panel.AddFileAsync();

        Assert.True(rig.Panel.IsAdding);
        Assert.False(rig.Panel.IsListing);
        Assert.Equal("se-got-02.conf", rig.Panel.FileName);
        Assert.Equal("se-got-02", rig.Panel.NewName);
        Assert.Equal(WireGuardCheck.ValidText, rig.Panel.CheckText);
        Assert.True(rig.Panel.CanSave);
    }

    [Fact]
    public async Task A_file_that_fails_the_check_says_what_is_missing_and_cannot_be_saved()
    {
        using var file = new TempFile("trasig.conf", WireGuardCheckTests.Valid.Replace("Endpoint", "# Endpoint"));
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Picker.WireGuardPath = file.Path;

        await rig.Panel.AddFileAsync();

        Assert.Equal("Saknar Endpoint", rig.Panel.CheckText);
        Assert.False(rig.Panel.CanSave);
    }

    [Fact]
    public async Task A_cancelled_dialog_changes_nothing()
    {
        var rig = new Rig();
        await rig.OpenAsync();

        await rig.Panel.AddFileAsync();

        Assert.Equal(1, rig.Picker.AskedForWireGuard);
        Assert.False(rig.Panel.IsAdding);
    }

    [Fact]
    public async Task Saving_stores_the_file_under_its_name_and_lists_it_without_choosing_it()
    {
        using var file = new TempFile("se-got-02.conf", WireGuardCheckTests.Valid);
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Picker.WireGuardPath = file.Path;
        await rig.Panel.AddFileAsync();
        rig.Panel.NewName = "  Göteborg ";

        await rig.Panel.SaveAsync();

        var (name, configuration) = Assert.Single(rig.Store.Added);
        Assert.Equal("Göteborg", name);
        Assert.Equal(WireGuardCheckTests.Valid, configuration);
        Assert.False(rig.Panel.IsAdding);
        Assert.Contains(rig.Panel.Rows, row => row.Name == "Göteborg" && !row.IsChosen);
        Assert.Null(rig.Store.Selected);
    }

    [Fact]
    public async Task A_file_changed_after_the_check_is_checked_again_and_not_stored()
    {
        using var file = new TempFile("se-got-02.conf", WireGuardCheckTests.Valid);
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Picker.WireGuardPath = file.Path;
        await rig.Panel.AddFileAsync();
        File.WriteAllText(file.Path, "[Interface]\n");

        await rig.Panel.SaveAsync();

        Assert.Empty(rig.Store.Added);
        Assert.True(rig.Panel.IsAdding);
        Assert.False(rig.Panel.IsFileValid);
        Assert.Equal("Ska ha en [Peer]", rig.Panel.CheckText);
    }

    [Fact]
    public async Task A_file_the_store_refuses_is_said()
    {
        using var file = new TempFile("se-got-02.conf", WireGuardCheckTests.Valid);
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Picker.WireGuardPath = file.Path;
        await rig.Panel.AddFileAsync();
        rig.Store.AddFailure = new ArgumentException("refused", "configuration");

        await rig.Panel.SaveAsync();

        Assert.Equal(ExitPanelViewModel.RefusedText, rig.Panel.ErrorText);
        Assert.True(rig.Panel.IsAdding);
    }

    [Fact]
    public async Task A_blank_name_cannot_be_saved()
    {
        using var file = new TempFile("se-got-02.conf", WireGuardCheckTests.Valid);
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Picker.WireGuardPath = file.Path;
        await rig.Panel.AddFileAsync();

        rig.Panel.NewName = "  ";

        Assert.False(rig.Panel.CanSave);
    }

    [Fact]
    public async Task Closing_the_panel_forgets_the_file()
    {
        using var file = new TempFile("se-got-02.conf", WireGuardCheckTests.Valid);
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Picker.WireGuardPath = file.Path;
        await rig.Panel.AddFileAsync();

        rig.Panel.Close();

        Assert.False(rig.Panel.IsOpen);
        Assert.False(rig.Panel.IsAdding);
        Assert.Empty(rig.Panel.FileName);
        Assert.False(rig.Panel.CanSave);
    }

    [Fact]
    public async Task The_shell_closes_the_pills_menus_when_the_panel_opens_and_the_panel_when_a_view_is_shown()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var menu = rig.Shell.Top.First(item => item.Submenu is not null);
        rig.Shell.Press(menu);
        Assert.True(menu.Submenu!.IsOpen);

        rig.Shell.ExitPanel.Toggle();
        await rig.Shell.ExitPanel.RefreshAsync();

        Assert.False(menu.Submenu.IsOpen);
        Assert.True(rig.Shell.ExitPanel.IsOpen);

        rig.Shell.Show(AppView.Labels);

        Assert.False(rig.Shell.ExitPanel.IsOpen);
    }

    [Fact]
    public void Leaving_the_window_keeps_the_panel_but_a_lock_closes_it()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Shell.ExitPanel.Toggle();

        rig.Shell.DismissSubmenus();
        Assert.True(rig.Shell.ExitPanel.IsOpen);

        rig.Shell.DismissAll();
        Assert.False(rig.Shell.ExitPanel.IsOpen);
    }

    /// <summary>The panel with test doubles that remember what was asked.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Store.Known.AddRange([Amsterdam, Stockholm]);
            Connection.Store = Store;
            Panel = new ExitPanelViewModel(Connection, Store, Picker, new InlineUi());
        }

        public PanelStore Store { get; } = new();

        public PanelConnection Connection { get; } = new();

        public TestFilePicker Picker { get; } = new();

        public ExitPanelViewModel Panel { get; }

        public ExitRowViewModel Row(string name) => Panel.Rows.Single(row => row.Name == name);

        public Task OpenAsync()
        {
            Panel.Toggle();
            return Panel.RefreshAsync();
        }
    }

    /// <summary>Exits in a list, the selected one, and failures the test sets.</summary>
    private sealed class PanelStore : IExitStore
    {
        public List<ExitInfo> Known { get; } = [];

        public List<(string Name, string Configuration)> Added { get; } = [];

        public Guid? Selected { get; set; }

        public Exception? Failure { get; set; }

        public Exception? AddFailure { get; set; }

        public Task SelectGate { get; set; } = Task.CompletedTask;

        public IReadOnlyList<ExitInfo> List() =>
            Failure is null ? Known.OrderBy(exit => exit.Name).ToList() : throw Failure;

        public ExitInfo Add(string name, string place, ExitKind kind, string configuration)
        {
            if (AddFailure is not null)
            {
                throw AddFailure;
            }

            Added.Add((name, configuration));
            var exit = new ExitInfo(Guid.NewGuid(), name, place, kind);
            Known.Add(exit);
            return exit;
        }

        public Task<bool> RemoveAsync(Guid id) => Task.FromResult(Known.RemoveAll(exit => exit.Id == id) > 0);

        public ExitInfo? FindSelected() => Known.FirstOrDefault(exit => exit.Id == Selected);

        public async Task SelectAsync(Guid? id)
        {
            await SelectGate;
            if (Failure is not null)
            {
                throw Failure;
            }

            Selected = id;
        }
    }

    /// <summary>A connection that connects the selected exit as the test says, and counts what it was asked.</summary>
    private sealed class PanelConnection : IExitConnection
    {
        public ExitStatus Status { get; private set; } = ExitStatus.NotConnected;

        public PanelStore? Store { get; set; }

        public Func<Guid, ExitStatus> Outcome { get; set; } = id => new ExitStatus(ExitState.Connected, id, null);

        public int Connects { get; private set; }

        public int Disconnects { get; private set; }

        public event EventHandler<ExitStatus>? StatusChanged;

        public Task<ExitStatus> ConnectAsync(CancellationToken cancellationToken = default)
        {
            Connects++;
            var id = Store?.Selected ?? throw new InvalidOperationException("No exit is selected.");
            Set(Outcome(id));
            return Task.FromResult(Status);
        }

        public Task DisconnectAsync()
        {
            Disconnects++;
            Set(ExitStatus.NotConnected);
            return Task.CompletedTask;
        }

        public void Set(ExitStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(this, status);
        }
    }

    /// <summary>A file in a folder of its own, deleted afterwards.</summary>
    private sealed class TempFile : IDisposable
    {
        private readonly string _folder = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "chainsaw-tests-" + Guid.NewGuid().ToString("N"));

        public TempFile(string name, string text)
        {
            Directory.CreateDirectory(_folder);
            Path = System.IO.Path.Combine(_folder, name);
            File.WriteAllText(Path, text);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(_folder, recursive: true);
    }
}
