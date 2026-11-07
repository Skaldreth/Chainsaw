namespace Chainsaw.App.ViewModels;

/// <summary>
/// Asks the user for a file, as the view models see it: the window gives Windows' dialog, the tests a path.
/// </summary>
public interface IFilePicker
{
    /// <summary>Asks for a sanction list the user has saved, an XML file.</summary>
    /// <returns>The file's path, or <see langword="null"/> when the user cancelled.</returns>
    string? PickSanctionList();

    /// <summary>Asks for a WireGuard configuration, a <c>.conf</c> file (sketch UT1a).</summary>
    /// <returns>The file's path, or <see langword="null"/> when the user cancelled.</returns>
    string? PickWireGuardFile();
}
