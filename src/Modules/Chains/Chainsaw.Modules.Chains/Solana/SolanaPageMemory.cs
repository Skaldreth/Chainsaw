using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Remembers the pages of signatures read per address, so that a page that goes back from a slot can be asked for.
/// Solana's <c>getSignaturesForAddress</c> pages by signature (<c>before</c>), not by slot, while the account model
/// pages by where a page starts (<see cref="IAccountSource.PageStartOf"/>); a reader always reads the newer page first
/// (from the case or the source), so the signature to go on from is known by the time the next page is asked for.
/// </summary>
internal sealed class SolanaPageMemory
{
    /// <summary>Guards the pages.</summary>
    private readonly Lock _gate = new();

    /// <summary>The pages read, by address, each with the signature it was asked after.</summary>
    private readonly Dictionary<string, List<(SolanaSignaturePage Page, EthereumTxHash? Before)>> _pages =
        new(StringComparer.Ordinal);

    /// <summary>Notes a page that was read, from the case or from the source.</summary>
    /// <param name="page">The page.</param>
    /// <param name="before">The signature it was asked after, or <see langword="null"/> for the newest.</param>
    public void Record(SolanaSignaturePage page, EthereumTxHash? before)
    {
        ArgumentNullException.ThrowIfNull(page);
        lock (_gate)
        {
            if (!_pages.TryGetValue(page.Address.Value, out var pages))
            {
                pages = [];
                _pages[page.Address.Value] = pages;
            }

            pages.RemoveAll(each => each.Page.AtOrBelow == page.AtOrBelow);
            pages.Add((page, before));
        }
    }

    /// <summary>
    /// Finds an address's page that goes back from a slot. A page read before that is not full and reaches up to the
    /// slot already holds the whole history at or below it: its own entries there are the page, and nothing is asked.
    /// Otherwise the page is asked after the oldest entry newer than the slot on a page read before that reaches down
    /// to it, or after that page's own start when it has none.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="slot">The slot the page goes back from.</param>
    /// <returns>
    /// Whether it is known; the entries at or below the slot when a page read before holds them all, so that nothing
    /// need be asked (empty when there are none); otherwise <see langword="null"/> and the signature to ask after.
    /// </returns>
    public (bool Known, EthereumTxHash? Before, IReadOnlyList<SolanaSignature>? Entries) Locate(
        EthereumAddress address, long slot)
    {
        ArgumentNullException.ThrowIfNull(address);
        lock (_gate)
        {
            if (!_pages.TryGetValue(address.Value, out var pages))
            {
                return (false, null, null);
            }

            foreach (var (page, before) in pages)
            {
                if (slot > (page.AtOrBelow ?? long.MaxValue) || (page.IsFull && slot < page.Entries[^1].Slot))
                {
                    continue;
                }

                if (!page.IsFull)
                {
                    // The page reaches the end of the history: what is at or below the slot is on it.
                    return (true, null, [.. page.Entries.Where(entry => entry.Slot <= slot)]);
                }

                if ((page.Entries.LastOrDefault(entry => entry.Slot > slot)?.Transaction ?? before) is { } after)
                {
                    return (true, after, null);
                }
            }

            return (false, null, null);
        }
    }
}
