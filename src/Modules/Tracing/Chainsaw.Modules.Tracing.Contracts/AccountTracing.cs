using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A tracing method for the account model, where an address holds one balance per asset and money that comes in
/// mixes with what is already there: given an account's transfers and fees in one asset in time order and how much of
/// each incoming transfer is traced, it says how much of the traced money leaves with each outgoing transfer, goes to
/// fees and stays. FIFO v1 and haircut v1 are registered for every account chain (Ethereum first); Chainsaw checks
/// every answer and refuses a method that creates or loses money.
/// </summary>
public interface IAccountTracingMethod : ITracingMethod
{
    /// <summary>Carries the traced money through one account.</summary>
    /// <param name="ledger">The account's transfers of one asset, in time order.</param>
    /// <returns>The traced part of each outgoing transfer, what went to fees and what stays in the account.</returns>
    AccountTaint Propagate(AccountLedger ledger);
}

/// <summary>How an entry moves an account's balance.</summary>
public enum TransferDirection
{
    /// <summary>A transfer pays the account.</summary>
    In,

    /// <summary>The account pays a transfer to another account.</summary>
    Out,

    /// <summary>
    /// The account pays a fee that no account receives: gas on Ethereum, burned energy or bandwidth on TRON, also for a
    /// transaction that failed. Only the chain's own coin pays fees, so a token's ledger has none.
    /// </summary>
    Fee,
}

/// <summary>One movement in an account's ledger.</summary>
/// <param name="Transfer">The transfer or fee, a fact from the chain.</param>
/// <param name="Direction">Whether it comes in, goes out, or is a fee.</param>
/// <param name="Traced">
/// For a transfer that comes in, how much of it is traced (at most its amount); zero for one that goes out or a fee,
/// whose share the method decides.
/// </param>
public sealed record AccountEntry(AssetTransfer Transfer, TransferDirection Direction, AssetAmount Traced);

/// <summary>An account's movements of one asset, in the order the chain applied them.</summary>
/// <param name="Account">The account.</param>
/// <param name="Asset">The asset; every entry and the opening balance are in it.</param>
/// <param name="Opening">The balance before the first entry; none of it is traced.</param>
/// <param name="Entries">The movements, in time order.</param>
public sealed record AccountLedger(
    ChainAddress Account, Asset Asset, AssetAmount Opening, IReadOnlyList<AccountEntry> Entries);

/// <summary>
/// How the traced money went through one account, by one method. Nothing is created or lost: what came in traced is
/// what left with the outgoing entries, what went to fees and what remains.
/// </summary>
/// <param name="Outgoing">The traced part of each outgoing entry, in the ledger's order.</param>
/// <param name="Fees">The traced money that went to fees.</param>
/// <param name="Remaining">The traced money still in the account after the last entry.</param>
public sealed record AccountTaint(IReadOnlyList<AssetAmount> Outgoing, AssetAmount Fees, AssetAmount Remaining);
