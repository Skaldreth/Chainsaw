namespace Chainsaw.App.ViewModels;

/// <summary>
/// Asks the user where to save a file, as the view models see it: the window gives Windows' dialog, the tests a path.
/// </summary>
public interface ISaveFilePicker
{
    /// <summary>
    /// Asks where to save a request (<em>Exportera .md …</em>, sketch BG1b). The dialog never asks to write over a
    /// file: the export takes the next number instead.
    /// </summary>
    /// <param name="suggestedName">The name offered, ending in <c>.md</c>.</param>
    /// <returns>The chosen path, or <see langword="null"/> when the user cancelled.</returns>
    string? PickRequestFile(string suggestedName);
}
