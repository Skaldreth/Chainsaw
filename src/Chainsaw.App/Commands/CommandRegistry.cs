using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.Commands;

/// <summary>One suggestion under the command bar (auto-suggest): what it fills in, its name and what it does.</summary>
/// <param name="Completion">The whole text the bar gets when it is taken.</param>
/// <param name="Label">What the row says first: the command with its slash, or the argument.</param>
/// <param name="Detail">What it does, in one short line; empty when the label speaks for itself.</param>
public sealed record CommandSuggestion(string Completion, string Label, string Detail);

/// <summary>
/// The command bar's commands: those the application and the modules register as <see cref="IShellCommand"/>, and
/// <c>/help</c>, which lists them. Text without a slash goes to <see cref="StartPointInput"/>, where an address or a
/// tx becomes a start point. Nothing here throws: every failure is a short answer above the bar.
/// </summary>
public sealed class CommandRegistry
{
    /// <summary>The name of the command that lists the others.</summary>
    public const string HelpName = "help";

    /// <summary>What <c>/help</c> does.</summary>
    private const string HelpSummary = "Visar kommandona";

    /// <summary>What is said for a command that does not exist.</summary>
    private const string UnknownText = "Okänt kommando. Skriv /help för att se kommandona.";

    /// <summary>The registered commands by name.</summary>
    private readonly SortedDictionary<string, IShellCommand> _commands = new(StringComparer.Ordinal);

    /// <summary>What text without a slash becomes.</summary>
    private readonly StartPointInput _freeText;

    /// <summary>
    /// Collects the commands; two with the same name, or a name that is not lower-case letters, is a bug.
    /// </summary>
    /// <param name="commands">The commands from the container.</param>
    /// <param name="freeText">What text without a slash becomes.</param>
    /// <exception cref="ArgumentException">A name is invalid, taken twice or is <c>help</c>.</exception>
    public CommandRegistry(IEnumerable<IShellCommand> commands, StartPointInput freeText)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _freeText = freeText;
        foreach (var command in commands)
        {
            if (command.Name.Length == 0 || !command.Name.All(char.IsAsciiLetterLower) || command.Name == HelpName
                || !_commands.TryAdd(command.Name, command))
            {
                throw new ArgumentException(
                    $"The command name '{command.Name}' is invalid or taken.", nameof(commands));
            }
        }
    }

    /// <summary>Gets the commands' names with the slash, <c>/help</c> included, in alphabetical order.</summary>
    public IReadOnlyList<string> Names =>
        [.. _commands.Keys.Append(HelpName).Order(StringComparer.Ordinal).Select(name => "/" + name)];

    /// <summary>Runs what is written in the bar.</summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The answer; never an exception.</returns>
    public async Task<CommandReply> RunAsync(string text, CancellationToken cancellationToken = default)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return CommandReply.None;
        }

        try
        {
            if (!trimmed.StartsWith('/'))
            {
                return await _freeText.RunAsync(trimmed);
            }

            var (name, argument) = Split(trimmed);
            if (name == HelpName)
            {
                return Help();
            }

            return _commands.TryGetValue(name, out var command)
                ? await command.RunAsync(argument, cancellationToken)
                : CommandReply.Failed(UnknownText);
        }
        catch (OperationCanceledException)
        {
            return CommandReply.Failed("Avbrutet.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CommandReply.Failed("Kommandot stoppades av ett oväntat fel. Inget mer gjordes.");
        }
    }

    /// <summary>
    /// Gives the ways to finish what is written, for Tab: command names while the name is written, then the
    /// command's suggestions for its argument. The best comes first.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>Whole texts for the bar; empty when there is nothing to suggest.</returns>
    public IReadOnlyList<string> Complete(string text)
    {
        var trimmed = (text ?? string.Empty).TrimStart();
        if (!trimmed.StartsWith('/'))
        {
            return [];
        }

        if (!trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return [.. Names.Where(name => name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))];
        }

        var (name, argument) = Split(trimmed);
        if (!_commands.TryGetValue(name, out var command))
        {
            return [];
        }

        try
        {
            return [.. command.Suggest(argument).Select(suggestion => $"/{name} {suggestion}")];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return [];
        }
    }

    /// <summary>
    /// Gives the suggestions for what is written, for the list under the bar (auto-suggest): while the name is
    /// written, the commands it can become with what each does; after the name and a space, the command's suggestions
    /// for its argument. A text without a slash is the search's, and gets none here.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The suggestions, the best first; empty when there are none.</returns>
    public IReadOnlyList<CommandSuggestion> Suggestions(string text)
    {
        var trimmed = (text ?? string.Empty).TrimStart();
        if (!trimmed.StartsWith('/'))
        {
            return [];
        }

        if (!trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return [.. Complete(trimmed).Select(name => new CommandSuggestion(name, Usage(name), Summary(name)))];
        }

        var (commandName, argument) = Split(trimmed);
        if (!_commands.TryGetValue(commandName, out var command))
        {
            return [];
        }

        try
        {
            return
            [
                .. command.Suggest(argument).Select(suggestion =>
                    new CommandSuggestion($"/{commandName} {suggestion}", suggestion, command.Describe(suggestion))),
            ];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return [];
        }
    }

    /// <summary>Gives how a command is written, from its name with the slash.</summary>
    /// <param name="name">The name, for example <c>/open</c>.</param>
    /// <returns>Its usage, for example <c>/open &lt;ärendenummer&gt;</c>.</returns>
    private string Usage(string name) =>
        _commands.TryGetValue(name[1..], out var command) ? command.Usage : name;

    /// <summary>Gives what a command does, from its name with the slash.</summary>
    /// <param name="name">The name, for example <c>/open</c>.</param>
    /// <returns>Its summary.</returns>
    private string Summary(string name) =>
        _commands.TryGetValue(name[1..], out var command) ? command.Summary : HelpSummary;

    /// <summary>
    /// Gives a hint for what is written: the command's form and what Tab would fill in, so that, for example, the
    /// case worked on last is offered for <c>/open</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The hint, or an empty text.</returns>
    public string Hint(string text)
    {
        var trimmed = (text ?? string.Empty).TrimStart();
        if (!trimmed.StartsWith('/') || !trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var (name, _) = Split(trimmed);
        if (!_commands.TryGetValue(name, out var command))
        {
            return string.Empty;
        }

        var first = Complete(trimmed).FirstOrDefault();
        return first is null || first.TrimEnd() == trimmed.TrimEnd()
            ? command.Usage
            : $"{command.Usage} · Tab: {first[(name.Length + 2)..]}";
    }

    /// <summary>Lists the commands, one per line.</summary>
    /// <returns>The answer.</returns>
    private CommandReply Help() =>
        CommandReply.Done(string.Join(
            Environment.NewLine,
            _commands.Values.Select(command => $"{command.Usage}  {command.Summary}")
                .Append($"/{HelpName}  {HelpSummary}")
                .Append("En adress eller tx utan / blir en startpunkt i det öppna ärendet. Tab fyller i.")));

    /// <summary>Splits <c>/name argument</c>.</summary>
    /// <param name="text">The text, starting with a slash.</param>
    /// <returns>The name in lower case and the trimmed argument.</returns>
    private static (string Name, string Argument) Split(string text)
    {
        var body = text[1..];
        var space = body.IndexOfAny([' ', '\t']);
        return space < 0
            ? (body.ToLowerInvariant(), string.Empty)
            : (body[..space].ToLowerInvariant(), body[(space + 1)..].Trim());
    }
}
