using Chainsaw.App.Links;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// Whether others can change the folder Chainsaw runs from (decision 16), judged from access lists as Windows sets them
/// on a fresh installation, and what the settings then say and refuse.
/// </summary>
public sealed class ProgramFolderTests
{
    private const string User = "S-1-5-21-1000-2000-3000-1001";
    private const string OtherUser = "S-1-5-21-1000-2000-3000-1002";
    private const string AuthenticatedUsers = "S-1-5-11";
    private const string Users = "S-1-5-32-545";
    private const string AllPackages = "S-1-15-2-1";

    private const int FullControl = 0x1F01FF;
    private const int Modify = 0x1301BF;
    private const int ReadAndExecute = 0x1200A9;
    private const int CreateFolders = 0x4;
    private const int GenericAll = 0x10000000;
    private const int GenericReadExecute = unchecked((int)0xA0000000);

    /// <summary><c>C:\</c>: Authenticated Users may create folders in it and modify what is under them.</summary>
    private static readonly FolderAcl Root = new(@"C:\", ProgramFolder.TrustedInstaller,
    [
        new(ProgramFolder.Administrators, FullControl, ContainerInherit: true, ObjectInherit: true),
        new(ProgramFolder.LocalSystem, FullControl, ContainerInherit: true, ObjectInherit: true),
        new(Users, ReadAndExecute, ContainerInherit: true, ObjectInherit: true),
        new(AuthenticatedUsers, Modify, ContainerInherit: true, ObjectInherit: true, InheritOnly: true),
        new(AuthenticatedUsers, CreateFolders),
    ]);

    /// <summary>The user's profile and the folders above <c>%LOCALAPPDATA%\Programs</c>.</summary>
    private static FolderAcl Profile(string path) => new(path, ProgramFolder.LocalSystem,
    [
        new(ProgramFolder.LocalSystem, FullControl, ContainerInherit: true, ObjectInherit: true),
        new(ProgramFolder.Administrators, FullControl, ContainerInherit: true, ObjectInherit: true),
        new(User, FullControl, ContainerInherit: true, ObjectInherit: true),
    ]);

    /// <summary><c>C:\Users</c>: everyone may read.</summary>
    private static readonly FolderAcl UsersFolder = new(@"C:\Users", ProgramFolder.LocalSystem,
    [
        new(ProgramFolder.LocalSystem, FullControl, ContainerInherit: true, ObjectInherit: true),
        new(ProgramFolder.Administrators, FullControl, ContainerInherit: true, ObjectInherit: true),
        new(Users, ReadAndExecute, ContainerInherit: true, ObjectInherit: true),
        new(ProgramFolder.Everyone, ReadAndExecute, ContainerInherit: true, ObjectInherit: true),
    ]);

    /// <summary><c>C:\Program Files</c> as Windows sets it.</summary>
    private static FolderAcl ProgramFiles(string path) => new(path, ProgramFolder.TrustedInstaller,
    [
        new(ProgramFolder.TrustedInstaller, FullControl),
        new(ProgramFolder.TrustedInstaller, GenericAll, ContainerInherit: true, ObjectInherit: true, InheritOnly: true),
        new(ProgramFolder.LocalSystem, Modify),
        new(ProgramFolder.LocalSystem, GenericAll, ContainerInherit: true, ObjectInherit: true, InheritOnly: true),
        new(ProgramFolder.Administrators, Modify),
        new(ProgramFolder.Administrators, GenericAll, ContainerInherit: true, ObjectInherit: true, InheritOnly: true),
        new(Users, ReadAndExecute),
        new(Users, GenericReadExecute, ContainerInherit: true, ObjectInherit: true, InheritOnly: true),
        new(ProgramFolder.CreatorOwner, GenericAll, ContainerInherit: true, ObjectInherit: true, InheritOnly: true),
        new(AllPackages, ReadAndExecute, ContainerInherit: true, ObjectInherit: true),
    ]);

    [Fact]
    public void A_folder_under_c_that_inherits_authenticated_users_modify_is_unsafe()
    {
        // C:\Chainsaw, unzipped by the user, inherits C:\'s entries without "inherit only".
        var chainsaw = new FolderAcl(@"C:\Chainsaw", User,
        [
            new(ProgramFolder.Administrators, FullControl, ContainerInherit: true, ObjectInherit: true),
            new(ProgramFolder.LocalSystem, FullControl, ContainerInherit: true, ObjectInherit: true),
            new(Users, ReadAndExecute, ContainerInherit: true, ObjectInherit: true),
            new(AuthenticatedUsers, Modify, ContainerInherit: true, ObjectInherit: true),
            new(ProgramFolder.CreatorOwner, GenericAll, ContainerInherit: true, ObjectInherit: true, InheritOnly: true),
        ]);

        var verdict = ProgramFolder.Judge([chainsaw, Root], User);

        Assert.False(verdict.IsSafe);
        Assert.Equal(ProgramFolderProblem.OpenToOthers, verdict.Problem);
        Assert.Equal(@"C:\Chainsaw", verdict.Path);
        Assert.Equal(AuthenticatedUsers, verdict.Sid);
        Assert.NotEqual(0, verdict.Rights & ProgramFolder.WriteData);
    }

