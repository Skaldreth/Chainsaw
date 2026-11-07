#if DEBUG
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.TestData;

/// <summary>
/// <c>/testdata</c>, only in a Debug build: creates a new test case with made-up data where everything Chainsaw can do
/// is seen (see <see cref="TestStory"/>), and opens it the way <c>/open</c> does.
/// </summary>
/// <param name="builder">Builds the test case.</param>
/// <param name="workflow">Opens it.</param>
public sealed class TestDataCommand(TestCaseBuilder builder, CaseWorkflow workflow) : IShellCommand
{
    /// <inheritdoc/>
    public string Name => "testdata";

    /// <inheritdoc/>
    public string Usage => "/testdata";

    /// <inheritdoc/>
    public string Summary => "Skapar ett testärende med påhittade data (bara Debug)";

    /// <inheritdoc/>
    public IReadOnlyList<string> Suggest(string argument) => [];

    /// <inheritdoc/>
    public async Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default)
    {
        if (workflow.IsBusy)
        {
            return CommandReply.Failed(CaseWorkflow.BusyText);
        }

        TestBuild build;
        try
        {
            build = await builder.BuildAsync(builder.NextNumber(), cancellationToken);
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

        var text = $"Testärendet {build.CaseNumber} skapades med påhittade data och är öppet. Etiketten från ett "
            + $"annat ärende kommer från {build.ColleagueCaseNumber}.";
        return build.Problems.Count == 0
            ? CommandReply.Done(text)
            : CommandReply.Failed($"{text} Men: {string.Join(" ", build.Problems)}");
    }
}
#endif
