using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>One output of a transaction: an amount locked to a script, usually an address.</summary>
/// <param name="Index">The output's place in the transaction (its <c>vout</c>), from 0.</param>
/// <param name="ScriptType">The kind of locking script, read from the script's bytes.</param>
/// <param name="Address">The address, when the script has one (not for OP_RETURN, P2PK or bare multisig).</param>
/// <param name="Amount">The amount.</param>
/// <param name="Script">The locking script (scriptPubKey) as lower-case hex.</param>
/// <param name="SpentBy">
/// The input that spends this output, when Chainsaw has asked the source (see
/// <see cref="IBitcoinSource.GetOutputSpendsAsync"/>); <see langword="null"/> when it is not known or unspent.
/// </param>
public sealed record BitcoinOutput(
    int Index,
    BitcoinScriptType ScriptType,
    BitcoinAddress? Address,
    Satoshi Amount,
    string Script,
    BitcoinSpend? SpentBy);
