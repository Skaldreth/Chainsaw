using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A tracing method for the account model, where an address holds one balance per asset and money that comes in
/// mixes with what is already there: given an account's transfers of one asset in time order and how much of each
/// incoming transfer is traced, it says how much of the traced money leaves with each outgoing transfer and how much
/// stays. No account chain is built yet; the contract is here so that one is added without anything for Bitcoin
/// changing.
/// </summary>
public interface IAccountTracingMethod : ITracingMethod
{
    /// <summary>Carries the traced money through one account.</summary>
    /// <param name="ledger">The account's transfers of one asset, in time order.</param>
    /// <returns>The traced part of each outgoing transfer, and what stays in the account.</returns>
    AccountTaint Propagate(AccountLedger ledger);
}

/// <summary>Whether a transfer comes into an account or leaves it.</summary>
public enum TransferDirection
{
    /// <summary>The transfer pays the account.</summary>
    In,

    /// <summary>The account pays the transfer.</summary>
    Out,
}

/// <summary>One transfer in an account's ledger.</summary>
/// <param name="Transfer">The transfer, a fact from the chain.</param>
/// <param name="Direction">Whether it comes in or goes out.</param>
/// <param name="Traced">
/// For a transfer that comes in, how much of it is traced (at most its amount); zero for one that goes out, whose
/// share the method decides.
/// </param>
public sealed record AccountEntry(AssetTransfer Transfer, TransferDirection Direction, AssetAmount Traced);

/// <summary>An account's transfers of one asset, in the order the chain applied them.</summary>
/// <param name="Account">The account.</param>
/// <param name="Opening">The balance before the first entry; none of it is traced.</param>
/// <param name="Entries">The transfers, in time order.</param>
public sealed record AccountLedger(ChainAddress Account, AssetAmount Opening, IReadOnlyList<AccountEntry> Entries);

/// <summary>How the traced money went through one account, by one method.</summary>
/// <param name="Outgoing">The traced part of each outgoing entry, in the ledger's order.</param>
/// <param name="Remaining">The traced money still in the account after the last entry.</param>
public sealed record AccountTaint(IReadOnlyList<AssetAmount> Outgoing, AssetAmount Remaining);
