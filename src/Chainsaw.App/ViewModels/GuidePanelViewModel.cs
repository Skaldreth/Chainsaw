using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The Guide as a panel beside the work (sketch GD1b): opened from <em>Varför?</em>, the graph's <em>?</em> and the
/// method, with the answer for what is chosen at the top and the section under it, opened at the heading that
/// answers. The rest is neither darkened nor blocked: choosing something else while the panel is open shows its
/// answer. The arrow goes back through what was read, × and Esc close, and <em>Öppna i Guide</em> takes the section
/// to the view Guide (F1).
/// </summary>
public sealed partial class GuidePanelViewModel : ObservableObject
{
    /// <summary>The Guide's sections.</summary>
    private readonly GuideLibrary _library;

    /// <summary>The way back through what was read.</summary>
    private readonly GuideHistory _history = new();

    /// <summary>Creates the panel, closed.</summary>
    /// <param name="library">The Guide's sections.</param>
    public GuidePanelViewModel(GuideLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
    }

    /// <summary>Gets the Guide's sections, for the titles of the sections a text links to.</summary>
    public GuideLibrary Library => _library;

    /// <summary>Raised when <em>Öppna i Guide</em> asks for the view Guide at the place shown.</summary>
    public event EventHandler<GuidePlace>? FullGuideWanted;

    /// <summary>Gets what the panel shows, or <see langword="null"/> while it is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(HeaderText), nameof(CanGoBack))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial GuidePage? Page { get; private set; }

    /// <summary>Gets whether the panel is open.</summary>
    public bool IsOpen => Page is not null;

    /// <summary>Gets whether the arrow has somewhere to go back to.</summary>
    public bool CanGoBack => _history.CanGoBack;

    /// <summary>Gets the panel's header, for example <c>Guide · Klustring</c>.</summary>
    public string HeaderText => Page is { } page ? "Guide · " + page.Section.Group : string.Empty;

    /// <summary>
    /// Opens the panel for a request from the graph: <em>Varför?</em> (about something chosen) starts anew, while the
    /// <em>?</em> and the method step on when the panel is already open (sketch GD1b).
    /// </summary>
    /// <param name="request">The request.</param>
    public void Ask(GuideRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Open(request, fresh: request.Subject is not null);
    }

    /// <summary>
    /// Opens the panel at a request: anew from <em>Varför?</em> (the way back is forgotten), or as a step on when the
    /// panel is already open and <paramref name="fresh"/> is <see langword="false"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="fresh">Whether to start anew even when the panel is open.</param>
    public void Open(GuideRequest request, bool fresh)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (PlaceOf(request) is not { } place)
        {
            return;
        }

        if (fresh || !IsOpen)
        {
            _history.Start(place);
        }
        else
        {
            _history.Push(place);
        }

        ShowCurrent();
    }

    /// <summary>
    /// Follows what is chosen while the panel is open: a fuller answer about the same thing takes the old one's place
    /// (where it lies in the way back), and something else chosen is a step on. Nothing chosen, or a closed panel,
    /// changes nothing.
    /// </summary>
    /// <param name="request">The answer for what is chosen now, or <see langword="null"/> for nothing.</param>
    public void Follow(GuideRequest? request)
    {
        if (!IsOpen || request is null || PlaceOf(request) is not { } place)
        {
            return;
        }

        if (!_history.UpdateLatest(place))
        {
            _history.Push(place);
        }

        ShowCurrent();
    }

    /// <summary>Follows a link in the text or in an answer: a step on.</summary>
    /// <param name="link">The link.</param>
    [RelayCommand]
    public void Go(GuideLink? link)
    {
        if (link is null || _library.Section(link.Section) is not { } section)
        {
            return;
        }

        _history.Push(new GuidePlace(section.Id, section.FindAnchor(link.Anchor), null, null));
        ShowCurrent();
    }

    /// <summary>Goes one step back through what was read.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public void Back()
    {
        if (_history.Back())
        {
            ShowCurrent();
        }
    }

    /// <summary>Closes the panel and forgets the way back (× and Esc).</summary>
    [RelayCommand]
    public void Close()
    {
        _history.Clear();
        Page = null;
    }

    /// <summary>Takes the place shown to the view Guide, and closes the panel.</summary>
    [RelayCommand]
    public void OpenInGuide()
    {
        if (_history.Current is not { } place)
        {
            return;
        }

        Close();
        FullGuideWanted?.Invoke(this, place);
    }

    /// <summary>Turns a request into a place: its section's id and the anchor of its heading.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The place, or <see langword="null"/> when the section is not in the library.</returns>
    private GuidePlace? PlaceOf(GuideRequest request) =>
        _library.Section(request.Topic.FileName()) is { } section
            ? new GuidePlace(section.Id, section.FindAnchor(request.Heading), request.Context, request.Subject)
            : null;

    /// <summary>Shows the current place.</summary>
    private void ShowCurrent()
    {
        Page = _history.Current is { } place ? GuidePage.Of(_library, place) : null;
        OnPropertyChanged(nameof(CanGoBack));
        BackCommand.NotifyCanExecuteChanged();
    }
}
