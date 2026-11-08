using Chainsaw.App.Graph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>One character of the comparison between a look-alike and the genuine address (sketch DM1a).</summary>
/// <param name="Genuine">The genuine address's character.</param>
/// <param name="Suspect">The look-alike's character.</param>
/// <param name="Differs">Whether they differ: the look-alike's lights red.</param>
public sealed record CharPair(string Genuine, string Suspect, bool Differs);

/// <summary>
/// The graph's part in the obfuscation it tones down or folds: dust and address poisoning as toned down nodes with
/// the poisoning's red mask at full strength and a comparison before the look-alike is copied (sketch DM1a), and many
/// hops folded into one line with a bead per hop (sketch HP1a). Only how the graph shows it; nothing is stored.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The nodes the dust and the poisonings tone down.</summary>
    private IReadOnlySet<string> _dusted = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The poisonings, by the look-alike's node.</summary>
    private IReadOnlyDictionary<string, Poisoning> _poisonings = new Dictionary<string, Poisoning>();

    /// <summary>The nodes folded away in the rows of many hops that are not unfolded.</summary>
    private HashSet<string> _foldedAway = new(StringComparer.Ordinal);

    /// <summary>The rows of many hops the user unfolded, by the finding's key.</summary>
    private readonly HashSet<string> _unfolded = new(StringComparer.Ordinal);

    /// <summary>Gets the rows of many hops in the graph.</summary>
    public IReadOnlyList<HopRow> HopRows { get; private set; } = [];

    /// <summary>
    /// Gets the poisoning of the chosen node, for the comparison in the inspector, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPoison), nameof(PoisonPairs), nameof(PoisonCopyText))]
    public partial Poisoning? Poison { get; private set; }

    /// <summary>Gets whether the chosen node is a likely poisoning.</summary>
    public bool HasPoison => Poison is not null;

    /// <summary>Gets whether copying the look-alike asked first, so that the comparison is lit.</summary>
    [ObservableProperty]
    public partial bool IsPoisonAsked { get; private set; }

    /// <summary>Gets the comparison character by character, the genuine address over the look-alike.</summary>
    public IReadOnlyList<CharPair> PoisonPairs => Poison is { } poison
        ? [.. ObfuscationMarks.Compare(poison.Genuine.Value, poison.Suspect.Value)
            .Select(pair => new CharPair(pair.Genuine.ToString(), pair.Suspect.ToString(), pair.Differs))]
        : [];

    /// <summary>Gets the default button's words, for example <c>Kopiera Börs B</c>.</summary>
    public string PoisonCopyText => Poison is { } poison ? "Kopiera " + poison.GenuineName : string.Empty;

    /// <summary>Says whether a node is toned down as dust or a look-alike (sketch DM1a).</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsDusted(string nodeKey) => _dusted.Contains(nodeKey);

    /// <summary>
    /// Gives a node's poisoning, so that it keeps its red mask at full strength, or <see langword="null"/>.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>The poisoning.</returns>
    public Poisoning? PoisoningOf(string nodeKey) => _poisonings.GetValueOrDefault(nodeKey);

    /// <summary>Says whether a node is folded away in a row of many hops (sketch HP1a).</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when it is not drawn.</returns>
    public bool IsFoldedAway(string nodeKey) => _foldedAway.Contains(nodeKey);

    /// <summary>Says whether a row of many hops is unfolded.</summary>
    /// <param name="rowKey">The row's key.</param>
    /// <returns><see langword="true"/> when its nodes show.</returns>
    public bool IsUnfolded(string rowKey) => _unfolded.Contains(rowKey);

    /// <summary>Folds a row of many hops out to its nodes, or back into one line (a click on its pill).</summary>
    /// <param name="rowKey">The row's key.</param>
    public void ToggleHopRow(string rowKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(rowKey);
        if (!_unfolded.Remove(rowKey))
        {
            _unfolded.Add(rowKey);
        }

        FoldHops();
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Copies the genuine address in place of the look-alike (Enter), the comparison's default.</summary>
    [RelayCommand]
    public void CopyGenuine()
    {
        if (Poison is { } poison)
        {
            CopyWanted?.Invoke(this, poison.Genuine.Value);
            IsPoisonAsked = false;
        }
    }

    /// <summary>Copies the look-alike after all, when that is what the user wants.</summary>
    [RelayCommand]
    public void CopySuspect()
    {
        if (Poison is { } poison)
        {
            CopyWanted?.Invoke(this, poison.Suspect.Value);
            IsPoisonAsked = false;
        }
    }

    /// <summary>
    /// Asks before a look-alike is copied: nothing goes to the clipboard, and the comparison is lit instead.
    /// </summary>
    /// <returns><see langword="true"/> when the chosen node is a look-alike and the copy waits for the choice.</returns>
    private bool AskBeforeCopy()
    {
        if (Poison is null)
        {
            return false;
        }

        IsPoisonAsked = true;
        return true;
    }

    /// <summary>Works out the dust, the poisonings and the rows of many hops from the scene and the checks.</summary>
    private void UpdateObfuscation()
    {
        if (Scene is not { } scene)
        {
            (_dusted, _poisonings, HopRows) = (new HashSet<string>(), new Dictionary<string, Poisoning>(), []);
            _foldedAway.Clear();
            return;
        }

        _dusted = ObfuscationMarks.Dusted(scene, Checks.Stored);
        _poisonings = ObfuscationMarks.Poisonings(scene, Checks.Stored);
        HopRows = ObfuscationMarks.HopRows(scene, Checks.Stored);
        FoldHops();
        FollowPoison();
    }

    /// <summary>Folds away the nodes between the first and last hop of every row that is not unfolded.</summary>
    private void FoldHops()
    {
        var kept = new HashSet<string>(StringComparer.Ordinal) { SelectedNode ?? string.Empty };
        _foldedAway = HopRows.Where(row => !_unfolded.Contains(row.Key))
            .SelectMany(row => row.Between)
            .Where(key => !kept.Contains(key))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Follows the chosen node's poisoning for the inspector.</summary>
    private void FollowPoison()
    {
        var poison = SelectedNode is { } key ? PoisoningOf(key) : null;
        if (poison != Poison)
        {
            IsPoisonAsked = false;
        }

        Poison = poison;
    }
}
