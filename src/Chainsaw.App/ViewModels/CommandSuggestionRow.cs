using Chainsaw.App.Commands;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>A row in the list of suggestions under the command bar: the suggestion, and whether it is the chosen one.</summary>
/// <param name="suggestion">The suggestion.</param>
public sealed partial class CommandSuggestionRow(CommandSuggestion suggestion) : ObservableObject
{
    /// <summary>Gets the suggestion.</summary>
    public CommandSuggestion Suggestion { get; } = suggestion;

    /// <summary>Gets whether the row has what it does to say.</summary>
    public bool HasDetail => Suggestion.Detail.Length > 0;

    /// <summary>Gets or sets whether the row is the chosen one, which Tab fills in.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
