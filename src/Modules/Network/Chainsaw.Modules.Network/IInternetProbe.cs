namespace Chainsaw.Modules.Network;

/// <summary>
/// Asks whether the internet actually answers through the way out through Windows. The tests replace it to answer
/// yes, no or not at all.
/// </summary>
internal interface IInternetProbe
{
    /// <summary>Asks the internet once, within a short time.</summary>
    /// <param name="cancellationToken">Stops the probe, when Chainsaw closes.</param>
    /// <returns><see langword="true"/> when a known target answered as it should.</returns>
    Task<bool> ReachesInternetAsync(CancellationToken cancellationToken);
}
