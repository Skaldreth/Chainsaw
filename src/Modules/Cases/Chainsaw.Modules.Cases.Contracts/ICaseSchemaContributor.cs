namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// A module's tables in the case file. Every module that stores something in a case registers one contributor in
/// the container; when a case is opened, the steps it has not run yet are run once, in order, each in its own
/// transaction, and the module's version in the case is raised.
/// </summary>
/// <remarks>
/// Before 1.0 the steps may be merged and renumbered; old test cases need not open. A case whose version for a module
/// is higher than the module's last step was written by a newer build and is refused.
/// </remarks>
public interface ICaseSchemaContributor
{
    /// <summary>
    /// Gets the module's stable name, for example <c>chains</c>: lower-case ASCII letters, digits and <c>_</c>,
    /// starting with a letter.
    /// </summary>
    string Module { get; }

    /// <summary>Gets the steps, with strictly increasing versions from 1.</summary>
    IReadOnlyList<CaseSchemaStep> Steps { get; }

    /// <summary>Gets the module's sealed tables, which the steps create; empty when the module has none.</summary>
    IReadOnlyList<SealedChain> SealedChains { get; }
}
