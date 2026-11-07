namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// The answer to a request for a rate: the value when there is one, how it was answered, and the sealed raw responses
/// it is built on. Nothing is made up: without an answer <see cref="Value"/> is <see langword="null"/> and
/// <see cref="Problem"/> says why, in Swedish.
/// </summary>
/// <typeparam name="T">What was looked up.</typeparam>
/// <param name="Status">How it was answered.</param>
/// <param name="Value">The value, or <see langword="null"/> when there is none.</param>
/// <param name="Origins">
/// The sealed raw responses the answer is built on, and a response that was sealed but could not be used; empty when
/// nothing was read or fetched.
/// </param>
/// <param name="Problem">Why there is no value, for the user; otherwise <see langword="null"/>.</param>
public sealed record PriceLookup<T>(
    PriceLookupStatus Status,
    T? Value,
    IReadOnlyList<RateOrigin> Origins,
    string? Problem)
    where T : class
{
    /// <summary>Gets whether there is a value.</summary>
    public bool HasValue => Value is not null;
}
