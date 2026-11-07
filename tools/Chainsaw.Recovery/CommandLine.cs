namespace Chainsaw.Recovery;

/// <summary>
/// A parsed command line: the command, its positional arguments and its options (<c>--name value</c>, or
/// <c>--name</c> alone for a switch). No secret is ever taken from the command line, where it would end up in the
/// shell's history and the process list.
/// </summary>
internal sealed class CommandLine
{
    /// <summary>The prefix of an option.</summary>
    private const string OptionPrefix = "--";

    /// <summary>The options by name, without the prefix; a switch has an empty value.</summary>
    private readonly Dictionary<string, string> _options;

    /// <summary>Keeps the parsed parts.</summary>
    /// <param name="command">The command.</param>
    /// <param name="positional">The positional arguments.</param>
    /// <param name="options">The options.</param>
    private CommandLine(string command, IReadOnlyList<string> positional, Dictionary<string, string> options)
    {
        Command = command;
        Positional = positional;
        _options = options;
    }

    /// <summary>Gets the command, in lowercase.</summary>
    public string Command { get; }

    /// <summary>Gets the positional arguments after the command.</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>Parses the arguments.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="switches">The option names that take no value.</param>
    /// <returns>The command line, or <see langword="null"/> when it does not parse.</returns>
    public static CommandLine? Parse(IReadOnlyList<string> args, IReadOnlySet<string> switches)
    {
        if (args.Count == 0)
        {
            return null;
        }

        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith(OptionPrefix, StringComparison.Ordinal))
            {
                positional.Add(argument);
                continue;
            }

            var name = argument[OptionPrefix.Length..];
            if (name.Length == 0 || options.ContainsKey(name))
            {
                return null;
            }

            if (switches.Contains(name))
            {
                options[name] = string.Empty;
            }
            else if (index + 1 < args.Count)
            {
                options[name] = args[++index];
            }
            else
            {
                return null;
            }
        }

        return new CommandLine(args[0].ToLowerInvariant(), positional, options);
    }

    /// <summary>Gives an option's value.</summary>
    /// <param name="name">The option's name.</param>
    /// <returns>The value, or <see langword="null"/> when it was not given.</returns>
    public string? Option(string name) => _options.GetValueOrDefault(name);

    /// <summary>Checks whether a switch was given.</summary>
    /// <param name="name">The switch's name.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    public bool Has(string name) => _options.ContainsKey(name);

    /// <summary>Checks that only known options were given.</summary>
    /// <param name="known">The option names the command takes.</param>
    /// <returns><see langword="true"/> when every option is known.</returns>
    public bool OnlyOptions(params string[] known) => _options.Keys.All(known.Contains);
}
