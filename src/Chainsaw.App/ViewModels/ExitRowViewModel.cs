using Chainsaw.App.Exits;
using Chainsaw.Modules.Network.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// An exit in the vault as a row (sketches UT2b and UT3b): its dot, name, place, what it is seen as and its button; on
/// the settings' page Nätverk it folds out with its name and place to change, its check and <em>Ta bort …</em>.
/// </summary>
public sealed partial class ExitRowViewModel : ObservableObject
{
    /// <summary>What a connected row says under its name.</summary>
    public const string ConnectedText = "Ansluten";

    /// <summary>What a row being connected says under its name.</summary>
    public const string ConnectingText = "Ansluter …";

    /// <summary>What the box <em>Kontroll</em> says beside its title while the exit is not connected.</summary>
    public const string NotConnectedText = "Inte ansluten";

    /// <summary>The button's words for a row that is not connected.</summary>
    public const string ConnectText = "Anslut";

    /// <summary>The button's words for a row that is connected or being connected.</summary>
    public const string DisconnectText = "Koppla ner";

    /// <summary>The latest answer of the check, or <see langword="null"/>.</summary>
    private ExitSighting? _sighting;

    /// <summary>Now, as last told, so that today's answer says <em>i dag</em>.</summary>
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    /// <summary>Creates the row for an exit.</summary>
    /// <param name="exit">The exit, without its configuration.</param>
    public ExitRowViewModel(ExitInfo exit)
    {
        ArgumentNullException.ThrowIfNull(exit);
        Id = exit.Id;
        Name = exit.Name;
        Place = exit.Place;
    }

    /// <summary>Gets the exit's id.</summary>
    public Guid Id { get; }

    /// <summary>Gets the name the user knows the exit by.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionName), nameof(RemoveText))]
    public partial string Name { get; private set; }

    /// <summary>Gets where the exit stands, such as <c>Sverige · Stockholm</c>; empty when unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceText), nameof(HasPlace))]
    public partial string Place { get; private set; }

    /// <summary>Gets the column Plats: the place, or that none is given.</summary>
    public string PlaceText => HasPlace ? Place : ExitPlace.NoPlaceText;

    /// <summary>Gets whether the exit has a place.</summary>
    public bool HasPlace => Place.Length > 0;

    /// <summary>Gets where the exit stands.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn), nameof(ActionText), nameof(ActionName), nameof(CanCheck))]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    public partial ExitRowState State { get; private set; }

    /// <summary>Gets the words under the name: the state, the reason for a failure, or nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWord))]
    public partial string Word { get; private set; } = string.Empty;

    /// <summary>Gets whether the exit is the program's selected one (the accent bar at the left).</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    /// <summary>Gets whether there are words under the name.</summary>
    public bool HasWord => Word.Length > 0;

    /// <summary>Gets whether the exit is connected or being connected, so that its button disconnects.</summary>
    public bool IsOn => State is ExitRowState.Connected or ExitRowState.Connecting;

    /// <summary>Gets the button's words, shown on hover.</summary>
    public string ActionText => IsOn ? DisconnectText : ConnectText;

    /// <summary>Gets the button's name for a screen reader: its words and the exit's name.</summary>
    public string ActionName => $"{ActionText} {Name}";

    /// <summary>Gets what the box <em>Kontroll</em> says beside its title: connected or not.</summary>
    public string ConnectionText => State == ExitRowState.Connected ? ConnectedText : NotConnectedText;

    /// <summary>Gets whether the exit can be checked now: only a connected exit can be asked through.</summary>
    public bool CanCheck => State == ExitRowState.Connected && Check != ExitCheckState.Checking;

    /// <summary>Gets whether the row is folded out on the settings' page.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    /// <summary>Gets or sets the name in the folded-out row's field.</summary>
    [ObservableProperty]
    public partial string EditName { get; set; } = string.Empty;

    /// <summary>Gets or sets the country chosen in the folded-out row, or <see langword="null"/> for none.</summary>
    [ObservableProperty]
    public partial Country? EditCountry { get; set; }

    /// <summary>Gets or sets the city in the folded-out row's field; it may be empty.</summary>
    [ObservableProperty]
    public partial string EditCity { get; set; } = string.Empty;

    /// <summary>Gets whether the question <em>Ta bort …?</em> is shown under the folded-out row.</summary>
    [ObservableProperty]
    public partial bool IsRemoving { get; private set; }

    /// <summary>Gets the question asked before the exit is removed.</summary>
    public string RemoveText =>
        $"Ta bort {Name}? WireGuard-filen tas bort ur valvet. Ärendena och det som hämtats genom den påverkas inte.";

    /// <summary>Gets where the exit's check stands.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck), nameof(IsChecking), nameof(HasCheckIcon), nameof(IsSeenAddress))]
    public partial ExitCheckState Check { get; private set; }

    /// <summary>Gets whether the trace is being asked (the spinner).</summary>
    public bool IsChecking => Check == ExitCheckState.Checking;

    /// <summary>Gets whether the column has an icon: a tick, a warning or a cut line.</summary>
    public bool HasCheckIcon => Check is ExitCheckState.Seen or ExitCheckState.Mismatch or ExitCheckState.Failed;

    /// <summary>Gets whether the column's text is an address, in the mono font.</summary>
    public bool IsSeenAddress => Check is ExitCheckState.Seen or ExitCheckState.Mismatch;

    /// <summary>Gets the column <em>Syns som</em>: the address, or what stands in its place.</summary>
    [ObservableProperty]
    public partial string SeenText { get; private set; } = ExitCheckWords.NotCheckedText;

