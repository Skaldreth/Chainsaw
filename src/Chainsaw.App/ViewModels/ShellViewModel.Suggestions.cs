using Chainsaw.App.Commands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The command bar's suggestions as the text is written (auto-suggest, the user's wish 2026-11-09): under a text with a
/// slash, the commands it can become and then their arguments, each with what it does. The arrows choose, Tab fills
/// in, and Enter fills in the row the arrows chose. A text without a slash is the search's (sketch KR1a).
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>The most suggestions shown at once, so that the list never pushes the bar off the screen.</summary>
    public const int MostSuggestions = 8;

    /// <summary>Whether the arrows have chosen a row, so that Enter fills it in rather than running the text.</summary>
    private bool _suggestionChosen;

    /// <summary>Gets the suggestions for what is written, the chosen one marked; empty when there are none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    public partial IReadOnlyList<CommandSuggestionRow> Suggestions { get; private set; } = [];

    /// <summary>Gets whether there are suggestions to show under the bar.</summary>
    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>
    /// Moves the chosen suggestion up or down, round at the ends (the arrows).
    /// </summary>
    /// <param name="step">One row down, or −1 for one up.</param>
    /// <returns>Whether there were suggestions to move through; otherwise the arrows go to the search.</returns>
    public bool MoveSuggestion(int step)
    {
        if (!HasSuggestions)
        {
            return false;
        }

        var at = Suggestions.ToList().FindIndex(row => row.IsSelected);
        var next = ((at + step) % Suggestions.Count + Suggestions.Count) % Suggestions.Count;
        Choose(next);
        _suggestionChosen = true;
        return true;
    }

    /// <summary>Takes a suggestion: the bar gets its whole text, ready for what comes after it.</summary>
    /// <param name="row">The row, from a click.</param>
    [RelayCommand]
    public void TakeSuggestion(CommandSuggestionRow? row)
    {
        if (row is null)
        {
            return;
        }

        _completions = [.. Suggestions.Select(each => each.Suggestion.Completion)];
        _completionIndex = Suggestions.ToList().IndexOf(row);
        _completed = row.Suggestion.Completion;
        CommandText = row.Suggestion.Completion;
    }

    /// <summary>Gives the row Tab and Enter take: the one chosen, or else the first.</summary>
    private CommandSuggestionRow? ChosenSuggestion =>
        Suggestions.FirstOrDefault(row => row.IsSelected) ?? Suggestions.FirstOrDefault();

    /// <summary>
    /// Takes the row the arrows chose when Enter is pressed, rather than running what is written.
    /// </summary>
    /// <returns>Whether a row was taken.</returns>
    public bool TakeChosenOnEnter()
    {
        if (!_suggestionChosen || ChosenSuggestion is not { } row)
        {
            return false;
        }

        TakeSuggestion(row);
        return true;
    }

    /// <summary>
    /// Shows the suggestions for the new text: none that is the text already, at most <see cref="MostSuggestions"/>,
    /// the first chosen.
    /// </summary>
    /// <param name="text">What is written.</param>
    private void ShowSuggestions(string text)
    {
        var written = text.Trim();
        Suggestions =
        [
            .. _commands.Suggestions(text)
                .Where(suggestion => !string.Equals(suggestion.Completion, written, StringComparison.OrdinalIgnoreCase))
                .Take(MostSuggestions)
                .Select(suggestion => new CommandSuggestionRow(suggestion)),
        ];
        _suggestionChosen = false;
        Choose(0);
    }

    /// <summary>Marks one row as chosen and the others not.</summary>
    /// <param name="index">The row's place.</param>
    private void Choose(int index)
    {
        for (var at = 0; at < Suggestions.Count; at++)
        {
            Suggestions[at].IsSelected = at == index;
        }
    }
}
