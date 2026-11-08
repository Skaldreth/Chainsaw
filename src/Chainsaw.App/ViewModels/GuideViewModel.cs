using Chainsaw.App.Guide;
using Chainsaw.App.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A section in the view Guide's index, marked while it is the one shown.</summary>
/// <param name="section">The section.</param>
public sealed partial class GuideIndexEntry(GuideSection section) : ObservableObject
{
    /// <summary>Gets the section.</summary>
    public GuideSection Section { get; } = section;

    /// <summary>Gets the section's title.</summary>
    public string Title => Section.Title;

    /// <summary>Gets whether the section is the one shown.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; internal set; }
}

/// <summary>A group in the view Guide's index.</summary>
/// <param name="Name">The group's name.</param>
/// <param name="Entries">Its sections.</param>
public sealed record GuideIndexGroup(string Name, IReadOnlyList<GuideIndexEntry> Entries);

/// <summary>
/// The view Guide (F1, after sketch GD1a in a simple form, since the panel GD1b is the main form): the sections in
/// groups to the left, and the chosen section in reading width with its marked heading. Opened from the panel, it has
/// a way back to where the panel was.
/// </summary>
public sealed partial class GuideViewModel : ObservableObject
{
    /// <summary>The Guide's sections.</summary>
    private readonly GuideLibrary _library;

    /// <summary>Creates the view at the first section.</summary>
    /// <param name="library">The Guide's sections.</param>
    public GuideViewModel(GuideLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        Groups = [.. library.Groups.Select(group =>
            new GuideIndexGroup(group.Name, [.. group.Sections.Select(section => new GuideIndexEntry(section))]))];
        Section = library.First;
        MarkCurrent();
    }

    /// <summary>Gets the Guide's sections, for the titles of the sections a text links to.</summary>
    public GuideLibrary Library => _library;

    /// <summary>Raised when the way back asks for the view the Guide was opened from.</summary>
    public event EventHandler<AppView>? ReturnWanted;

    /// <summary>Gets the index: the groups with their sections.</summary>
    public IReadOnlyList<GuideIndexGroup> Groups { get; }

    /// <summary>Gets the section shown, or <see langword="null"/> when the program has no Guide texts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSection))]
    public partial GuideSection? Section { get; private set; }

    /// <summary>Gets the marked heading's anchor, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? Anchor { get; private set; }

    /// <summary>Gets whether a section is shown.</summary>
    public bool HasSection => Section is not null;

    /// <summary>Gets the view the Guide was opened from, for the way back, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReturn), nameof(ReturnText))]
    public partial AppView? ReturnTo { get; private set; }

    /// <summary>Gets whether there is a way back.</summary>
    public bool HasReturn => ReturnTo is not null;

    /// <summary>Gets the way back's text, for example <c>Tillbaka till grafen</c>.</summary>
    public string ReturnText => ReturnTo switch
    {
        null => string.Empty,
        AppView.Graph => "Tillbaka till grafen",
        { } view => "Tillbaka till " + ViewTable.For(view).Name,
    };

    /// <summary>Shows a place that the panel was at, with a way back to the view it was beside.</summary>
    /// <param name="place">The place.</param>
    /// <param name="returnTo">The view to go back to, or <see langword="null"/>.</param>
    public void Open(GuidePlace place, AppView? returnTo)
    {
        ArgumentNullException.ThrowIfNull(place);
        ReturnTo = returnTo;
        ShowSection(place.Section, place.Anchor);
    }

    /// <summary>
    /// Remembers the view the Guide was reached from (the pill, F1 or a link), unless a way back is already set, so
    /// that the Guide always has a way back; the Guide itself is never a way back.
    /// </summary>
    /// <param name="view">The view shown before the Guide.</param>
    public void CameFrom(AppView view)
    {
        if (ReturnTo is null && view != AppView.Guide)
        {
            ReturnTo = view;
        }
    }

    /// <summary>Forgets the way back when the view is left, so that it never points to an old place.</summary>
    public void Leave() => ReturnTo = null;

    /// <summary>Shows a section from the index, at its top.</summary>
    /// <param name="entry">The index's entry.</param>
    [RelayCommand]
    public void Show(GuideIndexEntry? entry)
    {
        if (entry is not null)
        {
            ShowSection(entry.Section.Id, null);
        }
    }

    /// <summary>Follows a link in the text.</summary>
    /// <param name="link">The link.</param>
    [RelayCommand]
    public void Go(GuideLink? link)
    {
        if (link is not null)
        {
            ShowSection(link.Section, link.Anchor);
        }
    }

    /// <summary>Goes back to the view the Guide was opened from.</summary>
    [RelayCommand]
    public void Return()
    {
        if (ReturnTo is { } view)
        {
            ReturnTo = null;
            ReturnWanted?.Invoke(this, view);
        }
    }

    /// <summary>Shows a section at a heading; an unknown section changes nothing.</summary>
    /// <param name="id">The section's id.</param>
    /// <param name="anchor">The heading's anchor, or <see langword="null"/> for the top.</param>
    private void ShowSection(string id, string? anchor)
    {
        if (_library.Section(id) is not { } section)
        {
            return;
        }

        Section = section;
        Anchor = section.FindAnchor(anchor);
        MarkCurrent();
    }

    /// <summary>Marks the shown section in the index.</summary>
    private void MarkCurrent()
    {
        foreach (var entry in Groups.SelectMany(group => group.Entries))
        {
            entry.IsCurrent = entry.Section == Section;
        }
    }
}
