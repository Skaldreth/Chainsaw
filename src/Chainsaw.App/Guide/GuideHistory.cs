namespace Chainsaw.App.Guide;

/// <summary>A place in the Guide: a section, the heading marked in it, and the answer shown above it.</summary>
/// <param name="Section">The section's id.</param>
/// <param name="Anchor">The marked heading's anchor, or <see langword="null"/>.</param>
/// <param name="Context">The answer for what is chosen, or <see langword="null"/>.</param>
/// <param name="Subject">What the answer is about, or <see langword="null"/>.</param>
public sealed record GuidePlace(string Section, string? Anchor, GuideContext? Context, string? Subject);

/// <summary>
/// The way back through what was read in the Guide's panel (sketch GD1b): the arrow goes one place back, and the
/// first place has nowhere to go.
/// </summary>
public sealed class GuideHistory
{
    /// <summary>The most places kept; the oldest go first.</summary>
    public const int MostPlaces = 50;

    /// <summary>The places, the current last.</summary>
    private readonly List<GuidePlace> _places = [];

    /// <summary>Gets the current place, or <see langword="null"/> when nothing is read.</summary>
    public GuidePlace? Current => _places.Count > 0 ? _places[^1] : null;

    /// <summary>Gets whether there is a place to go back to.</summary>
    public bool CanGoBack => _places.Count > 1;

    /// <summary>Gets how many places there are.</summary>
    public int Count => _places.Count;

    /// <summary>Starts anew from a place, forgetting the way here.</summary>
    /// <param name="place">The place.</param>
    public void Start(GuidePlace place)
    {
        ArgumentNullException.ThrowIfNull(place);
        _places.Clear();
        _places.Add(place);
    }

    /// <summary>Goes on to a place; the same place again is not added twice.</summary>
    /// <param name="place">The place.</param>
    public void Push(GuidePlace place)
    {
        ArgumentNullException.ThrowIfNull(place);
        if (Current == place)
        {
            return;
        }

        _places.Add(place);
        if (_places.Count > MostPlaces)
        {
            _places.RemoveAt(0);
        }
    }

    /// <summary>Puts a place in the current one's stead, for example a fuller answer for the same thing.</summary>
    /// <param name="place">The place.</param>
    public void Replace(GuidePlace place)
    {
        ArgumentNullException.ThrowIfNull(place);
        if (_places.Count == 0)
        {
            _places.Add(place);
            return;
        }

        _places[^1] = place;
    }

    /// <summary>
    /// Puts a fuller answer in the stead of the latest answer about the same thing, wherever it lies in the way back,
    /// so that an answer that comes later (a cluster's links) never pulls the reader away from what they followed.
    /// The place keeps its section and heading: only the answer above them changes, so that the text being read
    /// never turns into another section.
    /// </summary>
    /// <param name="place">The fuller answer; its <see cref="GuidePlace.Subject"/> says what it is about.</param>
    /// <returns>
    /// <see langword="true"/> when the latest answer was about the same thing and was replaced; otherwise nothing
    /// changes.
    /// </returns>
    public bool UpdateLatest(GuidePlace place)
    {
        ArgumentNullException.ThrowIfNull(place);
        var latest = _places.FindLastIndex(each => each.Subject is not null);
        if (place.Subject is null || latest < 0 || _places[latest].Subject != place.Subject)
        {
            return false;
        }

        _places[latest] = _places[latest] with { Context = place.Context };
        return true;
    }

    /// <summary>Goes one place back.</summary>
    /// <returns><see langword="true"/> when it went back; at the first place it stays.</returns>
    public bool Back()
    {
        if (!CanGoBack)
        {
            return false;
        }

        _places.RemoveAt(_places.Count - 1);
        return true;
    }

    /// <summary>Forgets every place.</summary>
    public void Clear() => _places.Clear();
}
