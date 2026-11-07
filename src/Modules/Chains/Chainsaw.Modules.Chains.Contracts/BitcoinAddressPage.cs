namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// One page of an address's transactions, newest first. The first page has the unconfirmed transactions and the
/// newest confirmed ones; each following page has older confirmed transactions.
/// </summary>
/// <param name="Address">The address.</param>
/// <param name="After">
/// The txid the page continues after, or <see langword="null"/> for the first page.
/// </param>
/// <param name="Transactions">The transactions on the page.</param>
/// <param name="NextAfter">
/// The txid to ask for the next page with: the last confirmed transaction on the page. <see langword="null"/> when the
/// page has no confirmed transaction, which is where the history ends.
/// </param>
public sealed record BitcoinAddressPage(
    BitcoinAddress Address,
    Txid? After,
    IReadOnlyList<BitcoinTransaction> Transactions,
    Txid? NextAfter);
