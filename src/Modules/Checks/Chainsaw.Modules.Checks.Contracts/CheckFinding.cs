using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// What an analysis found: a check or a suggestion, the area it is drawn as, why in plain Swedish (principle 3) and
/// what it rests on, so that every step can be checked against the chain (principle 1). It is a judgement, never a
/// fact (principle 2).
/// </summary>
/// <param name="Key">
/// What the finding is about, stable within the analysis, for example the deposit's coin; a decision about a
/// suggestion is kept by it.
/// </param>
/// <param name="Kind">A check or a suggestion.</param>
/// <param name="Area">The sort of area it is drawn as.</param>
/// <param name="Title">A short title, for example <c>Peel chain, 12 hopp</c>.</param>
/// <param name="Summary">One line under the title.</param>
/// <param name="Why">Why, in plain Swedish, saying that it is a judgement.</param>
/// <param name="Addresses">
/// The addresses the area is drawn around. For a suggestion with candidates, the first is the anchor (for example the
/// mixer's deposit address) and the rest are the candidates' addresses.
/// </param>
/// <param name="Transactions">The transactions the area is drawn around, when they are actors of their own.</param>
/// <param name="Basis">
/// The transactions it rests on, with their blocks. For a suggestion with candidates, the first is the anchor's and
/// the rest are the candidates'.
/// </param>
/// <param name="Candidates">
/// The candidates of a suggestion that has several, best first, all of them; the service keeps the decided ones and
/// the best that wait (<see cref="CheckRules.MostWaitingCandidates"/>). Empty otherwise.
/// </param>
public sealed record CheckFinding(
    string Key,
    CheckKind Kind,
    AreaSort Area,
    string Title,
    string Summary,
    string Why,
    IReadOnlyList<BitcoinAddress> Addresses,
    IReadOnlyList<Txid> Transactions,
    IReadOnlyList<CheckBasis> Basis,
    IReadOnlyList<CheckCandidate> Candidates)
{
    /// <summary>Gets the key.</summary>
    public string Key { get; } = string.IsNullOrWhiteSpace(Key)
        ? throw new ArgumentException("A finding has a key.", nameof(Key))
        : Key;

    /// <summary>
    /// Gets the payment the suggestion starts from when it crosses chains (<c>cross-chain</c>): the money into a bridge
    /// or a swap, on any chain, with its block; <see langword="null"/> otherwise.
    /// </summary>
    public ChainMovement? Departure { get; init; }

    /// <summary>
    /// Gets the rates the finding rests on (for example <c>round-fiat</c>), each with its source and the SHA-256 of the
    /// sealed response it was read from, so that the valuation can be checked (principle 1); empty for a finding that
    /// rests on no rate.
    /// </summary>
    public IReadOnlyList<CheckRate> Rates { get; init; } = [];
}

/// <summary>
/// A rate a finding rests on: one sealed response of a rate source in the case (the module Pricing), and what was read
/// from it, in Swedish.
/// </summary>
/// <param name="Source">The source's stable name, for example <c>bitstamp</c> or <c>riksbank</c>.</param>
/// <param name="Sequence">The response's number in the case's sealed table of rate responses.</param>
/// <param name="Sha256">The SHA-256 of the raw response, as lower-case hex.</param>
/// <param name="Reading">What was read from it, for example the step <c>1 BTC = 61 234 EUR: Bitstamp, …</c>.</param>
public sealed record CheckRate(string Source, long Sequence, string Sha256, string Reading);

/// <summary>A transaction a finding rests on, with its block, so that it can be checked against the chain.</summary>
/// <param name="Txid">The transaction.</param>
/// <param name="Block">Its block; <see langword="null"/> while it is unconfirmed.</param>
public sealed record CheckBasis(Txid Txid, BlockReference? Block)
{
    /// <summary>Gives the basis of a transaction.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>Its txid and block.</returns>
    public static CheckBasis Of(BitcoinTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return new CheckBasis(transaction.Txid, transaction.Block);
    }
}

/// <summary>
/// One candidate of a suggestion, for example a likely output of a mixer: always dashed and never a fact (CLAUDE.md,
/// <em>Mixers är vanliga</em>), approved or dismissed by the user on its own.
/// </summary>
/// <param name="Key">What the candidate is, stable within the finding, for example its coin.</param>
/// <param name="Address">
/// The candidate's address on Bitcoin, or <see langword="null"/> when it is on another chain (<see cref="Arrival"/>).
/// </param>
/// <param name="Txid">The Bitcoin transaction it came in, or <see langword="null"/> on another chain.</param>
/// <param name="Output">The output's index on Bitcoin; 0 on another chain.</param>
/// <param name="Amount">The amount in satoshi, or <see langword="null"/> on another chain.</param>
/// <param name="Score">How likely the analysis finds it, from 0 to 1, rounded to two decimals.</param>
/// <param name="Reason">Why, in plain Swedish.</param>
public sealed record CheckCandidate(
    string Key, BitcoinAddress? Address, Txid? Txid, int Output, Satoshi? Amount, double Score, string Reason)
{
    /// <summary>
    /// Gets the payment on any chain the candidate is, for a suggestion that crosses chains (<c>cross-chain</c>): the
    /// transaction, the recipient and the amount in its asset; <see langword="null"/> for a candidate on Bitcoin alone.
    /// </summary>
    public ChainMovement? Arrival { get; init; }
}
