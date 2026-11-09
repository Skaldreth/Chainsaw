using Chainsaw.App.Guide;
using Chainsaw.App.Search;
using Chainsaw.App.ViewModels;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.Commands;

/// <summary>
/// <c>/guide</c>: opens the Guide's panel at the section that best answers a concept (sketch KR1a), for example
/// <c>/guide utxo</c>; without a concept at the Guide's first section. While it is written, the command bar's search
/// lists the sections it would open.
/// </summary>
public sealed class GuideCommand : IShellCommand
{
    /// <summary>The Guide's sections.</summary>
    private readonly GuideLibrary _library;

    /// <summary>The Guide's panel (sketch GD1b).</summary>
    private readonly GuidePanelViewModel _panel;

    /// <summary>The index over the Guide alone, built the first time it is used.</summary>
    private readonly Lazy<SearchIndex> _index;

    /// <summary>Creates the command.</summary>
    /// <param name="library">The Guide's sections.</param>
    /// <param name="panel">The Guide's panel.</param>
    public GuideCommand(GuideLibrary library, GuidePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(panel);
        _library = library;
        _panel = panel;
        _index = new Lazy<SearchIndex>(() => SearchIndex.Build(null, library));
    }

    /// <inheritdoc />
    public string Name => "guide";

    /// <inheritdoc />
    public string Usage => "/guide <begrepp>";

    /// <inheritdoc />
    public string Summary => "Öppnar Guidens avsnitt om ett begrepp, till exempel /guide utxo (F1 för hela Guiden)";

    /// <inheritdoc />
    public IReadOnlyList<string> Suggest(string argument) =>
    [
        .. _library.Sections
            .Select(section => section.Id)
            .Where(id => id.StartsWith(argument ?? string.Empty, StringComparison.OrdinalIgnoreCase)),
    ];

    /// <inheritdoc />
    public string Describe(string suggestion) =>
        _library.Sections.FirstOrDefault(section => section.Id == suggestion)?.Title ?? string.Empty;

    /// <inheritdoc />
    public async Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default)
    {
        var term = (argument ?? string.Empty).Trim();
        if (term.Length == 0)
        {
            _panel.OpenSection(_library.First?.Id);
            return CommandReply.None;
        }

        var outcome = await Task.Run(() => _index.Value.Search(term, guideOnly: true, null, cancellationToken));
        return outcome.Best is { Entry.GuideSection: { } id } && _panel.OpenSection(id)
            ? CommandReply.None
            : CommandReply.Failed($"Guiden har inget om {term}. F1 öppnar hela Guiden.");
    }
}
