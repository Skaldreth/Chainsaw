using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// One supplier of attribution: how to ask it about an address and how to read its answer. It never sends anything
/// itself; <see cref="AttributionProviders"/> sends the request through the exit and seals the response.
/// </summary>
internal interface IAttributionProvider
{
    /// <summary>Gets what the supplier is.</summary>
    AttributionProviderInfo Info { get; }

    /// <summary>Gives the request that asks about an address: the address only, and the key as a header.</summary>
    /// <param name="address">The address; its chain is one of <see cref="AttributionProviderInfo.Chains"/>.</param>
    /// <param name="key">The key's secret from the vault.</param>
    /// <returns>The request.</returns>
    ExitRequest Request(ChainAddress address, string key);

    /// <summary>
    /// Reads what a successful answer reports about the address, grouped so that each group becomes one label.
    /// </summary>
    /// <param name="body">The response's raw bytes.</param>
    /// <param name="address">The address that was asked about.</param>
    /// <returns>What it reports; empty when nothing is reported.</returns>
    /// <exception cref="FormatException">The answer cannot be read.</exception>
    ProviderAnswer Read(ReadOnlySpan<byte> body, ChainAddress address);
}

/// <summary>What a supplier reports about an address.</summary>
/// <param name="Reports">How many reports name the address.</param>
/// <param name="Findings">The reports grouped into what each label says.</param>
internal sealed record ProviderAnswer(int Reports, IReadOnlyList<ProviderFinding> Findings);

/// <summary>One label's worth of what a supplier reports.</summary>
/// <param name="Key">What identifies the finding at the supplier, the same each time it is asked.</param>
/// <param name="Kind">What the label says the address is.</param>
/// <param name="Name">The label's name, for example what the address was reported as.</param>
/// <param name="Reason">Why, in plain Swedish: what the reports say and their ids.</param>
internal sealed record ProviderFinding(string Key, LabelKind Kind, string Name, string Reason);
