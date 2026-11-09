using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// Ends the trail where a label says the money reaches a service: an exchange or another withdrawal point, a mixer,
/// a CoinJoin, a swap or bridge to another chain, or a service where money is spent. The end rests on the label and
/// says so; it is a judgement, never a fact (principle 2). Labels on a victim, a sanctioned owner or something
/// unknown do not end the trail: the money can still be followed.
/// </summary>
/// <param name="attribution">The labels.</param>
/// <param name="transactions">The labels on transactions, which end the trail at the transaction itself.</param>
internal sealed class LabelTrailEnds(IAttributionService attribution, ITransactionLabels transactions)
    : ITrailEndClassifier
{
    /// <summary>Says where a label ends the trail.</summary>
    /// <param name="kind">What the label says.</param>
    /// <returns>The reason, or <see langword="null"/> when the trail goes on.</returns>
    public static TrailEndReason? EndFor(LabelKind kind) => kind switch
    {
        LabelKind.Exchange or LabelKind.NoKycExchange or LabelKind.NestedService or LabelKind.OtcP2P
            or LabelKind.CryptoAtm or LabelKind.CryptoCard => TrailEndReason.Exchange,
        LabelKind.Gambling or LabelKind.Merchant or LabelKind.GiftCard or LabelKind.Darknet
            or LabelKind.EscrowMarket => TrailEndReason.Service,
        LabelKind.Mixer => TrailEndReason.Mixer,
        LabelKind.CoinJoin => TrailEndReason.CoinJoin,
        LabelKind.SwapBridge => TrailEndReason.OtherChain,
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<TrailEndHint> ClassifyAddress(ICase @case, BitcoinAddress address) =>
        Hints(attribution.GetLabels(@case, address));

    /// <inheritdoc/>
    public IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<TrailEndHint>> ClassifyAddresses(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses) =>
        attribution.GetLabels(@case, addresses)
            .Select(pair => (pair.Key, Hints: Hints(pair.Value)))
            .Where(pair => pair.Hints.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Hints);

    /// <inheritdoc/>
    /// <remarks>
    /// The same rules as for Bitcoin (<see cref="EndFor"/>), on the labels of each address read from the case once;
    /// an Ethereum account is not clustered, so only the labels on the account itself count.
    /// </remarks>
    public IReadOnlyDictionary<ChainAddress, IReadOnlyList<TrailEndHint>> ClassifyAccounts(
        ICase @case, IReadOnlyCollection<ChainAddress> addresses) =>
        attribution.GetLabels(@case, addresses)
            .Select(pair => (pair.Key, Hints: Hints(pair.Value)))
            .Where(pair => pair.Hints.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Hints);

    /// <summary>Gives the ends of the trail that some labels name.</summary>
    /// <param name="labels">The labels of one address.</param>
    /// <returns>The hints; empty when none ends the trail.</returns>
    private static IReadOnlyList<TrailEndHint> Hints(IEnumerable<Label> labels) =>
    [
        .. labels
            .Select(label => (Label: label, Reason: EndFor(label.Kind)))
            .Where(pair => pair.Reason is not null)
            .Select(pair => new TrailEndHint(pair.Reason!.Value, Source(pair.Label), pair.Label.Explain())),
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// A label on the transaction itself (for example a swap at a known service, where no address says it) ends the
    /// trail by the same rules as a label on an address (<see cref="EndFor"/>), resting on the label.
    /// </remarks>
    public IReadOnlyList<TrailEndHint> ClassifyTransaction(ICase @case, BitcoinTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return
        [
            .. transactions.For(@case, transaction.Txid.Chain, transaction.Txid.Value)
                .Select(label => (Label: label, Reason: EndFor(label.Kind)))
                .Where(pair => pair.Reason is not null)
                .Select(pair => new TrailEndHint(pair.Reason!.Value, Source(pair.Label), Explain(pair.Label))),
        ];
    }

    /// <summary>Says what an end at a transaction rests on: the label on it, set by whom.</summary>
    /// <param name="label">The label on the transaction.</param>
    /// <returns>The source, for example <c>etikett på tx Swap eller brygga: egen v1</c>.</returns>
    private static string Source(TransactionLabel label) => string.Format(
        CultureInfo.InvariantCulture,
        "etikett på tx {0}: {1} {2}",
        label.Kind.Describe(),
        LabelRules.OwnSourceName,
        LabelRules.OwnSourceVersion);

    /// <summary>Explains an end at a transaction in plain Swedish.</summary>
    /// <param name="label">The label on the transaction.</param>
    /// <returns>The explanation.</returns>
    private static string Explain(TransactionLabel label) => string.Format(
        CultureInfo.InvariantCulture,
        "Transaktionen är märkt {0} av {1} {2:yyyy-MM-dd}{3}. Det är en bedömning, inte ett faktum.",
        label.Title.ToLowerInvariant(),
        label.SetBy.Name,
        label.SetUtc,
        label.Reason.Length == 0 ? string.Empty : $": {label.Reason}");

    /// <summary>Says what the end rests on: the label and its origins with their versions.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The source, for example <c>etikett Börs: egen v1</c>.</returns>
    private static string Source(Label label) => string.Format(
        CultureInfo.InvariantCulture,
        "etikett {0}: {1}",
        label.Kind.Describe(),
        string.Join("; ", label.Origins.Select(origin => origin.IsFromOtherCase
            ? $"från ett annat ärende ({origin.SourceName} {origin.SourceVersion})"
            : $"{origin.SourceName} {origin.SourceVersion}")));
}
