using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>The forms that fold out in the inspector (sketch IN1a).</summary>
public enum InspectorForm
{
    /// <summary>No form is open; the inspector shows its facts.</summary>
    None,

    /// <summary><em>Etikett</em> (L): name, sort, source, reason and <em>Sprid inte</em>.</summary>
    Label,

    /// <summary><em>Bryt upp</em> (U), only on a cluster: the addresses to break out and the reason.</summary>
    Split,

    /// <summary><em>Annan kedja</em> (K): the chain and the transaction where the money continues.</summary>
    CrossChain,
}

/// <summary>
/// What a label rests on, as the form offers it (sketch IN1a, <em>Källa</em>). The order and the answer from an
/// exchange are the user's own judgement from a document, and say so in the reason; an open source is named.
/// </summary>
public enum LabelBasis
{
    /// <summary>The user's own judgement.</summary>
    Own,

    /// <summary>An open source, named by the user.</summary>
    OpenSource,

    /// <summary>The order the case came from.</summary>
    Order,

    /// <summary>An answer from an exchange to a request for information.</summary>
    ExchangeAnswer,
}

/// <summary>A sort the label form offers: the label's kind and how the graph draws it (sketch GR3c).</summary>
/// <param name="Kind">The label's kind.</param>
/// <param name="Look">How the sort is drawn.</param>
public sealed record LabelKindOption(LabelKind Kind, SortLook Look);

/// <summary>A chain the form <em>Annan kedja</em> offers.</summary>
/// <param name="Name">The chain's name.</param>
/// <param name="Chain">The chain, or <see langword="null"/> when Chainsaw does not know it yet.</param>
public sealed record ChainOption(string Name, Chain? Chain)
{
    /// <summary>Gets whether a link to the chain can be made now.</summary>
    public bool IsBuilt => Chain is not null;
}

/// <summary>
/// A coin, or an Ethereum movement, that came into the chosen node and can be linked to another chain. Exactly one of
/// <see cref="Coin"/> and <see cref="Movement"/> is set.
/// </summary>
/// <param name="Coin">The coin on its UTXO chain, or <see langword="null"/> for a movement on an account chain.</param>
/// <param name="Amount">Its amount.</param>
/// <param name="Time">When it came, or <see langword="null"/> while unconfirmed.</param>
public sealed record IncomingCoin(Outpoint? Coin, AssetAmount Amount, DateTimeOffset? Time)
{
    /// <summary>Gets the Ethereum movement, or <see langword="null"/> for a Bitcoin coin.</summary>
    public AccountSeed? Movement { get; init; }

    /// <summary>Gets the chain the money is on.</summary>
    public Chain Chain => Movement?.Transaction.Chain ?? Coin!.Txid.Chain;

    /// <summary>Gets the transaction it came in, as its chain writes it.</summary>
    public string TransactionId => Coin?.Txid.Value ?? Movement!.Transaction.Value;

    /// <summary>Writes what it is in full: the coin, or the movement's transaction and position.</summary>
    public string Written => Coin?.ToString() ?? string.Create(
        CultureInfo.InvariantCulture, $"{Movement!.Transaction.Value}#{Movement.Position}");
}

/// <summary>
/// The rules of the inspector's forms (sketch IN1a), without WPF: what each form may act on, the sorts, sources and
/// chains it offers, and what it asks the modules to store.
/// </summary>
public static class InspectorForms
{
    /// <summary>The most characters of a label's name, as Attribution takes it.</summary>
    public const int MostNameLength = 200;

    /// <summary>The most characters of a reason, as Attribution, Clustering and Tracing take it.</summary>
    public const int MostReasonLength = 1_000;

    /// <summary>What a reason from the order starts with.</summary>
    public const string OrderPrefix = "Ur beställningen: ";

    /// <summary>What a reason from an exchange's answer starts with.</summary>
    public const string ExchangeAnswerPrefix = "Ur svar från börs: ";

