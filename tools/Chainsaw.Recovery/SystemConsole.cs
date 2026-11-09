namespace Chainsaw.Recovery;

/// <summary>
/// The real console: secrets are typed without echo, or read as one line when the input is redirected.
/// </summary>
internal sealed class SystemConsole : IToolConsole
{
    /// <inheritdoc />
    public void Out(string line) => Console.Out.WriteLine(line);

    /// <inheritdoc />
    public void Error(string line) => Console.Error.WriteLine(line);

    /// <inheritdoc />
    public char[] ReadSecret(string prompt)
    {
        Console.Error.Write(prompt + ": ");

        // Room for any realistic secret up front, so that the list never copies it to a buffer it cannot clear.
        var typed = new List<char>(capacity: 1024);
        if (Console.IsInputRedirected)
        {
            return ReadRedirectedLine(typed);
        }

        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (typed.Count > 0)
                    {
                        typed[^1] = '\0';
                        typed.RemoveAt(typed.Count - 1);
                    }

                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    typed.Add(key.KeyChar);
                }
            }

            Console.Error.WriteLine();
            return [.. typed];
        }
        finally
        {
            Clear(typed);
        }
    }

    /// <summary>
    /// Reads one line of redirected input character by character, never as a <see cref="string"/>, which could not be
    /// cleared.
    /// </summary>
    /// <param name="typed">The list to read into; cleared afterwards.</param>
    /// <returns>The line without its line break, in an array the caller clears.</returns>
    private static char[] ReadRedirectedLine(List<char> typed)
    {
        try
        {
            int read;
            while ((read = Console.In.Read()) is not -1 and not '\n')
            {
                if (read != '\r')
                {
                    typed.Add((char)read);
                }
            }

            return [.. typed];
        }
        finally
        {
            Clear(typed);
        }
    }

    /// <summary>Overwrites the characters in a list before it is let go.</summary>
    /// <param name="typed">The list.</param>
    private static void Clear(List<char> typed)
    {
        for (var index = 0; index < typed.Count; index++)
        {
            typed[index] = '\0';
        }
    }
}
