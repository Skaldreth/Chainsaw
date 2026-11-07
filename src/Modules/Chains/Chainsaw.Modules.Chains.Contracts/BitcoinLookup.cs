namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The answer to a lookup: the value when there is one, how it was answered, and the sealed raw responses it is built
/// on. Nothing is made up: without an answer <see cref="Value"/> is <see langword="null"/> and
/// <see cref="Problem"/> says why, in Swedish.
/// </summary>
/// <typeparam name="T">What was looked up.</typeparam>
/// <param name="Status">How the lookup was answered.</param>
/// <param name="Value">The value, or <see langword="null"/> when there is none.</param>
/// <param name="Origins">
/// The sealed raw responses the answer is built on, oldest first, and a response that was sealed but could not be
/// used (an error status or a body that did not add up); empty when nothing was read or fetched.
/// </param>
/// <param name="Problem">Why there is no value or why it may be outdated, for the user; otherwise <see
/// langword="null"/>.</param>
public sealed record BitcoinLookup<T>(
    LookupStatus Status,
    T? Value,
    IReadOnlyList<ResponseOrigin> Origins,
    string? Problem)
    where T : class
{
    /// <summary>
    /// Gets what the user should know about the value that neither makes it missing nor outdated: that a transaction
    /// has left the block the case saw it in, or that it could not be checked against its own bytes or header. Empty
    /// when there is nothing to say.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Gets whether there is a value.</summary>
    public bool HasValue => Value is not null;
}
