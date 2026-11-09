namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>How a round of probes for a chain ended.</summary>
public enum ProbeRun
{
    /// <summary>The endpoints that are on were tried, and their results kept.</summary>
    Probed,

    /// <summary>The chain is <see cref="SourceMode.Off"/>; nothing was sent.</summary>
    Off,

    /// <summary>
    /// Nothing was kept: the exit does not allow lookups now (or stopped allowing them during the round), or the vault
    /// is locked. The earlier results stand.
    /// </summary>
    NotSent,

    /// <summary>
    /// The exit did not allow lookups, so only the user's own nodes were tried (straight in the own network) and their
    /// results kept, unmeasured against the chain (<see cref="EndpointProbe.Unreferenced"/>); the other endpoints'
    /// earlier results stand, and a round with the exit is still to be made.
    /// </summary>
    OwnNodesOnly,

    /// <summary>No endpoint of the chain is on; nothing was sent.</summary>
    NoneOn,
}
