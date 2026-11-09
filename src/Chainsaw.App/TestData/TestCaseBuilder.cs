#if DEBUG
using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>What a build of the test case gave.</summary>
/// <param name="CaseNumber">The test case's number.</param>
/// <param name="ColleagueCaseNumber">
/// The colleague's case's number, where the label from another case was set, or <see langword="null"/> when the variant
/// has none.
/// </param>
/// <param name="Problems">What did not go as planned, in plain Swedish; empty when everything did.</param>
public sealed record TestBuild(string CaseNumber, string? ColleagueCaseNumber, IReadOnlyList<string> Problems);

/// <summary>
/// Builds the test case through the same modules as a real case: a colleague's case first, whose labels go into the
/// label library, then the test case, marked as one in its journal before anything is looked up, built step by step
/// from a variant (<see cref="TestScenarios"/>, the whole <see cref="TestStory"/> unless another is asked for) and saved. Both cases are closed when it is done; opening the test case is the caller's.
/// </summary>
/// <param name="services">The modules.</param>
/// <param name="chain">The made-up chain.</param>
/// <param name="actors">The story's actors.</param>
public sealed class TestCaseBuilder(TestServices services, TestChain chain, TestActors actors)
{
    /// <summary>What a test case's number starts with.</summary>
    public const string NumberPrefix = "TEST-";

    /// <summary>What the colleague's case's number ends with.</summary>
    public const string ColleagueSuffix = "-KOLLEGA";

    /// <summary>Gives the first free test case number: <c>TEST-0001</c>, <c>TEST-0002</c> and so on.</summary>
    /// <returns>The number.</returns>
    public string NextNumber()
    {
        var taken = services.Cases.List().Select(file => file.CaseNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var number = NumberPrefix + index.ToString("D4", CultureInfo.InvariantCulture);
            if (!taken.Contains(number) && !taken.Contains(number + ColleagueSuffix))
            {
                return number;
            }
        }
    }

    /// <summary>Builds the test case and, when its variant has one, its colleague's case, off the calling thread.</summary>
    /// <param name="number">The test case's number, from <see cref="NextNumber"/>.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What the build gave.</returns>
    /// <exception cref="CaseException">A case could not be created or written.</exception>
    public Task<TestBuild> BuildAsync(string number, CancellationToken cancellationToken = default) =>
        BuildAsync(number, TestScenarios.Whole, cancellationToken);

    /// <summary>Builds a variant of the test case, off the calling thread.</summary>
    /// <param name="number">The test case's number, from <see cref="NextNumber"/>.</param>
    /// <param name="scenario">The variant.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What the build gave.</returns>
    /// <exception cref="CaseException">A case could not be created or written.</exception>
    public Task<TestBuild> BuildAsync(
        string number, TestScenario scenario, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return Task.Run(() => BuildCoreAsync(number, scenario, cancellationToken), cancellationToken);
    }

    /// <summary>Builds the colleague's case when the variant has one, and then the test case.</summary>
    /// <param name="number">The test case's number.</param>
    /// <param name="scenario">The variant.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What the build gave.</returns>
    private async Task<TestBuild> BuildCoreAsync(
        string number, TestScenario scenario, CancellationToken cancellationToken)
    {
        var colleagueNumber = scenario.WithColleague ? number + ColleagueSuffix : null;
        var problems = new List<string>();
        if (colleagueNumber is not null)
        {
            problems.AddRange(await BuildOneAsync(
                colleagueNumber, TestStory.ColleagueTitle, TestStory.ColleagueSteps(actors), true, cancellationToken)
                .ConfigureAwait(false));
        }

        problems.AddRange(await BuildOneAsync(
            number, TitleOf(scenario), scenario.Steps(actors), false, cancellationToken).ConfigureAwait(false));
        return new TestBuild(number, colleagueNumber, problems);
    }

    /// <summary>Gives a variant's case title: the whole story's own, or the variant's name after it.</summary>
    /// <param name="scenario">The variant.</param>
    /// <returns>The title.</returns>
    private static string TitleOf(TestScenario scenario) =>
        scenario.Number == 0 ? TestStory.Title : $"{TestStory.Title} ({scenario.Argument} {scenario.Name})";

    /// <summary>
    /// Creates one case, marks it, runs its steps and saves it. A colleague's case has its labels taken into the label
    /// library, as opening it would.
    /// </summary>
    /// <param name="number">The case number.</param>
    /// <param name="title">The title.</param>
    /// <param name="steps">The steps.</param>
    /// <param name="intoLibrary">Whether the case's labels go into the label library.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What did not go as planned.</returns>
    private async Task<IReadOnlyList<string>> BuildOneAsync(
        string number, string title, IReadOnlyList<ITestStep> steps, bool intoLibrary,
        CancellationToken cancellationToken)
    {
        using var @case = services.Cases.Create(number);
        @case.SetTitle(title);
        TestCases.Mark(@case);
        var run = new TestRun(services, @case, chain, actors);
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await step.RunAsync(run, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // One step that a change elsewhere broke must not keep the rest of the story from being built; the
                // reply says which.
                run.Note($"Steget {step.GetType().Name} misslyckades: {exception.Message}");
            }
        }

        if (intoLibrary)
        {
            services.Library?.Absorb(@case);
        }

        @case.Save();
        @case.Close();
        return run.Problems;
    }
}
#endif
