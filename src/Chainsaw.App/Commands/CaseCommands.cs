using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.Commands;

/// <summary><c>/new</c>: opens Nytt ärende, with the case number filled in when one is given.</summary>
/// <param name="newCase">The panel.</param>
public sealed class NewCaseCommand(NewCaseViewModel newCase) : IShellCommand
{
    /// <inheritdoc />
    public string Name => "new";

    /// <inheritdoc />
    public string Usage => "/new [ärendenummer]";

    /// <inheritdoc />
    public string Summary => "Nytt ärende (Ctrl+N)";

    /// <inheritdoc />
    public IReadOnlyList<string> Suggest(string argument) => [];

    /// <inheritdoc />
    public Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default)
    {
        newCase.Show(caseNumber: argument);
        return Task.FromResult(CommandReply.None);
    }
}

/// <summary>
/// <c>/open</c>: saves and closes the open case, and opens another; Tab suggests the cases in the folder.
/// </summary>
/// <param name="workflow">Opens the case.</param>
public sealed class OpenCaseCommand(CaseWorkflow workflow) : IShellCommand
{
    /// <inheritdoc />
    public string Name => "open";

    /// <inheritdoc />
    public string Usage => "/open <ärendenummer>";

    /// <inheritdoc />
    public string Summary => "Öppnar ett ärende; det som är öppet sparas och stängs först";

    /// <inheritdoc />
    public IReadOnlyList<string> Suggest(string argument) => workflow.SuggestCaseNumbers(argument);

    /// <inheritdoc />
    public async Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default)
    {
        if (argument.Length > 0)
        {
            return await workflow.OpenAsync(argument);
        }

        var last = workflow.SuggestCaseNumbers(string.Empty).FirstOrDefault();
        return CommandReply.Failed(last is null
            ? "Det finns inga andra ärenden i ärendemappen."
            : $"Skriv /open och ärendenumret. Tab föreslår {last}, som du arbetade med senast.");
    }
}

/// <summary><c>/close</c>: saves and closes the open case.</summary>
/// <param name="workflow">Closes the case.</param>
public sealed class CloseCaseCommand(CaseWorkflow workflow) : IShellCommand
{
    /// <inheritdoc />
    public string Name => "close";

    /// <inheritdoc />
    public string Usage => "/close";

    /// <inheritdoc />
    public string Summary => "Sparar och stänger ärendet";

    /// <inheritdoc />
    public IReadOnlyList<string> Suggest(string argument) => [];

    /// <inheritdoc />
    public Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default) =>
        workflow.CloseAsync();
}

/// <summary>
/// <c>/verify</c>: goes through every sealed chain of the open case and says in plain words whether it holds, and
/// where a chain breaks (principle 1).
/// </summary>
/// <param name="session">The open case.</param>
/// <param name="verifier">Cases' verification.</param>
public sealed class VerifyCommand(CaseSession session, ICaseVerifier verifier) : IShellCommand
{
    /// <inheritdoc />
    public string Name => "verify";

    /// <inheritdoc />
    public string Usage => "/verify";

    /// <inheritdoc />
    public string Summary => "Kontrollerar att inget i ärendet har ändrats";

    /// <inheritdoc />
    public IReadOnlyList<string> Suggest(string argument) => [];

    /// <inheritdoc />
    public async Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default)
    {
        if (session.Current is not { } openCase)
        {
            return CommandReply.Failed(CaseWorkflow.NoCaseText);
        }

        return await VerifyAsync(verifier, openCase, cancellationToken);
    }

    /// <summary>
    /// Verifies a case off the UI thread and says how it went, for <c>/verify</c> and for <em>Verifiera</em> in the
    /// case picker (sketch ÄV1a) alike.
    /// </summary>
    /// <param name="verifier">Cases' verification.</param>
    /// <param name="openCase">The case to verify.</param>
    /// <param name="cancellationToken">Stops waiting for the answer.</param>
    /// <returns>A done answer when nothing has changed, otherwise an error that says what and where.</returns>
    public static async Task<CommandReply> VerifyAsync(
        ICaseVerifier verifier, ICase openCase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        try
        {
            var (verification, replaced) = await Task.Run(
                () => (verifier.Verify(openCase), AnchorReplacement.LatestIn(openCase)), cancellationToken);
            return VerificationText.Describe(verification, replaced);
        }
        catch (CaseException exception)
        {
            return CommandReply.Failed(CaseTexts.For(exception.Error));
        }
    }
}

/// <summary><c>/lock</c>: locks Chainsaw at once, as Ctrl+L does.</summary>
/// <param name="signal">Carries the request to the gate.</param>
public sealed class LockCommand(LockSignal signal) : IShellCommand
{
    /// <inheritdoc />
    public string Name => "lock";

    /// <inheritdoc />
    public string Usage => "/lock";

    /// <inheritdoc />
    public string Summary => "Låser Chainsaw (Ctrl+L)";

    /// <inheritdoc />
    public IReadOnlyList<string> Suggest(string argument) => [];

    /// <inheritdoc />
    public Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default)
    {
        signal.Request();
        return Task.FromResult(CommandReply.None);
    }
}
