using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The trash in the lower left corner (sketch SO1a): a small round icon that grows into a round area as something
/// dragged comes near, and is <em>hot</em>, with a ring wave, when a release there throws. The view tells it how far the
/// pointer is from the bin's centre; everything else is worked out here, without WPF.
/// </summary>
public sealed partial class TrashBinViewModel : ObservableObject
{
    /// <summary>The bin's radius at rest, in pixels (a 38 px round button, like the pill's).</summary>
    public const double RestRadius = 19;

    /// <summary>How much the radius grows when the pointer is at the bin.</summary>
    public const double Growth = 100;

    /// <summary>How far from the centre the bin is fully grown.</summary>
    public const double FullAt = 110;

    /// <summary>Over how many pixels beyond <see cref="FullAt"/> it shrinks back to rest.</summary>
    public const double Reach = 320;

    /// <summary>How near the pointer must be, from 0 to 1, before a release can throw.</summary>
    public const double HotNearness = 0.6;

    /// <summary>How far outside the grown area a release still throws, so that the edge is forgiving.</summary>
    public const double HotSlack = 8;

    /// <summary>The icon's size at rest.</summary>
    private const double RestIcon = 20;

    /// <summary>How much the icon grows.</summary>
    private const double IconGrowth = 10;

    /// <summary>What the tooltip says.</summary>
    public const string ToolTipText = "Soptunnan · dra hit för att slänga, klicka för att öppna";

    /// <summary>Gets how near the dragged thing is, from 0 (far) to 1 (at the bin).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Radius), nameof(Diameter), nameof(IconSize), nameof(IsGrowing))]
    public partial double Nearness { get; private set; }

    /// <summary>Gets whether a release now throws: the area is bright and the ring wave goes.</summary>
    [ObservableProperty]
    public partial bool IsHot { get; private set; }

    /// <summary>Gets whether something is dragged that could be thrown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGrowing))]
    public partial bool IsDragging { get; private set; }

    /// <summary>Gets whether anything lies in the trash, shown as a small dot on the icon.</summary>
    [ObservableProperty]
    public partial bool HasThrows { get; set; }

    /// <summary>Gets whether the trash's view is open, so that the icon is shown chosen.</summary>
    [ObservableProperty]
    public partial bool IsViewOpen { get; set; }

    /// <summary>Gets whether the bin grows: something is dragged and near.</summary>
    public bool IsGrowing => IsDragging && Nearness > 0;

    /// <summary>Gets the bin's radius now.</summary>
    public double Radius => RestRadius + (Growth * EaseOut(Nearness));

    /// <summary>Gets the bin's diameter now.</summary>
    public double Diameter => 2 * Radius;

    /// <summary>Gets the icon's size now.</summary>
    public double IconSize => RestIcon + (IconGrowth * EaseOut(Nearness));

    /// <summary>Notes where the pointer is while something is dragged.</summary>
    /// <param name="distance">The pointer's distance from the bin's centre, in pixels.</param>
    public void DragAt(double distance)
    {
        IsDragging = true;
        Nearness = NearnessAt(distance);
        IsHot = Nearness > HotNearness && distance < Radius + HotSlack;
    }

    /// <summary>Ends a drag: the bin shrinks back.</summary>
    /// <returns>Whether the release was on the bin, so that what was dragged is thrown.</returns>
    public bool EndDrag()
    {
        var hot = IsHot;
        IsDragging = false;
        IsHot = false;
        Nearness = 0;
        return hot;
    }

    /// <summary>Gives how near a distance is, from 0 to 1.</summary>
    /// <param name="distance">The distance from the centre.</param>
    /// <returns>The nearness.</returns>
    public static double NearnessAt(double distance) => Math.Clamp(1 - ((distance - FullAt) / Reach), 0, 1);

    /// <summary>Eases a growth out, fast first and slow at the end.</summary>
    /// <param name="t">From 0 to 1.</param>
    /// <returns>The eased value.</returns>
    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
}
