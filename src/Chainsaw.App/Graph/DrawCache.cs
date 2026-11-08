namespace Chainsaw.App.Graph;

/// <summary>
/// Keeps what the graph's drawing makes again and again (pens, measured texts) by what it is made from, so that a
/// frame of the fan, the selection view or the amounts makes nothing it made in the frame before (principle 6). No
/// WPF, so that the rule is tested; the canvas gives it its own keys and values.
/// </summary>
/// <remarks>
/// The cache is bounded: when it is full it starts over, which is cheap and predictable, since a frame makes at most a
/// few hundred values and the keys that change with the zoom or an animation would otherwise pile up. A value must
/// never be changed after it is given out, as every frame shares it.
/// </remarks>
/// <typeparam name="TKey">What a value is made from.</typeparam>
/// <typeparam name="TValue">The value.</typeparam>
public sealed class DrawCache<TKey, TValue>
    where TKey : notnull
{
    /// <summary>How many values the cache holds when none is given.</summary>
    public const int DefaultCapacity = 2048;

    /// <summary>The values made so far, by what they are made from.</summary>
    private readonly Dictionary<TKey, TValue> _values;

    /// <summary>Creates an empty cache.</summary>
    /// <param name="capacity">How many values it holds before it starts over, at least one.</param>
    /// <param name="comparer">How keys are compared, or <see langword="null"/> for their own equality.</param>
    public DrawCache(int capacity = DefaultCapacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _values = new Dictionary<TKey, TValue>(comparer);
    }

    /// <summary>Gets how many values the cache holds before it starts over.</summary>
    public int Capacity { get; }

    /// <summary>Gets how many values it holds now.</summary>
    public int Count => _values.Count;

    /// <summary>Gets how many times a value had to be made, for the tests.</summary>
    public long Made { get; private set; }

    /// <summary>Gives the value made from a key, making it the first time.</summary>
    /// <param name="key">What it is made from.</param>
    /// <param name="make">Makes it.</param>
    /// <returns>The value.</returns>
    public TValue Get(TKey key, Func<TKey, TValue> make)
    {
        ArgumentNullException.ThrowIfNull(make);
        if (_values.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_values.Count >= Capacity)
        {
            _values.Clear();
        }

        value = make(key);
        _values[key] = value;
        Made++;
        return value;
    }

    /// <summary>Forgets every value, as when the screen's scale changes and the texts must be measured again.</summary>
    public void Clear() => _values.Clear();
}
