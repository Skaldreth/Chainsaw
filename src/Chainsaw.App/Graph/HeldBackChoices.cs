using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// The four groups the selection view's counterparties are sorted in (sketch EX2, <em>Motpart</em>), after what
/// happens to the money, as the graph's colours say (sketch GR3c).
/// </summary>
public enum CounterpartyGroup
{
    /// <summary>Blue: a cash-out (<em>Uttag</em>).</summary>
    CashOut,

    /// <summary>Grey: nothing is known (<em>Okänd</em>).</summary>
    Unknown,

    /// <summary>Red: risk and the end of the trail (<em>Risk</em>).</summary>
    Risk,

    /// <summary>Yellow: where the money is spent (<em>Spel</em>).</summary>
    Spent,
}

/// <summary>
/// A held-back transaction as the selection view shows it (sketch EX2): the fact from the chain, and what the case
/// knows about its counterparty (its label's sort, whether it is already in the graph, whether the label came from
/// another case), which are judgements with their origin in the case.
/// </summary>
/// <param name="Transaction">The transaction, as read from the address's history.</param>
/// <param name="Sort">The counterparty's sort by its label, or <see cref="NodeSort.Unknown"/> without one.</param>
/// <param name="Name">The counterparty's name: its label's name, else its address shortened.</param>
/// <param name="InGraph">Whether the counterparty is already a node in the graph.</param>
/// <param name="FromOtherCase">Whether its label came only from another case.</param>
public sealed record HeldBackChoice(
    HeldBackTransaction Transaction, NodeSort Sort, string Name, bool InGraph, bool FromOtherCase)
{
    /// <summary>Gets the transaction's id, the choice's key.</summary>
    public string Key => Transaction.Txid.Value;

    /// <summary>Gets whether the address paid in it.</summary>
    public bool IsOut => Transaction.Side == PileSide.Out;

    /// <summary>Gets the amount in whole bitcoin, for the view's scales.</summary>
    public double Btc => Transaction.Amount.Value / (double)Satoshi.PerBitcoin;

    /// <summary>Gets the group the counterparty is sorted in.</summary>
    public CounterpartyGroup Group => GroupOf(Sort);

    /// <summary>
    /// Gets the name of the first negative label (risk, a sanction, a report) on any counterparty of the transaction,
    /// not only the largest, or <see langword="null"/> when the case has none.
    /// </summary>
    public string? Negative { get; init; }

    /// <summary>
    /// Gets whether money comes from or goes to an actor with a negative label (red: risk, a sanction, a report),
    /// which pulses.
    /// </summary>
    public bool IsNegative => Negative is not null || Group == CounterpartyGroup.Risk;

    /// <summary>Gets the negative label's name for the tip, or <see langword="null"/> when there is none.</summary>
    public string? NegativeName => IsNegative ? Negative ?? VisualLanguage.Of(Sort).Name : null;

    /// <summary>
    /// Gets whether the amount is uncertain (drawn dashed, principle 2): the address's share of a transaction others
    /// paid into too.
    /// </summary>
    public bool IsUncertain => Transaction.IsShared;

    /// <summary>Gets whether the address only paid itself (a consolidation).</summary>
    public bool IsSelf => Transaction.IsSelf;

    /// <summary>Says whether a label is negative: red in the graph (risk, a sanction) or a supplier's report.</summary>
    /// <param name="kind">The label's kind.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsNegativeKind(LabelKind kind) =>
        kind == LabelKind.Reported || GroupOf(VisualLanguage.SortOf(kind)) == CounterpartyGroup.Risk;

    /// <summary>Names a negative label's kind for the tip.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>For example <c>Mixer</c> or <c>Rapporterad</c>.</returns>
    public static string NegativeKindName(LabelKind kind) =>
        kind == LabelKind.Reported ? ReportedName : VisualLanguage.Of(VisualLanguage.SortOf(kind)).Name;

    /// <summary>The word for a report from a supplier (for example Chainabuse), as the graph's mark says it.</summary>
    public const string ReportedName = "Rapporterad";

    /// <summary>Gets the counterparty's key, which groups its transactions per side.</summary>
    public string CounterpartyKey =>
        (Transaction.Counterparty?.Value ?? Key) + (IsOut ? ":out" : ":in");

    /// <summary>Gives the group a sort lies in.</summary>
    /// <param name="sort">The sort.</param>
    /// <returns>The group.</returns>
    public static CounterpartyGroup GroupOf(NodeSort sort) => VisualLanguage.Of(sort).Ink switch
    {
        GraphInk.CashOut => CounterpartyGroup.CashOut,
        GraphInk.Spent => CounterpartyGroup.Spent,
        GraphInk.Risk or GraphInk.Sanctioned => CounterpartyGroup.Risk,
        _ => CounterpartyGroup.Unknown,
    };

    /// <summary>Gives the colour a group draws in.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The ink.</returns>
    public static GraphInk InkOf(CounterpartyGroup group) => group switch
    {
        CounterpartyGroup.CashOut => GraphInk.CashOut,
        CounterpartyGroup.Spent => GraphInk.Spent,
        CounterpartyGroup.Risk => GraphInk.Risk,
        _ => GraphInk.Unknown,
    };

    /// <summary>Gives a group's name in the filter (sketch EX2).</summary>
    /// <param name="group">The group.</param>
    /// <returns>The name.</returns>
    public static string NameOf(CounterpartyGroup group) => group switch
    {
        CounterpartyGroup.CashOut => "Uttag",
        CounterpartyGroup.Spent => "Spel",
        CounterpartyGroup.Risk => "Risk",
        _ => "Okänd",
    };
}

