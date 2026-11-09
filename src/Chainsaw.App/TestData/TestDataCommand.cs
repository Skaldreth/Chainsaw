#if DEBUG
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.TestData;

/// <summary>
/// <c>/testdata</c>, only in a Debug build: creates a new test case with made-up data and opens it the way <c>/open</c>
/// does. Without an argument everything Chainsaw can do is seen (see <see cref="TestStory"/>); <c>/testdata -N</c>
/// builds a small variant that shows one thing (<see cref="TestScenarios"/>).
/// </summary>
/// <param name="builder">Builds the test case.</param>
/// <param name="workflow">Opens it.</param>
public sealed class TestDataCommand(TestCaseBuilder builder, CaseWorkflow workflow) : IShellCommand
{
    /// <inheritdoc/>
    public string Name => "testdata";

    /// <inheritdoc/>
    public string Usage => "/testdata [-1 … -8]";

    /// <inheritdoc/>
    public string Summary => "Skapar ett testärende med påhittade data, helt eller en variant -1 … -8 (bara Debug)";

    /// <inheritdoc/>
    public IReadOnlyList<string> Suggest(string argument) =>
    [
        .. TestScenarios.All
            .Where(scenario => scenario.Number > 0)
            .Select(scenario => scenario.Argument)
            .Where(value => value.StartsWith((argument ?? string.Empty).Trim(), StringComparison.Ordinal)),
    ];

    /// <inheritdoc/>
    public string Describe(string suggestion) => TestScenarios.Find(suggestion) is { Number: > 0 } scenario
        ? $"{scenario.Name}: {scenario.Summary}"
        : string.Empty;

    /// <inheritdoc/>
    public async Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default)
    {
        if (workflow.IsBusy)
        {
            return CommandReply.Failed(CaseWorkflow.BusyText);
        }

        if (TestScenarios.Find(argument) is not { } scenario)
        {
            return CommandReply.Failed($"Det finns ingen sådan variant. {TestScenarios.Listing()}");
        }

        TestBuild build;
        try
        {
            build = await builder.BuildAsync(builder.NextNumber(), scenario, cancellationToken);
        }
        catch (CaseException exception)
        {
            return CommandReply.Failed($"Testärendet kunde inte skapas: {CaseTexts.For(exception.Error)}");
        }

        await workflow.RefreshCasesAsync();
        var opened = await workflow.OpenAsync(build.CaseNumber);
        if (opened.IsError)
        {
            return opened;
        }

        var text = $"Testärendet {build.CaseNumber} skapades med påhittade data och är öppet: {scenario.Summary}";
        if (build.ColleagueCaseNumber is { } colleague)
        {
            text += $" Etiketten från ett annat ärende kommer från {colleague}.";
        }

        return build.Problems.Count == 0
            ? CommandReply.Done(text)
            : CommandReply.Failed($"{text} Men: {string.Join(" ", build.Problems)}");
    }
}
#endif
