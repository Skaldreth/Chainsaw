namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>How Chainsaw chooses among a chain's endpoints (CLAUDE.md, <em>Källornas adresser hanteras per kedja</em>).</summary>
public enum SourceMode
{
    /// <summary>
    /// The default: the endpoints are tried at the start (and with <em>Prova alla nu</em>) by asking only for the
    /// chain's height, and the fastest that answers right is used; one that lags behind the others in height is not.
    /// </summary>
    Fastest,

    /// <summary>The endpoints in the user's order; the next is taken when one fails or does not answer in time.</summary>
    Priority,

    /// <summary>No lookups for the chain: the case answers, and the graph says <em>Av</em>.</summary>
    Off,
}