/// <summary>
/// What the selection view opens with (sketch EX2): the held-back transactions as choices, and the traced money's
/// arrival at the address, which the time window centres on and the sonar for an amount near it compares with.
/// </summary>
/// <param name="Clue">The clue the view was opened from.</param>
/// <param name="AnchorName">The address's name as the graph writes it.</param>
/// <param name="Choices">The transactions, newest first.</param>
/// <param name="Traced">How much of the traced money reached the address, or <see langword="null"/> when none
/// did.</param>
/// <param name="Arrival">When the traced money first reached it, or <see langword="null"/>.</param>
/// <param name="Problem">What the user should know about the reading, in Swedish, or <see langword="null"/>.</param>
public sealed record HeldBackChoices(
    HeldBackClue Clue,
    string AnchorName,
    IReadOnlyList<HeldBackChoice> Choices,
    Satoshi? Traced,
    DateTimeOffset? Arrival,
    string? Problem)
{
    /// <summary>
    /// How close below the traced amount a payment out must be to pulse (sketch EX2): within 3 %, the traced money
    /// passed on less a fee.
    /// </summary>
    public const double NearShare = 0.97;

    /// <summary>
    /// Says whether a choice is near the traced amount: a payment out of between 97 % and all of what was traced in.
    /// </summary>
    /// <param name="choice">The choice.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsNear(HeldBackChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (Traced is not { } traced || !choice.IsOut || traced.Value <= 0)
        {
            return false;
        }

        var amount = choice.Transaction.Amount.Value;
        return amount <= traced.Value && amount >= traced.Value * NearShare;
    }

    /// <summary>
    /// Sorts chosen transactions by the address whose history they were read from, so that each is brought into that
    /// address's expansion; a key that is not a choice is left out.
    /// </summary>
    /// <param name="keys">The chosen transactions' ids.</param>
    /// <returns>The transactions per address, in the order of the choices.</returns>
    public IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Txid>> PicksOf(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        return Choices
            .Where(choice => wanted.Contains(choice.Key))
            .Select(choice => (Address: choice.Transaction.Address ?? Clue.Address, choice.Transaction.Txid))
            .Where(pick => pick.Address is not null)
            .GroupBy(pick => pick.Address!)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Txid>)[.. group.Select(pick => pick.Txid)]);
    }

    /// <summary>
    /// Builds the choices from what Tracing read and the labels the case has for the counterparties.
    /// </summary>
    /// <param name="clue">The clue.</param>
    /// <param name="list">What was held back.</param>
    /// <param name="labels">The labels by counterparty; missing for one without.</param>
    /// <param name="scene">The graph as it is drawn.</param>
    /// <returns>The choices.</returns>
    public static HeldBackChoices Build(
        HeldBackClue clue,
        HeldBackList list,
        IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Label>> labels,
        GraphScene scene) => Build(clue, [list], labels, scene);

    /// <summary>
    /// Builds the choices from what Tracing read at each of the clue's addresses (a cluster can have several) and the
    /// labels the case has for the counterparties. A transaction read at two of them is one choice.
    /// </summary>
    /// <param name="clue">The clue.</param>
    /// <param name="lists">What was held back at each address.</param>
    /// <param name="labels">The labels by counterparty; missing for one without.</param>
    /// <param name="scene">The graph as it is drawn.</param>
    /// <returns>The choices, newest first.</returns>
    public static HeldBackChoices Build(
        HeldBackClue clue,
        IReadOnlyList<HeldBackList> lists,
        IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Label>> labels,
        GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(clue);
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(scene);
        var inGraph = scene.Snapshot.Graph.Nodes.SelectMany(node => node.GraphAddresses).ToHashSet();
        var choices = lists.SelectMany(list => list.Transactions.Select(held => held.Address is null
                ? held with { Address = list.Address }
                : held))
            .DistinctBy(held => held.Txid)
            .OrderByDescending(held => held.Time ?? DateTimeOffset.MaxValue)
            .Select(held => Choice(held, labels, inGraph))
            .ToList();
        var anchor = scene.Node(clue.Anchor);
        var traced = anchor is { TracedIn.Units.Sign: > 0 } && anchor.TracedIn.Asset == GraphScene.Shown
            ? new Satoshi((long)anchor.TracedIn.Units)
            : (Satoshi?)null;
        return new HeldBackChoices(
            clue,
            anchor?.Subject ?? clue.Anchor,
            choices,
            traced,
            ArrivalAt(scene, clue.Anchor),
            ProblemOf(lists));
    }

    /// <summary>Gives when the traced money first reached an actor, by the traced lines into it.</summary>
    /// <param name="scene">The graph as it is drawn.</param>
    /// <param name="anchor">The actor's node key.</param>
    /// <returns>The time, or <see langword="null"/> when no traced money reached it.</returns>
    public static DateTimeOffset? ArrivalAt(GraphScene scene, string anchor)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var times = scene.Edges
            .Where(edge => edge.Edge.To == anchor && edge.Traced.Units.Sign > 0)
            .Select(edge => edge.Edge.FirstTime)
            .OfType<DateTimeOffset>()
            .ToList();
        return times.Count == 0 ? null : times.Min();
    }

    /// <summary>
    /// Joins what the user should know about the reading, each text prefixed with its address when there are several.
    /// </summary>
    /// <param name="lists">What was read at each address.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    private static string? ProblemOf(IReadOnlyList<HeldBackList> lists)
    {
        var said = lists.Where(list => list.Problem is not null)
            .Select(list => lists.Count == 1 ? list.Problem! : Prefixed(list))
            .ToList();
        return said.Count == 0 ? null : string.Join(" ", said);
    }

    /// <summary>
    /// Makes one choice: the transaction with its main counterparty's label, name and place in the graph, and the
    /// first negative label on any of its counterparties.
    /// </summary>
    /// <param name="held">The transaction.</param>
    /// <param name="labels">The labels by counterparty.</param>
    /// <param name="inGraph">The addresses already in the graph.</param>
    /// <returns>The choice.</returns>
    private static HeldBackChoice Choice(
        HeldBackTransaction held,
        IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Label>> labels,
        HashSet<BitcoinAddress> inGraph)
    {
        var address = held.Counterparty;
        var own = LabelsAt(address, labels);
        var label = own.FirstOrDefault();
        var sort = label is null ? NodeSort.Unknown : VisualLanguage.SortOf(label.Kind);
        var name = label?.Name ?? (address is null ? ShortTxid(held.Txid) : SubjectText.Fit(address.Value, 0));
        var negative = PartiesOf(held).SelectMany(party => LabelsAt(party, labels))
            .FirstOrDefault(one => HeldBackChoice.IsNegativeKind(one.Kind));
        return new HeldBackChoice(
            held,
            sort,
            name,
            address is not null && inGraph.Contains(address),
            own.Count > 0 && own.All(one => one.IsOnlyFromOtherCases))
        {
            Negative = negative is null ? null : HeldBackChoice.NegativeKindName(negative.Kind),
        };
    }

    /// <summary>Writes what the user should know about one address's reading, after the address shortened.</summary>
    /// <param name="list">The reading.</param>
    /// <returns>For example <c>bc1q…x0wl: Offline: …</c>.</returns>
    private static string Prefixed(HeldBackList list) => SubjectText.Fit(list.Address.Value, 0) + ": " + list.Problem;

    /// <summary>Gives every counterparty of a transaction: its parties, or its main one without them.</summary>
    /// <param name="held">The transaction.</param>
    /// <returns>The addresses.</returns>
    public static IReadOnlyList<BitcoinAddress> PartiesOf(HeldBackTransaction held)
    {
        ArgumentNullException.ThrowIfNull(held);
        if (held.Parties.Count > 0)
        {
            return held.Parties;
        }

        return held.Counterparty is { } main ? [main] : [];
    }

    /// <summary>Gives the labels the case has for an address.</summary>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <param name="labels">The labels by address.</param>
    /// <returns>Its labels; empty without.</returns>
    private static IReadOnlyList<Label> LabelsAt(
        BitcoinAddress? address, IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Label>> labels) =>
        address is not null && labels.TryGetValue(address, out var found) ? found : [];

    /// <summary>Shortens a transaction's id, the name of a choice without a counterparty address.</summary>
    /// <param name="txid">The id.</param>
    /// <returns>For example <c>tx 3fa1…9c0d</c>.</returns>
    private static string ShortTxid(Txid txid) =>
        string.Create(CultureInfo.InvariantCulture, $"tx {SubjectText.Fit(txid.Value, 0)}");
}
