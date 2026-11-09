using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A likely service as a label (sketch TJ1b): the label form at the node, opened from the check
/// <c>service-behaviour</c>, with the check as the label's source (name and version) in a dashed badge, and the node
/// taking the label's name, colour and icon while the sort is chosen. The label is a judgement, sealed by Attribution
/// like the user's other labels (principle 2).
/// </summary>
public sealed partial class InspectorFormsViewModel
{
    /// <summary>The analysis the open label comes from, or <see langword="null"/> for the user's own label.</summary>
    private HeuristicId? _fromCheck;

    /// <summary>Raised when the label the node shows while it is chosen changes (sketch TJ1b).</summary>
    public event EventHandler? PreviewChanged;

    /// <summary>
    /// Gets whether the open label comes from a check: its source is fixed and the node shows it at once.
    /// </summary>
    public bool IsFromCheck => _fromCheck is not null && IsLabelOpen;

    /// <summary>
    /// Gets the check's badge in the form's heading, for example <c>Bedömning · service-behaviour v1</c>.
    /// </summary>
    [ObservableProperty]
    public partial string CheckSourceText { get; private set; } = string.Empty;

    /// <summary>
    /// Opens the label form at the node from a check that found a likely service, with the check as its source and
    /// an exchange as the sort chosen first.
    /// </summary>
    /// <param name="check">The stored check.</param>
    public void OpenLabelFromCheck(StoredCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!CanLabel)
        {
            return;
        }

        if (Form != InspectorForm.Label)
        {
            Open(InspectorForm.Label);
        }

        _fromCheck = check.Analysis;
        CheckSourceText = "Bedömning · " + check.Analysis;
        ChooseKind(Kinds.FirstOrDefault(kind => kind.Kind == LabelKind.Exchange));
        OnPropertyChanged(nameof(IsFromCheck));
        OnPropertyChanged(nameof(CanSubmit));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gives how the chosen node looks with the label being set from a check (sketch TJ1b): its name, the sort's
    /// colour and icon, dashed as a judgement; the node itself is the preview.
    /// </summary>
    /// <param name="node">The node being drawn.</param>
    /// <returns>The node as it would look, or the node itself.</returns>
    public NodeView Previewed(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!IsFromCheck || _node?.Key != node.Key || Kinds.FirstOrDefault(kind => kind.IsChosen) is not { } kind)
        {
            return node;
        }

        var name = LabelName.Trim().Length > 0 ? LabelName.Trim() : kind.Name;
        return node with { Look = kind.Option.Look with { IsDashed = true }, Name = name, IsNamed = true };
    }

    /// <summary>Gives a draft its check as the source, when the form came from a check.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The draft with its source.</returns>
    private LabelDraft FromCheck(LabelDraft draft) => _fromCheck is { } analysis
        ? draft with
        {
            Source = LabelSource.Behaviour,
            SourceName = analysis.Name,
            SourceVersion = "v" + analysis.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }
        : draft;

    /// <summary>Forgets the check, so that the next label is the user's own.</summary>
    private void ForgetCheck()
    {
        var was = _fromCheck is not null;
        _fromCheck = null;
        CheckSourceText = string.Empty;
        OnPropertyChanged(nameof(IsFromCheck));
        if (was)
        {
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Lets the node follow the name as it is typed.</summary>
    /// <param name="value">The new name.</param>
    partial void OnLabelNameChanged(string value)
    {
        if (_fromCheck is not null)
        {
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Lets the node follow the sort as it is chosen.</summary>
    /// <param name="value">The new sort.</param>
    partial void OnKindChanged(LabelKind value)
    {
        if (_fromCheck is not null)
        {
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
