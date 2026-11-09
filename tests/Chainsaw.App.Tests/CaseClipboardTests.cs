using System.Text.RegularExpressions;

namespace Chainsaw.App.Tests;

/// <summary>
/// Every copy Chainsaw makes itself goes through <c>Interop/CaseClipboard</c>, which keeps it out of Windows' clipboard
/// history and the cloud clipboard (principle 4). The views cannot run here, so their code is read instead.
/// </summary>
public sealed partial class CaseClipboardTests
{
    /// <summary>No file but CaseClipboard puts text on the clipboard straight through WPF's clipboard.</summary>
    [Fact]
    public void OnlyCaseClipboardWritesTheClipboard()
    {
        var app = Path.Combine(GraphSceneTests.Root(), "src", "Chainsaw.App");
        var separator = Path.DirectorySeparatorChar;
        var writers = Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}")
                && Path.GetFileName(file) != "CaseClipboard.cs")
            .Where(file => ClipboardWrite().IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(writers);
    }

    /// <summary>A write to the clipboard that bypasses CaseClipboard.</summary>
    [GeneratedRegex(@"Clipboard\.(SetText|SetDataObject|SetData|SetFileDropList|SetImage)\(")]
    private static partial Regex ClipboardWrite();
}
