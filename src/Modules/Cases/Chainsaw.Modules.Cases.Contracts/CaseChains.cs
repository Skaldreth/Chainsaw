namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>The names of the Cases module's own sealed chains, as <see cref="ICaseVerifier"/> reports them.</summary>
public static class CaseChains
{
    /// <summary>The journal, which every recorded step in the case adds a sealed line to.</summary>
    public const string Journal = "cases.journal";
}
