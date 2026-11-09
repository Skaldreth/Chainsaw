namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>The rules about where a label comes from, written once.</summary>
public static class LabelSources
{
    /// <summary>
    /// Says whether a label from a source is the user's own judgement: set with the form, as own, from an open source,
    /// or from an analysis of behaviour that the user approved as a label (sketch TJ1b; a behaviour label is only ever
    /// set by the user, never by an analysis on its own). The user can take it back everywhere and mark it
    /// <em>Sprid inte</em>.
    /// </summary>
    /// <param name="source">The label's source.</param>
    /// <returns><see langword="true"/> when it is the user's own.</returns>
    public static bool IsUsersOwn(LabelSource source) =>
        source is LabelSource.Own or LabelSource.OpenSource or LabelSource.Behaviour;
}
