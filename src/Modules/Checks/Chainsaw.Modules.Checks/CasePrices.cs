using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Reads the prices the case already holds at the transactions' block times, for <see cref="CheckInput.Prices"/>:
/// never fetched, never guessed (principle 9).
/// </summary>
internal static class CasePrices
{
    /// <summary>The currencies read, in the order an analysis weighs them.</summary>
    public static readonly IReadOnlyList<FiatCurrency> Currencies =
        [FiatCurrency.Sek, FiatCurrency.Eur, FiatCurrency.Usd];

    /// <summary>
    /// The crypto rate's resolutions, in the order they are taken: the hour first (the default, the one the graph's
    /// kronor are usually counted with), then the minute and the day. The first the case holds is used, so that the
    /// same case always gives the same price, whatever the setting is now.
    /// </summary>
    public static readonly IReadOnlyList<PriceResolution> Resolutions =
        [PriceResolution.Hour, PriceResolution.Minute, PriceResolution.Day];

    /// <summary>Reads the prices the case holds for the transactions that can be weighed.</summary>
    /// <param name="rates">The reader of the case's rates.</param>
    /// <param name="case">The open case.</param>
    /// <param name="transactions">The graph's transactions.</param>
    /// <returns>
    /// Per confirmed transaction with at least two outputs, the prices at its block time in the order of
    /// <see cref="Currencies"/>, one per currency the case holds; a transaction without any is left out.
    /// </returns>
    public static Dictionary<Txid, IReadOnlyList<Price>> Read(
        ICaseRates rates, ICase @case, IReadOnlyList<BitcoinTransaction> transactions)
    {
        var weighed = transactions.Where(Weighable).ToList();
        var found = new Dictionary<(Txid Txid, FiatCurrency Currency), Price>();
        foreach (var resolution in Resolutions)
        {
            // One read of the case per resolution, and only for what an earlier resolution did not give.
            var wanted = weighed
                .SelectMany(transaction => Currencies.Select(currency => (Transaction: transaction, Currency: currency)))
                .Where(pair => !found.ContainsKey((pair.Transaction.Txid, pair.Currency)))
                .ToList();
            if (wanted.Count == 0)
            {
                break;
            }

            var answers = rates.ReadStored(
                @case,
                [
                    .. wanted.Select(pair => new PriceRequest(pair.Transaction.Txid.Chain, pair.Transaction.Block!.Time)
                    {
                        Currency = pair.Currency,
                        Resolution = resolution,
                    }),
                ]);
            for (var i = 0; i < wanted.Count; i++)
            {
                if (answers[i] is { Status: PriceLookupStatus.FromCase, Value: { } price })
                {
                    found[(wanted[i].Transaction.Txid, wanted[i].Currency)] = price;
                }
            }
        }

        return weighed
            .Select(transaction => (
                transaction.Txid,
                Prices: Currencies
                    .Select(currency => found.GetValueOrDefault((transaction.Txid, currency)))
                    .OfType<Price>()
                    .ToList()))
            .Where(entry => entry.Prices.Count > 0)
            .ToDictionary(entry => entry.Txid, entry => (IReadOnlyList<Price>)entry.Prices);
    }

    /// <summary>
    /// Says whether a transaction can be weighed for round amounts: confirmed (its block gives the time), not a
    /// coinbase, and with at least two outputs with an address.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    public static bool Weighable(BitcoinTransaction transaction) =>
        !transaction.IsCoinbase && transaction.Block is not null && transaction.PayingOutputs().Count >= 2;
}
