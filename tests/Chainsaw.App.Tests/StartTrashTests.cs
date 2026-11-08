using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>Throwing cases on the start page (sketch SO1a): hidden there, kept on this computer, never deleted.</summary>
public sealed class StartTrashTests
{
    private sealed class Rig
    {
        public Rig()
        {
            Workflow = new CaseWorkflow(Store, new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Start = new GraphStartViewModel(Session, Workflow, new InlineUi(), new TestClock(), TimeZoneInfo.Utc);
            Start.UseCaseTrash(Trash);
            Start.CaseThrown += (_, number) => Thrown.Add(number);
        }

        public TestCaseStore Store { get; } = new();

        public CaseSession Session { get; } = new();

        public CaseWorkflow Workflow { get; }

        public GraphStartViewModel Start { get; }

        public CaseTrash Trash { get; } = new(new TestClock());

        public List<string> Thrown { get; } = [];

        public async Task ListAsync(params string[] numbers)
        {
            for (var index = 0; index < numbers.Length; index++)
            {
                Store.AddFile(numbers[index], minutesAgo: 60 * (index + 1));
            }

            await Workflow.RefreshCasesAsync();
        }

        public IEnumerable<string> Loose => Start.Recent.Select(item => item.CaseNumber);
    }

    [Fact]
    public async Task A_lifted_case_dropped_on_the_hot_bin_leaves_the_ring_and_comes_back_when_put_back()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129", "2026-0124", "2026-0122", "2026-0101");
        rig.Start.Lift("2026-0129");
        rig.Start.LiftNear(0);
        Assert.True(rig.Start.Bin.IsHot);

        Assert.True(rig.Start.DropOnBin("2026-0129"));
        rig.Start.Drop();

        Assert.Equal(["2026-0129"], rig.Thrown);
        Assert.DoesNotContain("2026-0129", rig.Loose);
        Assert.Contains("2026-0101", rig.Loose);
        Assert.True(rig.Start.Bin.HasThrows);
        Assert.True(rig.Trash.Contains("2026-0129"));

        rig.Start.RestoreCase("2026-0129");

        Assert.Contains("2026-0129", rig.Loose);
        Assert.False(rig.Start.Bin.HasThrows);
    }

    [Fact]
    public async Task A_release_away_from_the_bin_throws_nothing()
    {
        var rig = new Rig();
        await rig.ListAsync("2026-0131", "2026-0129");
        rig.Start.Lift("2026-0129");
        rig.Start.LiftNear(600);

        Assert.False(rig.Start.DropOnBin("2026-0129"));

        Assert.Empty(rig.Thrown);
        Assert.Contains("2026-0129", rig.Loose);
    }

    [Fact]
    public void Without_a_lifted_case_the_bin_does_not_grow()
    {
        var rig = new Rig();

        rig.Start.LiftNear(0);

        Assert.False(rig.Start.Bin.IsDragging);
    }
}
