#if DEBUG
using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>What a build of the test case gave.</summary>
/// <param name="CaseNumber">The test case's number.</param>
/// <param name="ColleagueCaseNumber">The colleague's case's number, where the label from another case was set.</param>
/// <param name="Problems">What did not go as planned, in plain Swedish; empty when everything did.</param>
public sealed record TestBuild(string CaseNumber, string ColleagueCaseNumber, IReadOnlyList<string> Problems);

/// <summary>
/// Builds the test case through the same modules as a real case: a colleague's case first, whose labels go into the
/// label library, then the test case, marked as one in its journal before anything is looked up, built step by step
/// from <see cref="TestStory"/> and saved. Both cases are closed when it is done; opening the test case is the caller's.
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

    /// <summary>Builds the test case and its colleague's case, off the calling thread.</summary>
    /// <param name="number">The test case's number, from <see cref="NextNumber"/>.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What the build gave.</returns>
    /// <exception cref="CaseException">A case could not be created or written.</exception>
    public Task<TestBuild> BuildAsync(string number, CancellationToken cancellationToken = default) =>
        Task.Run(() => BuildCoreAsync(number, cancellationToken), cancellationToken);

    /// <summary>Builds both cases.</summary>
    /// <param name="number">The test case's number.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What the build gave.</returns>
    private async Task<TestBuild> BuildCoreAsync(string number, CancellationToken cancellationToken)
    {
        var colleagueNumber = number + ColleagueSuffix;
        var problems = new List<string>();
        problems.AddRange(await BuildOneAsync(
            colleagueNumber, TestStory.ColleagueTitle, TestStory.ColleagueSteps(actors), true, cancellationToken)
            .ConfigureAwait(false));
        problems.AddRange(await BuildOneAsync(
            number, TestStory.Title, TestStory.Steps(actors), false, cancellationToken).ConfigureAwait(false));
        return new TestBuild(number, colleagueNumber, problems);
    }

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
