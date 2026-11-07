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

    /// <summary>No endpoint of the chain is on; nothing was sent.</summary>
    NoneOn,
}