    /// <summary>
    /// Gets the sorts a label can say, in the legend's order with the victim and the unknown first (an unknown with a
    /// name is the user's own name for an actor). <see cref="LabelKind.Sanctioned"/>, <see cref="LabelKind.Reported"/>
    /// and <see cref="LabelKind.FormerlySanctioned"/> are left out: they come only from an imported list, a supplier's
    /// reports or Chainsaw's list of known contracts, and an own judgement must never be drawn as one (principle 2).
    /// </summary>
    public static IReadOnlyList<LabelKindOption> Kinds { get; } =
    [
        .. new[] { LabelKind.Victim, LabelKind.Unknown }
            .Concat(Enum.GetValues<LabelKind>()
                .Where(kind => kind is not (LabelKind.Victim or LabelKind.Unknown or LabelKind.Sanctioned
                    or LabelKind.Reported or LabelKind.FormerlySanctioned or LabelKind.PossiblySanctioned)))
            .Select(kind => new LabelKindOption(kind, VisualLanguage.Of(VisualLanguage.SortOf(kind))))
            .OrderBy(option => option.Kind is LabelKind.Victim or LabelKind.Unknown ? -1 : LegendOrder(option.Look)),
    ];

    /// <summary>Gets the sources the label form offers, in the sketch's order.</summary>
    public static IReadOnlyList<LabelBasis> Bases { get; } =
        [LabelBasis.Own, LabelBasis.OpenSource, LabelBasis.Order, LabelBasis.ExchangeAnswer];

    /// <summary>
    /// Gets the chains the form <em>Annan kedja</em> offers: Ethereum, TRON, BSC, Litecoin, Zcash, Solana and Bitcoin
    /// (last, since a coin on Bitcoin is where most links leave from), which a link can name now; a chain that is not
    /// built yet would be shown toned (CLAUDE.md: never show what does not exist as if it did).
    /// </summary>
    public static IReadOnlyList<ChainOption> Chains { get; } =
    [
        new("Ethereum", Chain.Ethereum),
        new("TRON", Chain.Tron),
        new("BSC", Chain.Bsc),
        new("Litecoin", Chain.Litecoin),
        new("Zcash", Chain.Zcash),
        new("Solana", Chain.Solana),
        new("Bitcoin", Chain.Bitcoin),
    ];

    /// <summary>Gives a source's name as the form writes it.</summary>
    /// <param name="basis">The source.</param>
    /// <returns>The name, in Swedish.</returns>
    public static string Describe(this LabelBasis basis) => basis switch
    {
        LabelBasis.OpenSource => "Öppen källa",
        LabelBasis.Order => "Beställningen",
        LabelBasis.ExchangeAnswer => "Svar från börs",
        _ => "Egen bedömning",
    };

    /// <summary>Gives what a label on a node is on: the address alone, or the cluster that holds it.</summary>
    /// <param name="node">The node.</param>
    /// <returns>
    /// The subject, or <see langword="null"/> when the node cannot carry a label (a tx, a coin, a chain).
    /// </returns>
    public static LabelSubject? LabelSubjectOf(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Node switch
        {
            { Kind: GraphNodeKind.Address, Accounts: [var account, ..] } => LabelSubject.ForAccount(account),
            { Kind: GraphNodeKind.Address, GraphAddresses: [var address, ..] } => LabelSubject.ForAddress(address),
            { Kind: GraphNodeKind.Cluster, GraphAddresses: [var address, ..] } => LabelSubject.ForCluster(address),
            _ => null,
        };
    }

    /// <summary>Says whether a node is a cluster that can be broken up.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> for a cluster.</returns>
    public static bool CanSplit(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Node is { Kind: GraphNodeKind.Cluster, GraphAddresses.Count: > 0 };
    }