    [Fact]
    public void Local_app_data_programs_is_safe()
    {
        var folders = new[]
        {
            Profile(@"C:\Users\anna\AppData\Local\Programs\Chainsaw") with { Owner = User },
            Profile(@"C:\Users\anna\AppData\Local\Programs") with { Owner = User },
            Profile(@"C:\Users\anna\AppData\Local"),
            Profile(@"C:\Users\anna\AppData"),
            Profile(@"C:\Users\anna"),
            UsersFolder,
            Root,
        };

        Assert.Equal(ProgramFolderVerdict.Safe, ProgramFolder.Judge(folders, User));
    }

    [Fact]
    public void Program_files_is_safe()
    {
        var chainsaw = ProgramFiles(@"C:\Program Files\Chainsaw") with { Owner = ProgramFolder.Administrators };

        var verdict = ProgramFolder.Judge([chainsaw, ProgramFiles(@"C:\Program Files"), Root], User);

        Assert.True(verdict.IsSafe);
    }

    [Fact]
    public void A_parent_that_lets_others_delete_its_children_is_unsafe_since_the_folder_can_be_moved_away()
    {
        var apps = new FolderAcl(@"D:\Apps", ProgramFolder.Administrators,
        [
            new(ProgramFolder.Administrators, FullControl, ContainerInherit: true, ObjectInherit: true),
            new(Users, ProgramFolder.DeleteChild),
        ]);
        var chainsaw = Profile(@"D:\Apps\Chainsaw") with { Owner = User };

        var verdict = ProgramFolder.Judge([chainsaw, apps], User);

        Assert.Equal(
            (ProgramFolderProblem.OpenToOthers, @"D:\Apps", Users), (verdict.Problem, verdict.Path, verdict.Sid));
    }

    [Fact]
    public void A_parent_where_others_may_only_create_folders_is_safe()
    {
        var chainsaw = Profile(@"D:\Chainsaw") with { Owner = User };
        var drive = new FolderAcl(@"D:\", ProgramFolder.LocalSystem,
        [
            new(ProgramFolder.Administrators, FullControl),
            new(AuthenticatedUsers, CreateFolders),
            new(AuthenticatedUsers, Modify, ContainerInherit: true, ObjectInherit: true, InheritOnly: true),
        ]);

        // The folder's own list was set without inheritance, so the parent's inherit-only entry does not reach it.
        Assert.True(ProgramFolder.Judge([chainsaw, drive], User).IsSafe);
    }

    [Fact]
    public void An_inherit_only_entry_in_the_program_folder_counts_for_its_files()
    {
        var chainsaw = Profile(@"D:\Chainsaw") with
        {
            Owner = User,
            Aces = [.. Profile("x").Aces, new(Users, Modify, ObjectInherit: true, InheritOnly: true)],
        };

        var verdict = ProgramFolder.Judge([chainsaw], User);

        Assert.Equal((ProgramFolderProblem.OpenToOthers, Users), (verdict.Problem, verdict.Sid));
    }

    [Fact]
    public void An_inherit_only_entry_that_reaches_nothing_and_creator_owner_are_ignored()
    {
        var chainsaw = Profile(@"D:\Chainsaw") with
        {
            Owner = User,
            Aces =
            [
                .. Profile("x").Aces,
                new(Users, Modify, InheritOnly: true),
                new(ProgramFolder.CreatorOwner, GenericAll, ContainerInherit: true, ObjectInherit: true,
                    InheritOnly: true),
            ],
        };

        Assert.True(ProgramFolder.Judge([chainsaw], User).IsSafe);
    }

    [Fact]
    public void Creator_owner_on_the_folder_itself_is_not_trusted()
    {
        var chainsaw = Profile(@"D:\Chainsaw") with
        {
            Owner = User,
            Aces = [.. Profile("x").Aces, new(ProgramFolder.CreatorOwner, FullControl)],
        };

        Assert.Equal(ProgramFolder.CreatorOwner, ProgramFolder.Judge([chainsaw], User).Sid);
    }

    [Fact]
    public void A_deny_before_the_grant_takes_the_write_rights_away()
    {
        var chainsaw = Profile(@"D:\Chainsaw") with
        {
            Owner = User,
            Aces =
            [
                new(Users, Modify & ~ReadAndExecute, IsDeny: true, ContainerInherit: true, ObjectInherit: true),
                .. Profile("x").Aces,
                new(Users, Modify, ContainerInherit: true, ObjectInherit: true),
            ],
        };

        Assert.True(ProgramFolder.Judge([chainsaw], User).IsSafe);
    }

