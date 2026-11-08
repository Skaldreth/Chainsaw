namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// A case in memory: it has the tables of the modules that seal into one sealed chain, so that a module's lookups
/// answer, pace and seal their records exactly as in a real case, and nothing more. It has no file, no journal and no
/// key, and is gone when it is disposed. What is sealed in it is brought into a real case by the module that owns the
/// records (for the chains' raw responses, <c>IResponseTransfer</c>), where it is sealed for real.
/// </summary>
/// <remarks>
/// Used where something must be fetched before there is a case to keep it in: one round of the background watching
/// (CLAUDE.md, <em>Bevaka</em>), and the start points that Nytt ärende fetches before the case is created (sketch
/// NC2a). It never touches a real case, the vault or the disk.
/// </remarks>
public interface IMemoryCase : ICase
{
    /// <summary>Gives a mark for <see cref="SealedSince"/>: how many records are sealed now.</summary>
    /// <returns>The mark.</returns>
    int Mark();

    /// <summary>Gives the records of one sealed chain sealed after a mark, oldest first.</summary>
    /// <param name="chain">The sealed chain's name.</param>
    /// <param name="mark">The mark, from <see cref="Mark"/>; 0 for every record.</param>
    /// <returns>Their numbers.</returns>
    IReadOnlyList<long> SealedSince(string chain, int mark);
}

/// <summary>Makes cases in memory (<see cref="IMemoryCase"/>).</summary>
public interface IMemoryCases
{
    /// <summary>
    /// Makes a case in memory with the tables of every module that seals into a sealed chain, and no others.
    /// </summary>
    /// <param name="sealedChain">The sealed chain, for example the chains' raw responses.</param>
    /// <returns>The case, which the caller disposes.</returns>
    IMemoryCase Create(string sealedChain);
}
