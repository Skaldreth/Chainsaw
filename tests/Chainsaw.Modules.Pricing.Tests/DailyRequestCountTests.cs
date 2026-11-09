namespace Chainsaw.Modules.Pricing.Tests;

/// <summary>The Riksbank's daily count kept across restarts on this computer.</summary>
public sealed class DailyRequestCountTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 11, 8);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-count-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_directory, "Kurser", "riksbank-requests.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void The_count_survives_a_restart_on_the_same_day()
    {
        var first = new DailyRequestCount(File);
        first.Add(Day);
        first.Add(Day);

        var restarted = new DailyRequestCount(File);

        Assert.Equal(2, restarted.On(Day));
    }

    [Fact]
    public void A_new_day_starts_at_zero()
    {
        var count = new DailyRequestCount(File);
        count.Add(Day);

        Assert.Equal(0, new DailyRequestCount(File).On(Day.AddDays(1)));
        count.Add(Day.AddDays(1));
        Assert.Equal(1, new DailyRequestCount(File).On(Day.AddDays(1)));
    }

    [Fact]
    public void A_clock_set_back_never_resets_the_count()
    {
        var count = new DailyRequestCount(File);
        count.Add(Day);
        count.Add(Day);

        count.Add(Day.AddDays(-1));

        Assert.Equal(3, new DailyRequestCount(File).On(Day));
        Assert.Equal(3, count.On(Day.AddDays(-1)));
    }

    [Fact]
    public void A_broken_file_counts_from_zero()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, "{not json");

        Assert.Equal(0, new DailyRequestCount(File).On(Day));
    }

    [Fact]
    public void Without_a_file_it_counts_in_memory()
    {
        var count = new DailyRequestCount(null);
        count.Add(Day);

        Assert.Equal(1, count.On(Day));
        Assert.False(Directory.Exists(_directory));
    }
}
