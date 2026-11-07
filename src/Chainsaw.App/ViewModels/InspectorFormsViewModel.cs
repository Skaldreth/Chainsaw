using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A sort in the label form's grid of icons (sketch IN1a, GR3c).</summary>
/// <param name="option">The sort.</param>
public sealed partial class KindChoice(LabelKindOption option) : ObservableObject
{
    /// <summary>Gets the sort.</summary>
    public LabelKindOption Option { get; } = option;

    /// <summary>Gets the label's kind.</summary>
    public LabelKind Kind => Option.Kind;

    /// <summary>Gets the sort's name, as the tooltip writes it.</summary>
    public string Name => Option.Kind.Describe();

    /// <summary>Gets whether it is chosen.</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }
}

/// <summary>A source in the label form (sketch IN1a, <em>Källa</em>).</summary>
/// <param name="basis">The source.</param>
public sealed partial class BasisChoice(LabelBasis basis) : ObservableObject
{
    /// <summary>Gets the source.</summary>
    public LabelBasis Basis { get; } = basis;

    /// <summary>Gets its name.</summary>
    public string Name => Basis.Describe();

    /// <summary>Gets whether it is chosen.</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }
}

/// <summary>An address in the form <em>Bryt upp</em>, with the heuristic that brought it into the cluster.</summary>
/// <param name="address">The address.</param>
/// <param name="heuristic">The heuristic's short name, or empty for the address the cluster was read from.</param>
/// <param name="toolTip">The address in full, the heuristic and the transaction it rests on.</param>
public sealed partial class MemberChoice(BitcoinAddress address, string heuristic, string toolTip) : ObservableObject
{
    /// <summary>Gets the address.</summary>
    public BitcoinAddress Address { get; } = address;

    /// <summary>Gets the address shortened for the row; the tooltip has it in full.</summary>
    public string ShortAddress { get; } = SubjectText.Fit(address.Value, 22);

    /// <summary>Gets the heuristic's short name.</summary>
    public string Heuristic { get; } = heuristic;

    /// <summary>Gets the tooltip.</summary>
    public string ToolTip { get; } = toolTip;

    /// <summary>Gets whether it is to be broken out.</summary>
    [ObservableProperty]
    public partial bool IsPicked { get; set; }
}

/// <summary>A chain in the form <em>Annan kedja</em>; one that is not built is toned and cannot be chosen.</summary>
/// <param name="option">The chain.</param>
public sealed partial class ChainChoice(ChainOption option) : ObservableObject
{
    /// <summary>Gets the chain.</summary>
    public ChainOption Option { get; } = option;

    /// <summary>Gets its name.</summary>
    public string Name => Option.Name;

    /// <summary>Gets whether a link to it can be made now.</summary>
    public bool IsBuilt => Option.IsBuilt;

    /// <summary>Gets the tooltip: the name, and that it is not built when it is not.</summary>
    public string ToolTip => IsBuilt ? Name : Name + " · " + InspectorFormsViewModel.NotBuiltText;

    /// <summary>Gets whether it is chosen.</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }
}

/// <summary>A coin in the form <em>Annan kedja</em>, when more than one came into the actor.</summary>
/// <param name="coin">The coin.</param>
public sealed partial class CoinChoice(IncomingCoin coin) : ObservableObject
{
    /// <summary>Gets the coin.</summary>
    public IncomingCoin Coin { get; } = coin;

    /// <summary>Gets the row's text: the amount and the transaction it came in.</summary>
    public string Text { get; } =
        $"{AmountText.WithUnit(coin.Amount)} · tx {GraphScene.Shorten(coin.TransactionId)}";

    /// <summary>Gets the coin in full and when it came.</summary>
    public string ToolTip { get; } = coin.Time is { } time
        ? $"{coin.Written} · {InspectorContent.Time(time)}"
        : $"{coin.Written} · obekräftad";

    /// <summary>Gets whether it is chosen.</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }
}

