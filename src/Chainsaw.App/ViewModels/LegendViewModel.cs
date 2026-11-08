using Chainsaw.App.Graph;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The legend behind the graph's <em>?</em> (sketch GR7a): the colours' rows with their sorts, the forms, the marks
/// and the lines from <see cref="Legend"/>, and the bottom row, which says the rule until something is hovered and
/// then what that is.
/// </summary>
public sealed partial class LegendViewModel : ObservableObject
{
    /// <summary>Gets the colours' rows, each with its sorts.</summary>
    public IReadOnlyList<LegendGroup> Groups => Legend.Groups;

    /// <summary>Gets the forms.</summary>
    public IReadOnlyList<LegendForm> Forms => Legend.Forms;

    /// <summary>Gets the marks.</summary>
    public IReadOnlyList<LegendBadge> Badges => Legend.Badges;

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<LegendLine> Lines => Legend.Lines;

    /// <summary>Gets what is hovered, or <see langword="null"/> when nothing is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint), nameof(IsHinting))]
    public partial ILegendItem? Hovered { get; private set; }

    /// <summary>Gets the bottom row: what the hovered thing means, or the rule.</summary>
    public string Hint => Legend.HintOf(Hovered);

    /// <summary>Gets whether the bottom row explains something, so that it is drawn bright rather than dim.</summary>
    public bool IsHinting => Hovered is not null;

    /// <summary>Says that the pointer came over something in the legend.</summary>
    /// <param name="item">What it came over.</param>
    public void Hover(ILegendItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Hovered = item;
    }

    /// <summary>Says that the pointer left something: the row says the rule, unless it is over another.</summary>
    /// <param name="item">What it left.</param>
    public void Leave(ILegendItem item)
    {
        if (ReferenceEquals(Hovered, item))
        {
            Hovered = null;
        }
    }

    /// <summary>Forgets what was hovered, when the legend closes, so that it opens with the rule.</summary>
    public void Reset() => Hovered = null;
}
