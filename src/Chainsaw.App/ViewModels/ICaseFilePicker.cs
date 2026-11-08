namespace Chainsaw.App.ViewModels;

/// <summary>
/// Asks the user for a case file, as the view models see it: the window gives Windows' dialog, the tests a path.
/// </summary>
public interface ICaseFilePicker
{
    /// <summary>Asks for a colleague's case file, a <c>.cut</c> file (sketch ÄR1b).</summary>
    /// <returns>The file's path, or <see langword="null"/> when the user cancelled.</returns>
    string? PickCaseFile();
}