    /// <summary>
    /// Gives the unfolded account cluster an account node can be broken out of (sketch KE1), or
    /// <see langword="null"/>: the node is one account, and an open account cluster holds it.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="node">The node.</param>
    /// <returns>The cluster, or <see langword="null"/>.</returns>
    public static GraphAccountCluster? AccountClusterToSplit(GraphScene scene, NodeView node)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(node);
        return node.Node is { Kind: GraphNodeKind.Address, Accounts: [var account] }
            ? scene.Snapshot.Graph.AccountClusters.FirstOrDefault(
                cluster => cluster.IsOpen && cluster.Members.Contains(account))
            : null;
    }

    /// <summary>Says what breaking an account out of its cluster does, in the form's notice.</summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="node">The account's node.</param>
    /// <returns>The sentence.</returns>
    public static string AccountSplitNotice(GraphAccountCluster cluster, NodeView node)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(node);
        return $"Bryter ut {node.Name} ur {GraphAreas.AccountClusterLabel(cluster)}. Ingen slutsats som slår ihop dem "
            + "räknas längre; uppbrytningen förseglas.";
    }

    /// <summary>
    /// Gives the coins that came into an actor along the graph's lines and have no link to another chain in force,
    /// the largest first: what can have gone into a bridge or a swap there.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="node">The actor; only an address or a cluster has any.</param>
    /// <returns>The coins.</returns>
    public static IReadOnlyList<IncomingCoin> IncomingCoins(GraphScene scene, NodeView node)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(node);
        if (node.Node.Kind is not (GraphNodeKind.Address or GraphNodeKind.Cluster))
        {
            return [];
        }

        var links = scene.Snapshot.Graph.CrossChainLinks;
        var linked = links.Select(link => link.From).OfType<Outpoint>().ToHashSet();
        var incoming = scene.Snapshot.Graph.Edges
            .Where(edge => edge.To == node.Key && edge.Kind == GraphEdgeKind.Transfer)
            .SelectMany(edge => edge.Transfers)
            .ToList();
        if (node.Node.Accounts.Count > 0)
        {
            var movements = links.Select(link => link.FromTransfer).OfType<AccountSeed>().ToHashSet();
            return
            [
                .. incoming.Where(transfer => transfer.Movement is not null)
                    .Select(transfer => (Transfer: transfer, Movement: MovementOf(transfer)))
                    .Where(pair => !movements.Contains(pair.Movement))
                    .DistinctBy(pair => pair.Movement)
                    .OrderByDescending(pair => pair.Transfer.Time ?? DateTimeOffset.MaxValue)
                    .ThenBy(pair => pair.Transfer.Transaction.Id, StringComparer.Ordinal)
                    .Select(pair => new IncomingCoin(null, pair.Transfer.Amount, pair.Transfer.Time)
                    {
                        Movement = pair.Movement,
                    }),
            ];
        }

        return
        [
            .. incoming
                .Where(transfer => transfer.Coin is { } coin && !linked.Contains(coin))
                .DistinctBy(transfer => transfer.Coin)
                .OrderByDescending(transfer => transfer.Amount.Units)
                .ThenBy(transfer => transfer.Coin!.ToString(), StringComparer.Ordinal)
                .Select(transfer => new IncomingCoin(transfer.Coin!, transfer.Amount, transfer.Block?.Time)),
        ];
    }

    /// <summary>Gives the Ethereum movement a line's transfer is.</summary>
    /// <param name="transfer">The transfer; it has a movement.</param>
    /// <returns>The movement.</returns>
    public static AccountSeed MovementOf(AssetTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return new AccountSeed(
            EthereumTxHash.Parse(transfer.Transaction.Chain, transfer.Transaction.Id), transfer.Movement!.Value,
            transfer.Position);
    }

    /// <summary>Says whether a line carries the money a link leaves from.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="link">The link.</param>
    /// <returns>Whether it does.</returns>
    public static bool Carries(GraphEdge edge, CrossChainLink link)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(link);
        return edge.Transfers.Any(transfer => link.From is { } coin
            ? transfer.Coin == coin
            : transfer.Movement is not null && MovementOf(transfer) == link.FromTransfer);
    }

    /// <summary>
    /// Recognises a transaction on another chain as it is typed: on the chosen chain, or else on another chain only
    /// when its format says so unambiguously (<c>0x</c> is Ethereum's). Plain hex fits both Ethereum without its prefix
    /// and TRON, so it is never moved to another chain by itself.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="chosen">The chosen chain.</param>
    /// <returns>The transaction, or <see langword="null"/> when no built chain writes it so.</returns>
    public static ChainTransactionRef? Recognise(string? text, Chain chosen)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var prefixed = text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        return Make(chosen, text) ?? (prefixed && chosen != Chain.Ethereum ? Make(Chain.Ethereum, text) : null);
    }

    /// <summary>Makes a reference on a chain when the text has the chain's format.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The text.</param>
    /// <returns>The reference, or <see langword="null"/>.</returns>
    private static ChainTransactionRef? Make(Chain chain, string text)
    {
        try
        {
            return new ChainTransactionRef(chain, text);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Builds the label the form asks Attribution to set.</summary>
    /// <param name="subject">What the label is on.</param>
    /// <param name="kind">The sort.</param>
    /// <param name="name">The name, or empty.</param>
    /// <param name="basis">What it rests on.</param>
    /// <param name="sourceName">The open source's name, when it rests on one.</param>
    /// <param name="reason">The user's reason.</param>
    /// <param name="doNotSpread">Whether it stays in this case (<em>Sprid inte</em>).</param>
    /// <param name="today">Today's date, the open source's version: when the user read it there.</param>
    /// <returns>The draft.</returns>
    public static LabelDraft Draft(
        LabelSubject subject,
        LabelKind kind,
        string? name,
        LabelBasis basis,
        string? sourceName,
        string reason,
        bool doNotSpread,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(reason);
        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var trimmedReason = reason.Trim();
        var draft = new LabelDraft(subject, kind, trimmedName, ReasonFor(basis, trimmedReason), null, doNotSpread);
        return basis == LabelBasis.OpenSource
            ? draft with
            {
                Source = LabelSource.OpenSource,
                SourceName = sourceName?.Trim(),
                SourceVersion = "läst " + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }
            : draft;
    }

    /// <summary>
    /// Gives the splits that break the picked addresses out of their cluster: for every link that merged a picked
    /// address with one that stays, a split between each picked address in it and one that stays, naming the link's
    /// heuristic. Clustering then refuses every such link, and the links among the picked addresses and among those
    /// that stay are not touched: picked addresses linked to each other leave together, and those joined only through
    /// an address that stays leave each on its own.
    /// </summary>
    /// <param name="cluster">The cluster, from Clustering.</param>
    /// <param name="picked">The addresses to break out; at least one, not all.</param>
    /// <param name="reason">The user's reason.</param>
    /// <returns>The splits, one per picked address in a link that crosses; empty when nothing crosses.</returns>
    public static IReadOnlyList<SplitRequest> BreakOut(
        Cluster cluster, IReadOnlyCollection<BitcoinAddress> picked, string reason)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(picked);
        ArgumentNullException.ThrowIfNull(reason);
        var leaving = picked.ToHashSet();
        var requests = new List<SplitRequest>();
        foreach (var link in cluster.Links
            .Where(link => link.State == LinkState.Merged)
            .DistinctBy(link => (link.Evidence.Heuristic.Name, link.Evidence.Sequence)))
        {
            // Every picked address in the link gets its own split, so that each one's history shows the breaking
            // out and a later link from it to an address that stays is refused too.
            var addresses = link.Evidence.Addresses;
            if (addresses.FirstOrDefault(address => !leaving.Contains(address)) is not { } stays)
            {
                continue;
            }

            requests.AddRange(addresses.Where(leaving.Contains).Select(goes =>
                new SplitRequest(goes, stays, link.Evidence.Heuristic.Name, reason.Trim())));
        }

        return [.. requests.DistinctBy(request => (request.First, request.Second, request.Heuristic))];
    }

    /// <summary>Gives a heuristic's short name, as a member's row writes it (sketch IN1a: <c>CIO v1</c>).</summary>
    /// <param name="heuristic">The heuristic.</param>
    /// <returns>The short name and the version.</returns>
    public static string ShortName(HeuristicId heuristic)
    {
        ArgumentNullException.ThrowIfNull(heuristic);
        var name = heuristic.Name switch
        {
            HeuristicNames.CommonInputOwnership => "CIO",
            HeuristicNames.ChangeAddress => "Växel",
            HeuristicNames.PeelChain => "Peel",
            HeuristicNames.CoinJoin => "CoinJoin",
            UserMerges.Name => "Egen",
            _ => heuristic.Name,
        };
        return name + " v" + heuristic.Version.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Gives a reason as it is stored, saying where a judgement from a document comes from.</summary>
    /// <param name="basis">What the label rests on.</param>
    /// <param name="reason">The user's reason, trimmed.</param>
    /// <returns>The reason to store.</returns>
    private static string ReasonFor(LabelBasis basis, string reason) => basis switch
    {
        LabelBasis.Order when reason.Length > 0 => OrderPrefix + reason,
        LabelBasis.ExchangeAnswer when reason.Length > 0 => ExchangeAnswerPrefix + reason,
        _ => reason,
    };

    /// <summary>Gives a sort's place in the legend, so that the form's sorts stand in the same order.</summary>
    /// <param name="look">The sort.</param>
    /// <returns>Its index.</returns>
    private static int LegendOrder(SortLook look)
    {
        for (var index = 0; index < VisualLanguage.Sorts.Count; index++)
        {
            if (VisualLanguage.Sorts[index].Sort == look.Sort)
            {
                return index;
            }
        }

        return VisualLanguage.Sorts.Count;
    }
}
