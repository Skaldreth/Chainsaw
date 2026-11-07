namespace Chainsaw.Modules.Checks;

/// <summary>Joins items into groups: two items joined, directly or through others, are in the same group.</summary>
/// <typeparam name="T">The kind of item.</typeparam>
internal sealed class UnionFind<T>
    where T : notnull
{
    /// <summary>Each item's parent; a group's root is its own parent.</summary>
    private readonly Dictionary<T, T> _parent = [];

    /// <summary>The items in the order they were first seen, so that the groups come out in a stable order.</summary>
    private readonly List<T> _order = [];

    /// <summary>Adds an item in a group of its own, unless it is known.</summary>
    /// <param name="item">The item.</param>
    public void Add(T item)
    {
        if (_parent.TryAdd(item, item))
        {
            _order.Add(item);
        }
    }

    /// <summary>Joins two items' groups.</summary>
    /// <param name="first">One item.</param>
    /// <param name="second">The other.</param>
    public void Union(T first, T second)
    {
        Add(first);
        Add(second);
        var (a, b) = (Root(first), Root(second));
        if (!EqualityComparer<T>.Default.Equals(a, b))
        {
            _parent[b] = a;
        }
    }

    /// <summary>Gives the groups, each in the order its items were first seen.</summary>
    /// <returns>The groups, ordered by their first item.</returns>
    public IReadOnlyList<IReadOnlyList<T>> Groups() =>
    [
        .. _order.GroupBy(Root).Select(group => (IReadOnlyList<T>)[.. group]),
    ];

    /// <summary>Gives an item's root, shortening the path on the way.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The root.</returns>
    private T Root(T item)
    {
        var root = item;
        while (!EqualityComparer<T>.Default.Equals(_parent[root], root))
        {
            root = _parent[root];
        }

        while (!EqualityComparer<T>.Default.Equals(_parent[item], root))
        {
            (item, _parent[item]) = (_parent[item], root);
        }

        return root;
    }
}