    [Fact]
    public void A_deny_for_everyone_counts_for_every_user()
    {
        var chainsaw = Profile(@"D:\Chainsaw") with
        {
            Owner = User,
            Aces =
            [
                new(ProgramFolder.Everyone, FullControl & ~ReadAndExecute, IsDeny: true, ContainerInherit: true,
                    ObjectInherit: true),
                new(OtherUser, FullControl, ContainerInherit: true, ObjectInherit: true),
            ],
        };

        Assert.True(ProgramFolder.Judge([chainsaw], User).IsSafe);
    }

    [Fact]
    public void A_deny_after_the_grant_or_for_another_sid_takes_nothing_away()
    {
        var after = Profile(@"D:\Chainsaw") with
        {
            Owner = User,
            Aces =
            [
                new(OtherUser, Modify, ContainerInherit: true, ObjectInherit: true),
                new(OtherUser, Modify, IsDeny: true, ContainerInherit: true, ObjectInherit: true),
            ],
        };
        var another = after with
        {
            Aces =
            [
                new(Users, Modify, IsDeny: true, ContainerInherit: true, ObjectInherit: true),
                new(OtherUser, Modify, ContainerInherit: true, ObjectInherit: true),
            ],
        };

        Assert.Equal(OtherUser, ProgramFolder.Judge([after], User).Sid);
        Assert.Equal(OtherUser, ProgramFolder.Judge([another], User).Sid);
    }

    [Fact]
    public void Generic_write_and_read_rights_are_spelt_out()
    {
        var chainsaw = Profile(@"D:\Chainsaw") with
        {
            Owner = User,
            Aces = [.. Profile("x").Aces, new(Users, GenericReadExecute, ContainerInherit: true, ObjectInherit: true)],
        };
        var written = chainsaw with
        {
            Aces = [.. Profile("x").Aces, new(Users, 0x40000000, ObjectInherit: true, InheritOnly: true)],
        };

        Assert.True(ProgramFolder.Judge([chainsaw], User).IsSafe);
        Assert.Equal(Users, ProgramFolder.Judge([written], User).Sid);
    }

    [Fact]
    public void A_folder_owned_by_another_user_is_unsafe()
    {
        var chainsaw = Profile(@"D:\Chainsaw") with { Owner = OtherUser };

        var verdict = ProgramFolder.Judge([chainsaw], User);

        Assert.Equal((ProgramFolderProblem.OwnedByOther, OtherUser), (verdict.Problem, verdict.Sid));
    }

    [Fact]
    public void An_unsafe_folder_takes_the_link_away_and_greys_its_row_with_the_reason()
    {
        var registry = new MemoryLinkRegistry();
        var links = new LinkRegistration(registry, @"C:\Chainsaw\Chainsaw.exe", allowed: false);
        registry.Command = LinkRegistration.CommandFor(@"C:\Chainsaw\Chainsaw.exe");

        links.Withdraw();
        Assert.Null(registry.Command);

        links.Register();
        Assert.Null(registry.Command);

        var settings = Settings(links, new ProgramFolderVerdict(ProgramFolderProblem.OpenToOthers));
        Assert.False(settings.CanRegisterLinks);
        settings.ToggleLinks();
        Assert.Null(registry.Command);
        Assert.Equal(SettingsViewModel.ProgramFolderOpenText, settings.ProgramFolderWarning);
        Assert.True(settings.HasProgramFolderWarning);
    }

    [Fact]
    public void An_unsafe_folder_leaves_a_link_to_another_copy_alone()
    {
        var registry = new MemoryLinkRegistry { Command = LinkRegistration.CommandFor(@"D:\Other\Chainsaw.exe") };

        new LinkRegistration(registry, @"C:\Chainsaw\Chainsaw.exe", allowed: false).Withdraw();

        Assert.NotNull(registry.Command);
    }

    [Fact]
    public void The_settings_say_nothing_and_say_the_background_starts_with_windows_where_the_folder_is_safe()
    {
        var settings = Settings(null, ProgramFolderVerdict.Safe);
        var unknown = Settings(null, new ProgramFolderVerdict(ProgramFolderProblem.Unreadable));

        Assert.False(settings.HasProgramFolderWarning);
        Assert.Equal(settings.WatchesInBackground, settings.StartsWithWindows);
        Assert.Equal(SettingsViewModel.ProgramFolderUnknownText, unknown.ProgramFolderWarning);
        Assert.False(unknown.StartsWithWindows);
    }

    private static SettingsViewModel Settings(LinkRegistration? links, ProgramFolderVerdict folder)
    {
        var rig = new GateRig(VaultState.Unlocked);
        return new SettingsViewModel(
            new Chainsaw.App.Settings.AppSettings { WatchInBackground = true },
            rig.SettingsStore,
            rig.Way,
            rig.ExitPanel,
            rig.AutoLock,
            rig.Prices,
            rig.Profiles,
            rig.Vault,
            rig.Cases,
            rig.Build,
            links: links,
            programFolder: folder);
    }

    /// <summary>The user's registry in memory.</summary>
    private sealed class MemoryLinkRegistry : ILinkRegistry
    {
        public string? Command { get; set; }

        public string? ReadCommand() => Command;

        public void Write(string command, string icon) => Command = command;

        public void Delete() => Command = null;
    }
}
