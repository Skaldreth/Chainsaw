namespace Chainsaw.SharedKernel.Commands;

/// <summary>
/// A command for the command bar at the bottom of the window, written as <c>/name argument</c>. The application and
/// the modules contribute commands by registering them in the container; the bar finds them by name, completes them
/// with Tab and lists them with <c>/help</c>.
/// </summary>
/// <remarks>
/// A command answers with a short <see cref="CommandReply"/> shown above the bar. It never throws for what the user
/// did wrong; an unexpected failure is caught by the bar and shown as a short error, never as a crash.
/// </remarks>
public interface IShellCommand
{
    /// <summary>Gets the name, without the slash: lower-case ASCII letters, for example <c>verify</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Gets how the command is written, for <c>/help</c>, for example <c>/open &lt;ärendenummer&gt;</c>.
    /// </summary>
    string Usage { get; }

    /// <summary>Gets what the command does, in one short Swedish line, for <c>/help</c>.</summary>
    string Summary { get; }

    /// <summary>Suggests ways to finish the argument, for Tab; the best first.</summary>
    /// <param name="argument">What is written after the name and a space so far.</param>
    /// <returns>Whole arguments that start with what is written; empty when there is nothing to suggest.</returns>
    IReadOnlyList<string> Suggest(string argument);

    /// <summary>
    /// Says what one of <see cref="Suggest"/>'s suggestions does, in a short Swedish line for the list of suggestions
    /// under the command bar; empty when the suggestion speaks for itself, such as a case number.
    /// </summary>
    /// <param name="suggestion">The suggestion.</param>
    /// <returns>The line, or an empty text.</returns>
    string Describe(string suggestion) => string.Empty;

    /// <summary>Runs the command. Slow work is done off the calling thread.</summary>
    /// <param name="argument">What is written after the name, trimmed; empty when nothing is.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The answer to show.</returns>
    Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default);
}
