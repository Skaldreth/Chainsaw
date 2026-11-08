using Chainsaw.App.Links;
using Microsoft.Win32;

namespace Chainsaw.App.Interop;

/// <summary>
/// The link <c>chainsaw:</c> in the user's part of the registry, <c>HKCU\Software\Classes\chainsaw</c>: the key says
/// it is a link (<c>URL Protocol</c>), and <c>shell\open\command</c> holds the command Windows runs. Nothing is
/// written for the whole computer.
/// </summary>
public sealed class CurrentUserLinkRegistry : ILinkRegistry
{
    /// <summary>The link's key under the user's classes.</summary>
    private const string LinkKey = @"Software\Classes\chainsaw";

    /// <summary>The key under the link that holds the command.</summary>
    private const string CommandKey = @"shell\open\command";

    /// <summary>The key under the link that holds the icon.</summary>
    private const string IconKey = "DefaultIcon";

    /// <summary>The value that tells Windows the key is a link, not a file type.</summary>
    private const string ProtocolValue = "URL Protocol";

    /// <summary>The link's name as Windows shows it.</summary>
    private const string Description = "URL:Chainsaw";

    /// <inheritdoc/>
    public string? ReadCommand()
    {
        using var link = Registry.CurrentUser.OpenSubKey(LinkKey);
        if (link?.GetValue(ProtocolValue) is null)
        {
            return null;
        }

        using var command = link.OpenSubKey(CommandKey);
        return command?.GetValue(null) as string;
    }

    /// <inheritdoc/>
    public void Write(string command, string icon)
    {
        using var link = Registry.CurrentUser.CreateSubKey(LinkKey);
        link.SetValue(null, Description, RegistryValueKind.String);
        link.SetValue(ProtocolValue, string.Empty, RegistryValueKind.String);
        using (var iconKey = link.CreateSubKey(IconKey))
        {
            iconKey.SetValue(null, $"\"{icon}\",0", RegistryValueKind.String);
        }

        using var commandKey = link.CreateSubKey(CommandKey);
        commandKey.SetValue(null, command, RegistryValueKind.String);
    }

    /// <inheritdoc/>
    public void Delete() => Registry.CurrentUser.DeleteSubKeyTree(LinkKey, throwOnMissingSubKey: false);
}
