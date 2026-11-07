using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "chainsaw-settings-" + Guid.NewGuid());

    [Fact]
    public void Without_a_file_the_automatic_lock_is_fifteen_minutes()
    {
        var settings = new AppSettingsStore(_directory).Load();

        Assert.Equal(TimeSpan.FromMinutes(15), settings.AutoLockTimeout);
    }

    [Fact]
    public void A_chosen_time_is_kept()
    {
        var store = new AppSettingsStore(_directory);

        store.Save(new AppSettings { AutoLockMinutes = 5 });

        Assert.Equal(5, store.Load().AutoLockMinutes);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void A_damaged_file_gives_the_defaults()
    {
        Directory.CreateDirectory(_directory);
        var store = new AppSettingsStore(_directory);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.Equal(AppSettings.DefaultAutoLockMinutes, store.Load().AutoLockMinutes);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100_000, 480)]
    public void A_time_out_of_range_is_kept_within_it(int minutes, int expected) =>
        Assert.Equal(TimeSpan.FromMinutes(expected), new AppSettings { AutoLockMinutes = minutes }.AutoLockTimeout);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

public sealed class AutoLockTests
{
    [Fact]
    public void The_lock_is_due_when_the_time_without_activity_has_run_out()
    {
        var clock = new TestClock();
        var autoLock = new AutoLock(clock, TimeSpan.FromMinutes(15));

        clock.UtcNow += TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1);
        Assert.False(autoLock.IsDue());
        clock.UtcNow += TimeSpan.FromSeconds(1);
        Assert.True(autoLock.IsDue());

        autoLock.Touch();
        Assert.False(autoLock.IsDue());
    }

    [Theory]
    [InlineData(1, "Låses automatiskt efter 1 minut utan aktivitet.")]
    [InlineData(15, "Låses automatiskt efter 15 minuter utan aktivitet.")]
    public void The_line_says_the_time(int minutes, string text) =>
        Assert.Equal(text, new AutoLock(new TestClock(), TimeSpan.FromMinutes(minutes)).Description);
}

public sealed class BuildInfoTests
{
    [Fact]
    public void The_commit_is_read_from_the_informational_version_and_shortened()
    {
        var build = BuildInfo.Parse("0.1.0+744412d203735183ef597f8b87ecc74cc20442d5");

        Assert.Equal("0.1.0", build.Version);
        Assert.Equal("744412d", build.ShortCommit);
    }

    [Fact]
    public void Without_a_commit_it_says_unknown()
    {
        var build = BuildInfo.Parse("0.1.0");

        Assert.Null(build.Commit);
        Assert.Equal("okänd", build.ShortCommit);
    }

    [Fact]
    public void The_test_assembly_can_be_read()
    {
        Assert.False(string.IsNullOrEmpty(BuildInfo.From(typeof(BuildInfoTests).Assembly).Version));
    }
}

public sealed class CaseSessionTests
{
    [Fact]
    public void Opening_a_case_names_it_and_a_new_one_lets_go_of_the_old()
    {
        var session = new CaseSession();
        var changes = 0;
        session.Changed += (_, _) => changes++;
        var first = new TestCase("A-1");

        session.Open(first);
        session.Open(new TestCase("A-2"));

        Assert.Equal("A-2", session.Current?.Info.CaseNumber);
        Assert.True(SpinWait.SpinUntil(() => first.Disposed > 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Forgetting_without_a_case_does_nothing()
    {
        var session = new CaseSession();
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.Forget();

        Assert.Equal(0, changes);
    }
}
