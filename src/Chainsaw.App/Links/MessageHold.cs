namespace Chainsaw.App.Links;

/// <summary>
/// Holds the messages that arrive before anyone receives them, so that a link sent by a second start while the first
/// Chainsaw is still building its host is kept and handed over, in order, once the receiver is there. Thread-safe:
/// messages arrive on the pipe's thread.
/// </summary>
/// <param name="capacity">The most messages held; more are dropped, so that a flood cannot fill the memory.</param>
public sealed class MessageHold(int capacity)
{
    /// <summary>Guards the fields below.</summary>
    private readonly Lock _gate = new();

    /// <summary>The messages that wait for the receiver.</summary>
    private readonly Queue<string> _held = new();

    /// <summary>The receiver once it is there.</summary>
    private Action<string>? _receiver;

    /// <summary>Gets how many messages wait.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _held.Count;
            }
        }
    }

    /// <summary>Hands a message to the receiver, or holds it until there is one.</summary>
    /// <param name="message">The message.</param>
    public void Arrive(string message)
    {
        Action<string>? receiver;
        lock (_gate)
        {
            receiver = _receiver;
            if (receiver is null)
            {
                if (_held.Count < capacity)
                {
                    _held.Enqueue(message);
                }

                return;
            }
        }

        receiver(message);
    }

    /// <summary>Sets the receiver and hands it the messages held so far, in the order they came.</summary>
    /// <param name="receiver">The receiver.</param>
    public void Attach(Action<string> receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        lock (_gate)
        {
            // Delivered under the gate, so that a message that arrives meanwhile cannot overtake the held ones.
            while (_held.TryDequeue(out var message))
            {
                receiver(message);
            }

            _receiver = receiver;
        }
    }
}
