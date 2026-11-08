namespace Chainsaw.App.Links;

/// <summary>
/// Registers the link <c>chainsaw:</c> for the user, so that another program can open Chainsaw with an address or a
/// tx (CLAUDE.md, <em>Öppna med en adress</em>): Windows runs <c>Chainsaw.exe "%1"</c>, and Chainsaw takes the link
/// as it takes the argument. Only for the user, never for the computer, so no administrator is needed.
/// </summary>
/// <param name="registry">Where the link is registered.</param>
/// <param name="program">The path of <c>Chainsaw.exe</c>, which the link opens.</param>
public sealed class LinkRegistration(ILinkRegistry registry, string program)
{
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

    /// <summary>Registers the link to open this program.</summary>
    public void Register() => registry.Write(Command, program);

    /// <summary>Takes the link away.</summary>
    public void Unregister() => registry.Delete();
}
