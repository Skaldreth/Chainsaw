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
    /// Finds how to ask for an address's page that goes back from a slot: after the oldest entry newer than the slot
    /// on a page read before that reaches down to it, or after that page's own start when it has none.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="slot">The slot the page goes back from.</param>
    /// <returns>
    /// Whether it is known, the signature to ask after (<see langword="null"/> to ask from the newest and leave out
    /// what is above the slot), and whether the history is known to hold nothing at or below the slot, so that
    /// nothing need be asked.
    /// </returns>
    public (bool Known, EthereumTxHash? Before, bool Empty) Locate(EthereumAddress address, long slot)
    {
        ArgumentNullException.ThrowIfNull(address);
        lock (_gate)
        {
            if (!_pages.TryGetValue(address.Value, out var pages))
            {
                return (false, null, false);
            }

            foreach (var (page, before) in pages)
            {
                var top = page.AtOrBelow ?? long.MaxValue;
                var bottom = page.IsFull ? page.Entries[^1].Slot : long.MinValue;
                if (slot > top || slot < bottom)
                {
                    continue;
                }

                var after = page.Entries.LastOrDefault(entry => entry.Slot > slot)?.Transaction ?? before;
                var empty = !page.IsFull && page.Entries.All(entry => entry.Slot > slot);
                if (after is not null || empty)
                {
                    return (true, after, empty);
                }
            }

            return (false, null, false);
        }
    }
}
