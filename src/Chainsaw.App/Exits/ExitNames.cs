using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.App.Exits;

/// <summary>The names and words an exit is shown with.</summary>
public static class ExitNames
{
    /// <summary>The name given when a file's name gives none.</summary>
    public const string Fallback = "WireGuard";

    /// <summary>Gives an exit its first name from the file's name, within the limit, or the fallback.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The name, which the user can change before the exit is stored.</returns>
    public static string FromFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        if (name.Length == 0)
        {
            return Fallback;
        }

        return name.Length > ExitRules.MaximumNameLength ? name[..ExitRules.MaximumNameLength].Trim() : name;
    }

    /// <summary>
    /// Gives the short Swedish words for why an exit failed, shown in red under its name (sketch UT1a). They never
    /// name the file's content.
    /// </summary>
    /// <param name="failure">Why it failed.</param>
    /// <returns>The words.</returns>
    public static string FailureText(ExitFailure failure) => failure switch
    {
        ExitFailure.TunnelUnavailable => "Tunneln finns inte",
        ExitFailure.ConnectFailed => "Gick inte att ansluta",
        ExitFailure.ConnectionLost => "Förbindelsen förlorad",
        ExitFailure.ConfigurationRefused => "Filen godtas inte",
        ExitFailure.NoAnswer => "Servern svarar inte",
        _ => "Fel",
    };
}
