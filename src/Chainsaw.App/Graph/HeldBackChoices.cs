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

    /// <summary>Gets whether the counterparty has a negative label (red: risk or a sanction), which pulses.</summary>
    public bool IsNegative => Group == CounterpartyGroup.Risk;

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
        GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(clue);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(scene);
        var inGraph = scene.Snapshot.Graph.Nodes.SelectMany(node => node.GraphAddresses).ToHashSet();
        var choices = list.Transactions.Select(held => Choice(held, labels, inGraph)).ToList();
        var anchor = scene.Node(clue.Anchor);
        var traced = anchor is { TracedIn.Units.Sign: > 0 } && anchor.TracedIn.Asset == GraphScene.Shown
            ? new Satoshi((long)anchor.TracedIn.Units)
            : (Satoshi?)null;
        var arrival = scene.Edges
            .Where(edge => edge.Edge.To == clue.Anchor && edge.Traced.Units.Sign > 0)
            .Select(edge => edge.Edge.FirstTime)
            .OfType<DateTimeOffset>()
            .DefaultIfEmpty()
            .Min();
        return new HeldBackChoices(
            clue,
            anchor?.Subject ?? clue.Anchor,
            choices,
            traced,
            arrival == default ? null : arrival,
            list.Problem);
    }

    /// <summary>Makes one choice: the transaction with its counterparty's label, name and place in the graph.</summary>
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
        var own = address is not null && labels.TryGetValue(address, out var found) ? found : [];
        var label = own.FirstOrDefault();
        var sort = label is null ? NodeSort.Unknown : VisualLanguage.SortOf(label.Kind);
        var name = label?.Name ?? (address is null ? ShortTxid(held.Txid) : SubjectText.Fit(address.Value, 0));
        return new HeldBackChoice(
            held,
            sort,
            name,
            address is not null && inGraph.Contains(address),
            own.Count > 0 && own.All(one => one.IsOnlyFromOtherCases));
    }

    /// <summary>Shortens a transaction's id, the name of a choice without a counterparty address.</summary>
    /// <param name="txid">The id.</param>
    /// <returns>For example <c>tx 3fa1…9c0d</c>.</returns>
    private static string ShortTxid(Txid txid) =>
        string.Create(CultureInfo.InvariantCulture, $"tx {SubjectText.Fit(txid.Value, 0)}");
}
