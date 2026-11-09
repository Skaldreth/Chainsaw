// The test case and its made-up source exist only in a Debug build.
#if DEBUG
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.TestData.Tests;

/// <summary>The small variants of the test case (<c>/testdata -N</c>): each is built whole and verifies.</summary>
public sealed class TestScenarioTests : IDisposable
{
    /// <summary>The modules the variants are built through.</summary>
    private readonly Harness _harness = new();

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    /// <summary>The variants that are not the whole story, by number.</summary>
    public static TheoryData<int> Small => [.. TestScenarios.All.Where(s => s.Number > 0).Select(s => s.Number)];

    /// <summary>A variant is built without problems, is titled after itself and its hash chain is whole.</summary>
    /// <param name="number">The variant's number.</param>
    [Theory]
    [MemberData(nameof(Small))]
    public async Task A_variant_is_built_without_problems_and_verifies(int number)
    {
        var scenario = TestScenarios.Find($"-{number}")!;
        var builder = _harness.Get<TestCaseBuilder>();

        var build = await builder.BuildAsync(builder.NextNumber(), scenario);

        Assert.Empty(build.Problems);
        Assert.Equal(scenario.WithColleague, build.ColleagueCaseNumber is not null);
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);
        Assert.Contains(scenario.Name, @case.Title, StringComparison.Ordinal);
        Assert.True(_harness.Get<ICaseVerifier>().Verify(@case).IsIntact);
    }

    /// <summary>Nothing, 0, -N and N pick a variant; a number that is none picks nothing.</summary>
    [Fact]
    public void The_argument_picks_the_variant()
    {
        Assert.Same(TestScenarios.Whole, TestScenarios.Find(null));
        Assert.Same(TestScenarios.Whole, TestScenarios.Find(" "));
        Assert.Equal(3, TestScenarios.Find("-3")!.Number);
        Assert.Equal(3, TestScenarios.Find("3")!.Number);
        Assert.Null(TestScenarios.Find("-99"));
        Assert.Null(TestScenarios.Find("mixern"));
    }
}
#endif