/// <summary>
/// The forms in the inspector (sketch IN1a): <em>Etikett</em> (L), <em>Bryt upp</em> (U, only on a cluster) and
/// <em>Annan kedja</em> (K), folded out under the chosen node's heading. What they store is written by Attribution,
/// Clustering and Tracing, sealed with a line in the journal (principle 1), and the graph is then read again from the
/// case. Esc cancels and Enter stores. It knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class InspectorFormsViewModel : ObservableObject
{
    /// <summary>What a chain or a button says when it is not built yet.</summary>
    public const string NotBuiltText = "Inte byggt än";

    /// <summary>What <em>Bryt upp</em> says on anything but a cluster.</summary>
    public const string OnlyClusterText = "Bara på ett kluster";

    /// <summary>What <em>Etikett</em> says on a transaction, a coin or another chain's transaction.</summary>
    public const string OnlyActorText = "Bara på en adress eller ett kluster";

    /// <summary>What <em>Annan kedja</em> says when no coin came into the actor that is not already linked.</summary>
    public const string NoCoinText = "Inga mynt in som inte redan är knutna";

    /// <summary>What the form says while the cluster's addresses are read.</summary>
    public const string ReadingMembersText = "Läser klustrets adresser …";

    /// <summary>What the form says when the cluster could not be read.</summary>
    public const string MembersFailedText = "Klustrets adresser kunde inte läsas ur ärendet.";

    /// <summary>What the form says when the splits were stored but the cluster could not be read to check them.</summary>
    public const string UncheckedText =
        "Uppbrytningarna är sparade, men klustret kunde inte läsas för att kontrollera dem.";

    /// <summary>What the form says when a picked address is still in the cluster after the splits.</summary>
    public const string StillInClusterText =
        "Uppbrytningarna är sparade, men en annan länk håller kvar dem; öppna Bryt upp igen.";

    /// <summary>What the form says when the case would not store what was asked.</summary>
    public const string SaveFailedText = "Det kunde inte sparas i ärendet";

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The modules' writers.</summary>
    private readonly IGraphEditor _editor;

    /// <summary>The clock, for the date an open source was read.</summary>
    private readonly IClock _clock;

    /// <summary>The scene the chosen node is in.</summary>
    private GraphScene? _scene;

    /// <summary>The chosen node, or <see langword="null"/>.</summary>
    private NodeView? _node;

    /// <summary>The cluster read for <em>Bryt upp</em>, or <see langword="null"/>.</summary>
    private Cluster? _cluster;

    /// <summary>Counts the forms opened, so that a cluster read for an earlier one is dropped.</summary>
    private int _opened;

    /// <summary>
    /// Whether the form's error tells of something that was stored (a split's warning), and so moves to the graph's
    /// notice when the form closes; a plain refusal the user has read does not.
    /// </summary>
    private bool _errorOutlivesForm;

    /// <summary>Counts the readings of the cluster's addresses, so that only the latest is shown.</summary>
    private int _memberReads;

    /// <summary>Creates the forms.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="editor">The modules' writers.</param>
    /// <param name="clock">The clock.</param>
    public InspectorFormsViewModel(CaseSession session, IGraphEditor editor, IClock clock)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Kinds = [.. InspectorForms.Kinds.Select(option => new KindChoice(option))];
        Bases = [.. InspectorForms.Bases.Select(basis => new BasisChoice(basis))];
        Chains = [.. InspectorForms.Chains.Select(option => new ChainChoice(option))];
    }

    /// <summary>Raised when something was stored, so that the graph is read again from the case.</summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Raised with what went wrong when a store failed after its form was closed or replaced, so that the graph's
    /// notice says it instead of the failure vanishing with the form.
    /// </summary>
    public event EventHandler<string>? Failed;

    /// <summary>Gets the open form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(IsLabelOpen), nameof(IsSplitOpen), nameof(IsCrossChainOpen))]
    [NotifyPropertyChangedFor(nameof(SubmitText), nameof(CanSubmit))]
    public partial InspectorForm Form { get; private set; }

    /// <summary>Gets whether a form is open, so that the inspector's facts give way to it.</summary>
    public bool IsOpen => Form != InspectorForm.None;

    /// <summary>Gets whether the label form is open.</summary>
    public bool IsLabelOpen => Form == InspectorForm.Label;

    /// <summary>Gets whether the form <em>Bryt upp</em> is open.</summary>
    public bool IsSplitOpen => Form == InspectorForm.Split;

    /// <summary>Gets whether the form <em>Annan kedja</em> is open.</summary>
    public bool IsCrossChainOpen => Form == InspectorForm.CrossChain;

    /// <summary>Gets whether a node is chosen, so that the three buttons show.</summary>
    [ObservableProperty]
    public partial bool HasNode { get; private set; }

    /// <summary>Gets whether the chosen node can carry a label.</summary>
    [ObservableProperty]
    public partial bool CanLabel { get; private set; }

    /// <summary>Gets whether the chosen node is a cluster that can be broken up.</summary>
    [ObservableProperty]
    public partial bool CanSplit { get; private set; }

    /// <summary>Gets whether a coin came into the chosen node that can be linked to another chain.</summary>
    [ObservableProperty]
    public partial bool CanCrossChain { get; private set; }

    /// <summary>Gets the label button's tooltip.</summary>
    public string LabelToolTip => CanLabel ? "Etikett (L)" : OnlyActorText;

    /// <summary>Gets the split button's tooltip.</summary>
    public string SplitToolTip => CanSplit ? "Bryt upp (U)" : OnlyClusterText;

    /// <summary>Gets the other chain button's tooltip.</summary>
    public string CrossChainToolTip => CanCrossChain ? "Annan kedja (K)" : NoCoinText;

    /// <summary>Gets the sorts of the label form.</summary>
    public IReadOnlyList<KindChoice> Kinds { get; }

    /// <summary>Gets the sources of the label form.</summary>
    public IReadOnlyList<BasisChoice> Bases { get; }

    /// <summary>Gets the chains of the form <em>Annan kedja</em>.</summary>
    public IReadOnlyList<ChainChoice> Chains { get; }

    /// <summary>Gets the cluster's addresses in the form <em>Bryt upp</em>.</summary>
    public ObservableCollection<MemberChoice> Members { get; } = [];

    /// <summary>Gets the coins that can be linked, when more than one came in.</summary>
    public ObservableCollection<CoinChoice> Coins { get; } = [];

    /// <summary>Gets or sets the label's name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string LabelName { get; set; } = string.Empty;

    /// <summary>Gets the chosen sort.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindName), nameof(IsSpreadOptional))]
    public partial LabelKind Kind { get; private set; } = LabelKind.Unknown;

    /// <summary>
    /// Gets whether <em>Sprid inte</em> means anything for the chosen sort: a victim's label never spreads, whatever
    /// the switch says (Attribution).
    /// </summary>
    public bool IsSpreadOptional => Kind != LabelKind.Victim;

    /// <summary>Gets the chosen sort's name, as the form writes it beside the grid.</summary>
    public string KindName => Kind.Describe();

    /// <summary>Gets the chosen source.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenSource), nameof(CanSubmit))]
    public partial LabelBasis Basis { get; private set; } = LabelBasis.Own;

    /// <summary>Gets whether the label rests on an open source, which is then named.</summary>
    public bool IsOpenSource => Basis == LabelBasis.OpenSource;

    /// <summary>Gets or sets the open source's name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string SourceName { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the label stays in this case (<em>Sprid inte</em>).</summary>
    [ObservableProperty]
    public partial bool DoNotSpread { get; set; }

    /// <summary>Gets or sets the reason; every form asks for one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string Reason { get; set; } = string.Empty;

    /// <summary>Gets what the form <em>Bryt upp</em> says over its rows: how many of how many leave.</summary>
    [ObservableProperty]
    public partial string PickedText { get; private set; } = string.Empty;

    /// <summary>Gets what the form says while the cluster is read, or when it could not be; empty otherwise.</summary>
    [ObservableProperty]
    public partial string MembersNotice { get; private set; } = string.Empty;

    /// <summary>Gets the chosen chain.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxLabel))]
    public partial Chain TargetChain { get; private set; } = Chain.Ethereum;

    /// <summary>Gets the transaction field's heading, for example <c>Ethereum-tx</c>.</summary>
    public string TxLabel => InspectorForms.Chains.First(option => option.Chain == TargetChain).Name + "-tx";

    /// <summary>Gets or sets the transaction on the other chain, as typed or pasted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTxRecognised), nameof(CanSubmit))]
    public partial string TxText { get; set; } = string.Empty;

    /// <summary>Gets the transaction, recognised as it is typed, or <see langword="null"/>.</summary>
    public ChainTransactionRef? RecognisedTx => InspectorForms.Recognise(TxText, TargetChain);

    /// <summary>Gets whether the transaction is recognised, which the field shows with a check.</summary>
    public bool IsTxRecognised => RecognisedTx is not null;

    /// <summary>Gets whether the coins are listed to choose from: when more than one came in.</summary>
    public bool ShowsCoins => Coins.Count > 1;

    /// <summary>Gets whether something is being stored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsSaving { get; private set; }

    /// <summary>Gets what went wrong the last time, in Swedish; empty when nothing did.</summary>
    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the store button's words (sketch IN1a): <c>Sätt etikett</c>, <c>Bryt ut 2</c> or <c>Knyt</c>.
    /// </summary>
    public string SubmitText => Form switch
    {
        InspectorForm.Label => "Sätt etikett",
        InspectorForm.Split => "Bryt ut " + Picked().Count.ToString(CultureInfo.InvariantCulture),
        InspectorForm.CrossChain => "Knyt",
        _ => string.Empty,
    };

    /// <summary>Gets whether the open form has what it needs to be stored.</summary>
    public bool CanSubmit => !IsSaving && _session.Current is not null && Form switch
    {
        InspectorForm.Label => LabelDraft() is not null,
        InspectorForm.Split => SplitRequests().Count > 0,
        InspectorForm.CrossChain => ChosenCoin() is { } coin && RecognisedTx is { } target
            && target.Chain != coin.Chain && HasReason(Reason),
        _ => false,
    };

    /// <summary>
    /// Follows what the inspector shows: a new node closes the open form, and the same node read again keeps it, with
    /// the new graph's coins.
    /// </summary>
    /// <param name="scene">The scene, or <see langword="null"/>.</param>
    /// <param name="node">The chosen node, or <see langword="null"/> when nothing or a line is chosen.</param>
    public void Follow(GraphScene? scene, NodeView? node)
    {
        var same = node is not null && _node?.Key == node.Key;
        (_scene, _node) = (scene, node);
        HasNode = node is not null;
        CanLabel = node is not null && InspectorForms.LabelSubjectOf(node) is not null;
        CanSplit = node is not null && InspectorForms.CanSplit(node);
        CanCrossChain = scene is not null && node is not null && InspectorForms.IncomingCoins(scene, node).Count > 0;
        OnPropertyChanged(nameof(LabelToolTip));
        OnPropertyChanged(nameof(SplitToolTip));
        OnPropertyChanged(nameof(CrossChainToolTip));
        if (!same || !IsAllowed(Form))
        {
            // What the closing form had to say moves to the graph's notice instead of vanishing with it.
            if (IsOpen && Error.Length > 0 && _errorOutlivesForm)
            {
                Failed?.Invoke(this, Error);
            }

            Close();
        }
        else if (Form == InspectorForm.CrossChain)
        {
            FillCoins();
        }
        else if (Form == InspectorForm.Split && !IsSaving)
        {
            _ = ReadMembersAsync(_opened);
        }
    }

    /// <summary>Opens a form for the chosen node, or closes it when it is already open (the same button again).</summary>
    /// <param name="form">The form.</param>
    [RelayCommand]
    public void Open(InspectorForm form)
    {
        if (form == Form)
        {
            Close();
            return;
        }

        if (form == InspectorForm.None || !IsAllowed(form))
        {
            return;
        }

        Clear();
        _opened++;
        Form = form;
        switch (form)
        {
            case InspectorForm.Split:
                _ = ReadMembersAsync(_opened);
                break;
            case InspectorForm.CrossChain:
                FillCoins();
                break;
            default:
                break;
        }
    }

    /// <summary>Closes the open form without storing anything (Esc).</summary>
    [RelayCommand]
    public void Cancel() => Close();

    /// <summary>Chooses a sort in the label form.</summary>
    /// <param name="choice">The sort.</param>
    [RelayCommand]
    public void ChooseKind(KindChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        Kind = choice.Kind;
        foreach (var kind in Kinds)
        {
            kind.IsChosen = kind == choice;
        }
    }

    /// <summary>Chooses a source in the label form.</summary>
    /// <param name="choice">The source.</param>
    [RelayCommand]
    public void ChooseBasis(BasisChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        Basis = choice.Basis;
        foreach (var basis in Bases)
        {
            basis.IsChosen = basis == choice;
        }
    }

    /// <summary>Picks an address to break out, or puts it back.</summary>
    /// <param name="member">The address.</param>
    [RelayCommand]
    public void ToggleMember(MemberChoice? member)
    {
        if (member is null)
        {
            return;
        }

        member.IsPicked = !member.IsPicked;
        UpdatePicked();
    }

    /// <summary>Chooses the other chain; one that is not built cannot be chosen.</summary>
    /// <param name="choice">The chain.</param>
    [RelayCommand]
    public void ChooseChain(ChainChoice? choice)
    {
        if (choice?.Option.Chain is not { } chain)
        {
            return;
        }

        SelectChain(chain);
    }

    /// <summary>Chooses which coin went into the bridge or swap.</summary>
    /// <param name="choice">The coin.</param>
    [RelayCommand]
    public void ChooseCoin(CoinChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        foreach (var coin in Coins)
        {
            coin.IsChosen = coin == choice;
        }

        OnPropertyChanged(nameof(CanSubmit));
    }

    /// <summary>
    /// Stores what the open form says (Enter), through the module that owns it, off the UI thread; then closes the
    /// form and asks for the graph to be read again. What the case refuses is said in the form, which stays open.
    /// </summary>
    /// <returns>A task that completes when it is stored or could not be.</returns>
    [RelayCommand]
    public async Task SubmitAsync()
    {
        if (!CanSubmit || _session.Current is not { } openCase)
        {
            return;
        }

        var form = Form;
        var opened = _opened;
        IsSaving = true;
        Error = string.Empty;
        try
        {
            var problem = form switch
            {
                InspectorForm.Label => await StoreLabelAsync(openCase),
                InspectorForm.Split => await StoreSplitsAsync(openCase),
                _ => await StoreLinkAsync(openCase),
            };
            if (problem is not null)
            {
                // An empty problem is a form that was not complete after all; nothing was stored or is to be said.
                if (problem.Length > 0)
                {
                    Say(problem, opened);
                    _errorOutlivesForm = form == InspectorForm.Split && opened == _opened;
                }
            }
            else if (_session.Current == openCase && Form == form && opened == _opened)
            {
                Close();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Say(Failure(exception), opened);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Gives what to say when the case would not store something.</summary>
    /// <param name="exception">What went wrong.</param>
    /// <returns>The text, in Swedish.</returns>
    public static string Failure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var why = exception switch
        {
            CaseException { Error: CaseError.NoOperatorProfile } => "operatörsprofilen saknas, och journalen behöver den",
            CaseException { Error: CaseError.Closed } => "ärendet är stängt",
            ArgumentException => "uppgifterna godtogs inte (" + exception.Message + ")",
            _ => exception.Message,
        };
        return $"{SaveFailedText}: {why}.";
    }

    /// <summary>
    /// Says what went wrong: in the form while it is the same one, otherwise in the graph's notice, which outlives it.
    /// </summary>
    /// <param name="text">What to say.</param>
    /// <param name="opened">The form's number when the store began.</param>
    private void Say(string text, int opened)
    {
        if (opened == _opened)
        {
            Error = text;
        }
        else
        {
            Failed?.Invoke(this, text);
        }
    }

    /// <summary>Sets the label, sealed with a line in the journal.</summary>
    /// <param name="openCase">The open case.</param>
    /// <returns>What to say when it was not stored, or <see langword="null"/> when it was.</returns>
    private async Task<string?> StoreLabelAsync(ICase openCase)
    {
        if (LabelDraft() is not { } draft)
        {
            return string.Empty;
        }

        await _editor.AddLabelAsync(openCase, draft);
        Saved?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Stores the splits that break the picked addresses out, as one breaking out, all or none.</summary>
    /// <param name="openCase">The open case.</param>
    /// <returns>What to say, or <see langword="null"/> when the picked addresses were broken out.</returns>
    private async Task<string?> StoreSplitsAsync(ICase openCase)
    {
        if (_cluster is not { } shown || !HasReason(Reason))
        {
            return string.Empty;
        }

        // The cluster is read again just before, so that links that came since the rows were read are split too.
        var picked = Picked();
        var fresh = await _editor.ReadClusterAsync(openCase, shown.Start, CancellationToken.None);
        var requests = InspectorForms.BreakOut(fresh, [.. picked.Where(fresh.Contains)], Reason);
        var outcome = await _editor.SplitAsync(openCase, requests);
        var problem = outcome.Failure is { } failure
            ? Failure(failure)
            : await CheckBrokenOutAsync(openCase, shown.Start, picked);

        // The graph is read again only after the check, so that the form it may close has said what it found.
        if (outcome.Stored > 0)
        {
            Saved?.Invoke(this, EventArgs.Empty);
        }

        return problem;
    }

    /// <summary>
    /// Checks against the case, never assumes, that the picked addresses have left the cluster: one still in it is
    /// said, not hidden.
    /// </summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="start">The address the cluster was read from.</param>
    /// <param name="picked">The picked addresses.</param>
    /// <returns>What to say, or <see langword="null"/> when they all left.</returns>
    private async Task<string?> CheckBrokenOutAsync(ICase openCase, BitcoinAddress start, List<BitcoinAddress> picked)
    {
        Cluster after;
        try
        {
            after = await _editor.ReadClusterAsync(openCase, start, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return UncheckedText;
        }

        var staying = picked.Count(after.Contains);
        return staying == 0
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{staying} av de valda adresserna är fortfarande i klustret. {StillInClusterText}");
    }

    /// <summary>Links the chosen coin to the transaction on the other chain, sealed with a line in the journal.</summary>
    /// <param name="openCase">The open case.</param>
    /// <returns>What to say when it was not stored, or <see langword="null"/> when it was.</returns>
    private async Task<string?> StoreLinkAsync(ICase openCase)
    {
        if (ChosenCoin() is not { } coin || RecognisedTx is not { } target)
        {
            return string.Empty;
        }

        if (coin.Movement is { } movement)
        {
            await _editor.LinkCrossChainAsync(openCase, movement, target, Reason.Trim());
        }
        else
        {
            await _editor.LinkCrossChainAsync(openCase, coin.Coin!, target, Reason.Trim());
        }

        Saved?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Gives the label the form says, or <see langword="null"/> while something it needs is missing.</summary>
    /// <returns>The draft.</returns>
    private LabelDraft? LabelDraft()
    {
        if (_node is null || InspectorForms.LabelSubjectOf(_node) is not { } subject || !HasReason(Reason)
            || LabelName.Trim().Length > InspectorForms.MostNameLength
            || (IsOpenSource && (string.IsNullOrWhiteSpace(SourceName)
                || SourceName.Trim().Length > InspectorForms.MostNameLength)))
        {
            return null;
        }

        var draft = InspectorForms.Draft(
            subject,
            Kind,
            LabelName,
            Basis,
            SourceName,
            Reason,
            DoNotSpread,
            DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime().DateTime));
        return draft.Reason.Length <= InspectorForms.MostReasonLength ? draft : null;
    }

    /// <summary>Gives the splits the form asks for; empty while something it needs is missing.</summary>
    /// <returns>The splits.</returns>
    private IReadOnlyList<SplitRequest> SplitRequests()
    {
        var picked = Picked();
        if (_cluster is null || picked.Count == 0 || picked.Count == Members.Count || !HasReason(Reason))
        {
            return [];
        }

        return InspectorForms.BreakOut(_cluster, picked, Reason);
    }

    /// <summary>Gives the picked addresses.</summary>
    /// <returns>The addresses.</returns>
    private List<BitcoinAddress> Picked() => [.. Members.Where(member => member.IsPicked).Select(m => m.Address)];

    /// <summary>Gives the coin to link: the one chosen, or the only one that came in.</summary>
    /// <returns>The coin, or <see langword="null"/>.</returns>
    private IncomingCoin? ChosenCoin() => Coins.Count == 1
        ? Coins[0].Coin
        : Coins.FirstOrDefault(coin => coin.IsChosen)?.Coin;

    /// <summary>Says whether a reason is given and not too long.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool HasReason(string reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= InspectorForms.MostReasonLength;

    /// <summary>Says whether a form can be opened for the chosen node.</summary>
    /// <param name="form">The form.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    private bool IsAllowed(InspectorForm form) => form switch
    {
        InspectorForm.Label => CanLabel,
        InspectorForm.Split => CanSplit,
        InspectorForm.CrossChain => CanCrossChain,
        _ => true,
    };

    /// <summary>Closes the open form and forgets what was typed.</summary>
    private void Close()
    {
        _opened++;
        Form = InspectorForm.None;
        Clear();
    }

    /// <summary>Starts every field over, with the sketch's defaults: unknown, own judgement, Ethereum.</summary>
    private void Clear()
    {
        LabelName = string.Empty;
        ChooseKind(Kinds.First(kind => kind.Kind == LabelKind.Unknown));
        ChooseBasis(Bases[0]);
        SourceName = string.Empty;
        DoNotSpread = false;
        Reason = string.Empty;
        _cluster = null;
        Members.Clear();
        MembersNotice = string.Empty;
        UpdatePicked();
        SelectChain(Chain.Ethereum);
        TxText = string.Empty;
        Coins.Clear();
        OnPropertyChanged(nameof(ShowsCoins));
        Error = string.Empty;
        _errorOutlivesForm = false;
    }

    /// <summary>Chooses a chain and marks it.</summary>
    /// <param name="chain">The chain.</param>
    private void SelectChain(Chain chain)
    {
        TargetChain = chain;
        foreach (var choice in Chains)
        {
            choice.IsChosen = choice.Option.Chain == chain;
        }

        OnPropertyChanged(nameof(RecognisedTx));
        OnPropertyChanged(nameof(IsTxRecognised));
        OnPropertyChanged(nameof(CanSubmit));
    }

    /// <summary>Recognises a pasted transaction's chain at once, so that the chosen chain follows it.</summary>
    /// <param name="value">The new text.</param>
    partial void OnTxTextChanged(string value)
    {
        if (InspectorForms.Recognise(value, TargetChain) is { } found && found.Chain != TargetChain)
        {
            SelectChain(found.Chain);
        }

        OnPropertyChanged(nameof(RecognisedTx));
    }

    /// <summary>Lists the coins that came into the chosen node and can be linked, keeping the one chosen.</summary>
    private void FillCoins()
    {
        var chosen = Coins.FirstOrDefault(coin => coin.IsChosen)?.Coin;
        Coins.Clear();
        if (_scene is { } scene && _node is { } node)
        {
            foreach (var coin in InspectorForms.IncomingCoins(scene, node))
            {
                Coins.Add(new CoinChoice(coin) { IsChosen = coin == chosen });
            }

            if (node.Node.Chain == TargetChain)
            {
                // A link leads off the chain the money is on; from Ethereum, TRON is the first other built chain.
                SelectChain(InspectorForms.Chains.First(option => option.Chain is { } chain && chain != node.Node.Chain)
                    .Chain!.Value);
            }
        }

        OnPropertyChanged(nameof(ShowsCoins));
        OnPropertyChanged(nameof(CanSubmit));
    }

    /// <summary>
    /// Reads the chosen cluster's addresses off the UI thread, each with the heuristic that brought it in.
    /// </summary>
    /// <param name="opened">The form's number, so that an answer for an earlier form is dropped.</param>
    /// <returns>A task that completes when the rows are shown or could not be read.</returns>
    private async Task ReadMembersAsync(int opened)
    {
        if (_session.Current is not { } openCase || _node?.Node.GraphAddresses is not [var first, ..])
        {
            return;
        }

        var read = ++_memberReads;
        if (Members.Count == 0)
        {
            MembersNotice = ReadingMembersText;
        }
        try
        {
            var cluster = await _editor.ReadClusterAsync(openCase, first, CancellationToken.None);
            if (opened != _opened || read != _memberReads)
            {
                return;
            }

            // A cluster read again keeps the picks of the addresses still in it.
            var picked = Picked().ToHashSet();
            _cluster = cluster;
            Members.Clear();
            foreach (var member in cluster.Members)
            {
                var row = RowOf(member);
                row.IsPicked = picked.Contains(member.Address);
                Members.Add(row);
            }

            MembersNotice = string.Empty;
            UpdatePicked();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (opened == _opened && read == _memberReads)
            {
                MembersNotice = MembersFailedText;
            }
        }
    }

    /// <summary>Gives a member's row: its heuristic and the transaction it rests on.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The row.</returns>
    private static MemberChoice RowOf(ClusterMember member)
    {
        if (member.JoinedBy?.Evidence is not { } evidence)
        {
            return new MemberChoice(member.Address, string.Empty, member.Address.Value);
        }

        var tip = $"{member.Address.Value} · {evidence.Heuristic} i tx {GraphScene.Shorten(evidence.Txid.Value)}";
        return new MemberChoice(member.Address, InspectorForms.ShortName(evidence.Heuristic), tip);
    }

    /// <summary>Counts the picked addresses for the heading and the button.</summary>
    private void UpdatePicked()
    {
        PickedText = Members.Count == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{Picked().Count} av {Members.Count} ut");
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(CanSubmit));
    }
}
