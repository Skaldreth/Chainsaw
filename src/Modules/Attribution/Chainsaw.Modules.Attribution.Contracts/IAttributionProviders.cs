using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The suppliers of attribution Chainsaw can ask about an address, for example Chainabuse (the user's decision
/// 2026-11-08). Several can be on at once, each with its own switch, and Chainsaw asks every one that is on. A lookup
/// sends only the address, through the chosen exit (principle 4); every response is sealed in the case with its
/// SHA-256 (principle 1), and what a supplier reports becomes labels with the supplier as their origin, always
/// judgements and never facts (principle 2).
/// </summary>
public interface IAttributionProviders
{
    /// <summary>Gets the suppliers Chainsaw knows, in the order they are asked.</summary>
    IReadOnlyList<AttributionProviderInfo> Providers { get; }

    /// <summary>
    /// Asks the suppliers that are on about an address and labels what they report. A report already labelled in the
    /// case, also one taken back, is not labelled again. Nothing is sent for a supplier that is off, that does not
    /// know the chain, that has no key in the vault, or when no exit is connected.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address with its chain.</param>
    /// <param name="enabled">The names of the suppliers that are on (<see cref="AttributionProviderInfo.Name"/>).</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns>What each supplier that is on answered, in <see cref="Providers"/>' order.</returns>
    Task<IReadOnlyList<ProviderLookup>> LookupAsync(
        ICase @case, ChainAddress address, IReadOnlyCollection<string> enabled,
        CancellationToken cancellationToken = default);
}

/// <summary>A supplier of attribution.</summary>
/// <param name="Name">The supplier's stable name in the case and the settings, for example <c>chainabuse</c>.</param>
/// <param name="DisplayName">The name the user reads, for example <c>Chainabuse</c>.</param>
/// <param name="KeyService">The service its API key is stored under in the vault.</param>
/// <param name="Chains">The chains it can be asked about.</param>
public sealed record AttributionProviderInfo(
    string Name, string DisplayName, string KeyService, IReadOnlyList<Chain> Chains);

/// <summary>What asking a supplier about an address came to.</summary>
public enum ProviderLookupStatus
{
    /// <summary>The supplier answered; <see cref="ProviderLookup.Added"/> holds the new labels, if any.</summary>
    Answered,

    /// <summary>The supplier is not asked about the chain, or the chain cannot carry labels yet; nothing was sent.</summary>
    NotSupported,

    /// <summary>No key for the supplier is stored in the vault, or the vault is locked; nothing was sent.</summary>
    NoKey,

    /// <summary>No exit is connected; nothing was sent (principle 4).</summary>
    Offline,

    /// <summary>The supplier refused the key (401 or 403); the response is sealed.</summary>
    Refused,

    /// <summary>The supplier asked Chainsaw to wait (429); the response is sealed.</summary>
    RateLimited,

    /// <summary>The lookup failed on the way or the response could not be read.</summary>
    Failed,
}

/// <summary>What one supplier answered about an address.</summary>
/// <param name="Provider">The supplier's name.</param>
/// <param name="Status">What the lookup came to.</param>
/// <param name="Added">The labels set from the answer.</param>
/// <param name="Reports">How many reports the answer held about the address, also those already labelled.</param>
/// <param name="ResponseSequence">The sealed response's sequence, or <see langword="null"/> when nothing came back.</param>
/// <param name="Problem">Why there is no answer, in Swedish, or <see langword="null"/>.</param>
public sealed record ProviderLookup(
    string Provider,
    ProviderLookupStatus Status,
    IReadOnlyList<LabelEntry> Added,
    int Reports,
    long? ResponseSequence,
    string? Problem);
