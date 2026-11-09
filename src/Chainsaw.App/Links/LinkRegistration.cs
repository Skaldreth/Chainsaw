namespace Chainsaw.App.Links;

/// <summary>
/// Registers the link <c>chainsaw:</c> for the user, so that another program can open Chainsaw with an address or a
/// tx (CLAUDE.md, <em>Öppna med en adress</em>): Windows runs <c>Chainsaw.exe "%1"</c>, and Chainsaw takes the link
/// as it takes the argument. Only for the user, never for the computer, so no administrator is needed.
/// </summary>
/// <param name="registry">Where the link is registered.</param>
/// <param name="program">The path of <c>Chainsaw.exe</c>, which the link opens.</param>
/// <param name="allowed">
/// Whether the link may open the program: not when others can change its folder (<see cref="ProgramFolder"/>).
/// </param>
public sealed class LinkRegistration(ILinkRegistry registry, string program, bool allowed = true)
{
    /// <summary>Gets whether the link may be registered for this program.</summary>
    public bool IsAllowed => allowed;

    /// <summary>Gets the command Windows runs for a link: the program, then the link as one argument.</summary>
    public string Command => CommandFor(program);

    /// <summary>Gives the command for a program: <c>"C:\…\Chainsaw.exe" "%1"</c>.</summary>
    /// <param name="program">The path of the program.</param>
    /// <returns>The command, with both parts in quotation marks so that a path with spaces holds.</returns>
    public static string CommandFor(string program)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        return $"\"{program}\" \"%1\"";
    }

    /// <summary>Reads where the link points now.</summary>
    /// <returns>Not registered, this program, or another program.</returns>
    public LinkState Read() => registry.ReadCommand() switch
    {
        null => LinkState.None,
        var command when string.Equals(command, Command, StringComparison.OrdinalIgnoreCase) => LinkState.ThisProgram,
        _ => LinkState.OtherProgram,
    };

    /// <summary>Registers the link to open this program; where that is not allowed, takes it away instead.</summary>
    public void Register()
    {
        if (allowed)
        {
            registry.Write(Command, program);
        }
        else
        {
            Withdraw();
        }
    }

    /// <summary>
    /// Takes the link away where it opens this program and that is not allowed, so that Windows never starts the
    /// program from a folder others can change. A link to another copy is that copy's to judge.
    /// </summary>
    public void Withdraw()
    {
        if (!allowed && Read() == LinkState.ThisProgram)
        {
            registry.Delete();
        }
    }

    /// <summary>Takes the link away.</summary>
    public void Unregister() => registry.Delete();
}
