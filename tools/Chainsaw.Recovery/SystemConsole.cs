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
        if (Console.IsInputRedirected)
        {
            return (Console.In.ReadLine() ?? string.Empty).ToCharArray();
        }

        // Room for any realistic secret up front, so that the list never copies it to a buffer it cannot clear.
        var typed = new List<char>(capacity: 1024);
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
            for (var index = 0; index < typed.Count; index++)
            {
                typed[index] = '\0';
            }
        }
    }
}
