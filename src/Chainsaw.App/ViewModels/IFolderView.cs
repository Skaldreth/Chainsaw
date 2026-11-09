namespace Chainsaw.App.ViewModels;

/// <summary>
/// Shows a written file in its folder, as the view models see it: the window opens Windows' Explorer with the file
/// chosen, the tests note the path.
/// </summary>
public interface IFolderView
{
    /// <summary>Shows the folder a file lies in, with the file chosen (<em>Visa i mappen</em>, sketch RP1b).</summary>
    /// <param name="path">The file's full path.</param>
    void ShowInFolder(string path);
}
