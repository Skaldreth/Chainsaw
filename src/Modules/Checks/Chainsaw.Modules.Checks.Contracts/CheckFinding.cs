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
}

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
/// <param name="Address">The candidate's address.</param>
/// <param name="Txid">The transaction it came in.</param>
/// <param name="Output">The output's index.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Score">How likely the analysis finds it, from 0 to 1, rounded to two decimals.</param>
/// <param name="Reason">Why, in plain Swedish.</param>
public sealed record CheckCandidate(
    string Key, BitcoinAddress Address, Txid Txid, int Output, Satoshi Amount, double Score, string Reason);
