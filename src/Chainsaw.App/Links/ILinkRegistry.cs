namespace Chainsaw.App.Links;

/// <summary>
/// Where the link <c>chainsaw:</c> is registered for the user in Windows, read and written as plain values, so that
/// <see cref="LinkRegistration"/> is tested without Windows. The Windows registry is
/// <c>HKCU\Software\Classes\chainsaw</c> with <c>URL Protocol</c> and <c>shell\open\command</c>.
/// </summary>
public interface ILinkRegistry
{
    /// <summary>Reads the command Windows runs for a link.</summary>
    /// <returns>The command, or <see langword="null"/> when the link is not registered.</returns>
    string? ReadCommand();

    /// <summary>Registers the link for the user, replacing what was there.</summary>
    /// <param name="command">The command Windows runs, with <c>%1</c> for the link.</param>
    /// <param name="icon">The program whose icon Windows shows for the link.</param>
    void Write(string command, string icon);

    /// <summary>Takes the link away; harmless when it is not registered.</summary>
    void Delete();
}
