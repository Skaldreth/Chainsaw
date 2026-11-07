using Chainsaw.App.Exits;
using Chainsaw.Modules.Network.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>An exit in the vault as a row of the panel (sketch UT1a): its dot, name, state and button.</summary>
/// <param name="exit">The exit, without its configuration.</param>
public sealed partial class ExitRowViewModel(ExitInfo exit) : ObservableObject
{
    /// <summary>What a connected row says under its name.</summary>
    public const string ConnectedText = "Ansluten";

    /// <summary>What a row being connected says under its name.</summary>
    public const string ConnectingText = "Ansluter …";

    /// <summary>The button's words for a row that is not connected.</summary>
    public const string ConnectText = "Anslut";

    /// <summary>The button's words for a row that is connected or being connected.</summary>
    public const string DisconnectText = "Koppla ner";

    /// <summary>Gets the exit's id.</summary>
    public Guid Id { get; } = exit.Id;

    /// <summary>Gets the name the user knows the exit by.</summary>
    public string Name { get; } = exit.Name;

    /// <summary>Gets where the exit stands.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn), nameof(ActionText), nameof(ActionName))]
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
            ExitRowState.Failed when status.Failure is { } failure => ExitNames.FailureText(failure),
            ExitRowState.Failed => ExitNames.FailureText(ExitFailure.ConnectFailed),
            _ => string.Empty,
        };
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
