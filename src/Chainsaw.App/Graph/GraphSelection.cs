using System.Collections;

namespace Chainsaw.App.Graph;

/// <summary>
/// The actors picked in the graph, with Ctrl+click or with a box drawn on the empty surface: an ordered set, so that
/// the pick keeps the order the user picked in while asking whether an actor is picked costs nothing, also when the
/// surface draws every node in every frame (principle 6). Delete throws what is picked and Z draws an own area around
/// it.
/// </summary>
public sealed class GraphSelection : IReadOnlyList<string>
{
    /// <summary>The picked actors in the order they were picked.</summary>
    private readonly List<string> _order = [];

    /// <summary>The same actors, for asking.</summary>
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);

    /// <summary>Gets how many actors are picked.</summary>
    public int Count => _order.Count;

    /// <summary>Gets the actor picked as number <paramref name="index"/>.</summary>
    /// <param name="index">The place in the order.</param>
    /// <returns>The actor's key.</returns>
    public string this[int index] => _order[index];

    /// <summary>Says whether an actor is picked.</summary>
    /// <param name="key">The actor's key.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(string key) => _set.Contains(key);

    /// <summary>Picks an actor, last; nothing when it is picked already.</summary>
    /// <param name="key">The actor's key.</param>
    /// <returns><see langword="true"/> when it was not picked before.</returns>
    public bool Add(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_set.Add(key))
        {
            return false;
        }

        _order.Add(key);
        return true;
    }

    /// <summary>Takes an actor out of the pick.</summary>
    /// <param name="key">The actor's key.</param>
    /// <returns><see langword="true"/> when it was picked.</returns>
    public bool Remove(string key)
    {
        if (!_set.Remove(key))
        {
            return false;
        }

        _order.Remove(key);
        return true;
    }

    /// <summary>Picks an actor that is not picked, and takes back one that is (Ctrl+click).</summary>
    /// <param name="key">The actor's key.</param>
    public void Toggle(string key)
    {
        if (!Remove(key))
        {
            Add(key);
        }
    }

    /// <summary>Forgets the whole pick.</summary>
    public void Clear()
    {
        _order.Clear();
        _set.Clear();
    }

    /// <summary>Makes the pick exactly the given actors, in their order.</summary>
    /// <param name="keys">The actors.</param>
    /// <returns><see langword="true"/> when the pick changed.</returns>
    public bool SetTo(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (SameAs(keys))
        {
            return false;
        }

        Clear();
        for (var index = 0; index < keys.Count; index++)
        {
            Add(keys[index]);
        }

        return true;
    }

    /// <summary>Keeps only the actors that a rule keeps, in their order.</summary>
    /// <param name="keep">Says whether an actor stays picked.</param>
    public void KeepWhere(Func<string, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        _order.RemoveAll(key => !keep(key));
        _set.RemoveWhere(key => !keep(key));
    }

    /// <summary>Gives the picked actors in their order.</summary>
    /// <returns>The enumerator.</returns>
    public List<string>.Enumerator GetEnumerator() => _order.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<string> IEnumerable<string>.GetEnumerator() => _order.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => _order.GetEnumerator();

    /// <summary>Says whether the pick is exactly the given actors, in the same order.</summary>
    /// <param name="keys">The actors.</param>
    /// <returns><see langword="true"/> when nothing would change.</returns>
    private bool SameAs(IReadOnlyList<string> keys)
    {
        if (keys.Count != _order.Count)
        {
            return false;
        }

        for (var index = 0; index < keys.Count; index++)
        {
            if (!string.Equals(keys[index], _order[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
