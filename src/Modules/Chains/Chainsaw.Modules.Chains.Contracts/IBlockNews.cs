using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The chains' news in real time, where a source has it (mempool.space's websocket says when a new block comes): the
/// watching then asks at once, instead of waiting for its next ask for the height. The news is only a nudge: it is
/// never stored, shown or believed, and every check still asks the chain's height and the addresses through the
/// ordinary lookups, which are sealed in the case (principle 1). Where no source has news, the watching asks as before
/// (the fallback). Nothing about a case is sent: only that new blocks are wanted, through the way out (principle 4).
/// </summary>
public interface IBlockNews
{
    /// <summary>
    /// Gets how many new blocks have been heard of on a chain since Chainsaw started; it only grows. A watcher that sees
    /// it change asks the chain at once.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The count; 0 while nothing has been heard.</returns>
    long Heard(Chain chain);

    /// <summary>
    /// Keeps listening to a chain for a while (a round's worth and a margin), starting when it does not listen. Called
    /// each round while the watching may look the chain up; a chain no longer kept stops listening by itself.
    /// </summary>
    /// <param name="chain">The chain.</param>
    void Keep(Chain chain);

    /// <summary>Stops listening to a chain at once, as when the watching may no longer look it up.</summary>
    /// <param name="chain">The chain.</param>
    void Release(Chain chain);
}
