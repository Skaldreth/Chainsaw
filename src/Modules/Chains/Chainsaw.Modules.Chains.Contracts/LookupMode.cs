namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>Whether a lookup may ask the source.</summary>
public enum LookupMode
{
    /// <summary>
    /// Ask the source when the answer may have changed or is missing; answer from the case when it cannot have
    /// changed. The default.
    /// </summary>
    AsNeeded,

    /// <summary>
    /// Answer only from the case and never ask the source, for example when a graph is shown again. What may have
    /// changed comes as <see cref="LookupStatus.FromCaseMayBeOutdated"/>, and what is missing as
    /// <see cref="LookupStatus.NotInCase"/>; nothing is sent.
    /// </summary>
    CaseOnly,

    /// <summary>
    /// As <see cref="AsNeeded"/>, but the source is asked only through an endpoint in step with the chain: one whose
    /// latest probe answered with the highest height of its round, not even one block behind. For the monitoring
    /// (decided 2026-11-07), which must not miss the newest block; the other lookups accept an endpoint one block
    /// behind, since the information comes in step by step. With no endpoint known to be in step nothing is sent and
    /// the lookup says so (try the endpoints first, <see cref="IChainSources.ProbeAsync"/>).
    /// </summary>
    InStep,
}