    /// <summary>Gets the quiet words after it: the country and, for an earlier answer, the day.</summary>
    [ObservableProperty]
    public partial string SeenSubText { get; private set; } = string.Empty;

    /// <summary>Gets the column's tip.</summary>
    [ObservableProperty]
    public partial string SeenTip { get; private set; } = ExitCheckWords.NotCheckedTip;

    /// <summary>Gets the latest answer of the exit's check, or <see langword="null"/> when there is none.</summary>
    public ExitSighting? Sighting => _sighting;

    /// <summary>Gets the lines of the box <em>Kontroll</em>.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ExitFact> Facts { get; private set; } = ExitCheckWords.NotCheckedFacts();

    /// <summary>Shows the program's status for this row: its own state when the status is about it, else off.</summary>
    /// <param name="status">The program's exit status.</param>
    public void Show(ExitStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var mine = status.ExitId == Id;
        State = mine ? StateOf(status.State) : ExitRowState.Off;
        Word = State switch
        {
            ExitRowState.Connected => ConnectedText,
            ExitRowState.Connecting => ConnectingText,
            ExitRowState.Failed => ExitNames.FailureText(status),
            _ => string.Empty,
        };
        ShowCheck();
    }

    /// <summary>Takes the exit's new name and place after they are stored.</summary>
    /// <param name="exit">The exit as it is now.</param>
    public void Describe(ExitInfo exit)
    {
        ArgumentNullException.ThrowIfNull(exit);
        Name = exit.Name;
        Place = exit.Place;
        ShowCheck();
    }

    /// <summary>Folds the row out with its name, country and city in the fields, or in.</summary>
    /// <param name="expanded">Whether it is folded out.</param>
    public void Expand(bool expanded)
    {
        IsExpanded = expanded;
        IsRemoving = false;
        if (expanded)
        {
            EditName = Name;
            EditCountry = ExitPlace.CountryOf(Place);
            EditCity = ExitPlace.CityOf(Place);
        }
    }

    /// <summary>Shows or hides the question asked before the exit is removed.</summary>
    /// <param name="removing">Whether it is shown.</param>
    public void AskRemove(bool removing) => IsRemoving = removing && IsExpanded;

    /// <summary>Shows that the trace is being asked through the exit.</summary>
    public void StartChecking()
    {
        Check = ExitCheckState.Checking;
        ShowCheck();
    }

    /// <summary>Shows an answer of the check, kept or new.</summary>
    /// <param name="sighting">The answer, or <see langword="null"/> when the exit has none.</param>
    /// <param name="now">Now, so that today's answer says <em>i dag</em>.</param>
    public void ShowSighting(ExitSighting? sighting, DateTimeOffset now)
    {
        _sighting = sighting;
        _now = now;
        Check = sighting is null ? ExitCheckState.NotChecked : ExitCheckWords.StateOf(sighting, Place);
        ShowCheck();
    }

    /// <summary>Shows that the trace did not answer; a kept answer is not shown as the exit's now.</summary>
    public void ShowCheckFailed()
    {
        _sighting = null;
        Check = ExitCheckState.Failed;
        ShowCheck();
    }

    /// <summary>Writes the column and the box for the check as it stands.</summary>
    private void ShowCheck()
    {
        if (_sighting is { } sighting && Check is ExitCheckState.Seen or ExitCheckState.Mismatch)
        {
            Check = ExitCheckWords.StateOf(sighting, Place);
            ShowSeen(sighting);
            return;
        }

        (SeenText, SeenSubText, SeenTip, Facts) = Check switch
        {
            ExitCheckState.Checking => (ExitCheckWords.CheckingText, string.Empty, ExitCheckWords.AboutText,
                ExitCheckWords.CheckingFacts()),
            ExitCheckState.Failed => (ExitCheckWords.FailedText, ExitCheckWords.FailedSubText,
                ExitCheckWords.FailedTip, ExitCheckWords.FailedFacts()),
            _ => (ExitCheckWords.NotCheckedText, string.Empty, ExitCheckWords.NotCheckedTip,
                ExitCheckWords.NotCheckedFacts()),
        };
    }

    /// <summary>
    /// Writes an answer: the address, its country, and for an exit that is not connected now the day it was seen,
    /// since it may stand elsewhere today.
    /// </summary>
    /// <param name="sighting">The answer.</param>
    private void ShowSeen(ExitSighting sighting)
    {
        var country = ExitCheckWords.CountryText(sighting, Place);
        var day = State == ExitRowState.Connected ? string.Empty : ExitCheckWords.Day(sighting.CheckedAt, _now);
        SeenText = sighting.Address;
        SeenSubText = string.Join(" · ", new[] { country, day }.Where(part => part.Length > 0));
        SeenTip = $"Frågade 1.1.1.1 genom utgången {ExitCheckWords.When(sighting.CheckedAt, _now)}. "
            + "Skickar inget om ärendet; svaret sparas inte i ärendet.";
        Facts = ExitCheckWords.FactsFor(sighting, Place, _now);
    }

    /// <summary>Gives the row's state for the program's state.</summary>
    /// <param name="state">The program's state.</param>
    /// <returns>The row's state.</returns>
    private static ExitRowState StateOf(ExitState state) => state switch
    {
        ExitState.Connected => ExitRowState.Connected,
        ExitState.Connecting => ExitRowState.Connecting,
        ExitState.Failed => ExitRowState.Failed,
        _ => ExitRowState.Off,
    };
}
