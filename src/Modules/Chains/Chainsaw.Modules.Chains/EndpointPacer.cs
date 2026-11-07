using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>An endpoint's turn: when its request may leave, and how long the caller waits for it.</summary>
/// <param name="Endpoint">The endpoint whose turn it is.</param>
/// <param name="StartsAt">When its request may leave, in UTC.</param>
/// <param name="Wait">How long from now, never negative.</param>
/// <param name="Previous">The endpoint's next free time before the turn was taken, so that it can be given
/// back.</param>
internal sealed record EndpointTurn(
    PlannedEndpoint Endpoint, DateTimeOffset StartsAt, TimeSpan Wait, DateTimeOffset? Previous);

/// <summary>Why no turn was taken.</summary>
/// <param name="Earliest">When the earliest turn comes, in UTC.</param>
/// <param name="SourceAsked">
/// Whether that is because a source asked Chainsaw to wait; otherwise Chainsaw's own pace holds the turns.
/// </param>
internal sealed record NoTurn(DateTimeOffset Earliest, bool SourceAsked);

/// <summary>
/// Each endpoint's own pace (decided 2026-11-07: rate limits are always respected). A request takes a turn: the
/// earliest time the endpoint's pace allows and it has not asked Chainsaw to wait (<c>429</c>, <c>Retry-After</c>).
/// Turns are taken in advance, so that lookups that run at the same time line up instead of asking at once; a turn
/// that is not used is given back. A lookup that may ask several endpoints takes the one whose turn comes first among
/// the best group (<see cref="PlannedEndpoint.Tier"/>), so that the lookups are spread over the endpoints that answer
/// right rather than asking one too fast; a worse group is asked only when the best one's turn is further away than
/// <see cref="Patience"/> (it asked to wait, or its queue is long). The members are thread-safe.
/// </summary>
/// <param name="clock">The time now.</param>
internal sealed class EndpointPacer(IClock clock)
{
    /// <summary>How long a lookup waits for the best group's turn before a worse group is asked.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>Guards the times.</summary>
    private readonly Lock _gate = new();

    /// <summary>When each endpoint's next request may leave by its pace.</summary>
    private readonly Dictionary<Guid, DateTimeOffset> _next = [];

    /// <summary>Until when each endpoint asked Chainsaw to wait.</summary>
    private readonly Dictionary<Guid, DateTimeOffset> _waitUntil = [];

    /// <summary>
    /// Takes a turn: the one that comes first in the best group whose turn is within <see cref="Patience"/>, the
    /// earlier in the list on a tie; when no group's is, the one that comes first of all, unless even that one is
    /// further away than the caller will wait. Then nothing is taken.
    /// </summary>
    /// <param name="endpoints">The endpoints that may be asked, in the order of the chain's mode.</param>
    /// <param name="longest">The longest the caller waits.</param>
    /// <returns>The turn, or why none was taken; both <see langword="null"/> for no endpoints.</returns>
    public (EndpointTurn? Turn, NoTurn? None) Take(IReadOnlyList<PlannedEndpoint> endpoints, TimeSpan longest)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        lock (_gate)
        {
            var now = clock.UtcNow;
            if (endpoints.Count == 0)
            {
                return (null, null);
            }

            var chosen = endpoints
                .GroupBy(planned => planned.Tier)
                .OrderBy(group => group.Key)
                .Select(group => Earliest(group, now))
                .FirstOrDefault(found => found.Ready - now <= Patience);
            if (chosen.Endpoint is null)
            {
                chosen = Earliest(endpoints, now);
            }

            var (first, ready) = (chosen.Endpoint!, chosen.Ready);
            if (ready - now > longest)
            {
                var asked = _waitUntil.TryGetValue(first.Endpoint.Id, out var until) && until >= ready;
                return (null, new NoTurn(ready, asked));
            }

            DateTimeOffset? previous = _next.TryGetValue(first.Endpoint.Id, out var next) ? next : null;
            _next[first.Endpoint.Id] = ready + first.Endpoint.Interval;
            return (new EndpointTurn(first, ready, ready - now, previous), null);
        }
    }

    /// <summary>
    /// Gives back a turn that was not used (the lookup was cancelled, nothing could be sent, or the source asked to
    /// wait meanwhile), when no later turn has been taken since, so that a cancelled queue never holds the next
    /// lookups back.
    /// </summary>
    /// <param name="turn">The turn.</param>
    public void Release(EndpointTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        lock (_gate)
        {
            var id = turn.Endpoint.Endpoint.Id;
            if (!_next.TryGetValue(id, out var next) || next != turn.StartsAt + turn.Endpoint.Endpoint.Interval)
            {
                return;
            }

            if (turn.Previous is { } previous)
            {
                _next[id] = previous;
            }
            else
            {
                _next.Remove(id);
            }
        }
    }

    /// <summary>
    /// Notes that an endpoint asked Chainsaw to wait. A later time than one already noted replaces it; an earlier one
    /// does not shorten it.
    /// </summary>
    /// <param name="id">The endpoint.</param>
    /// <param name="wait">How long, from now.</param>
    /// <returns>Until when it is waited for, in UTC.</returns>
    public DateTimeOffset AskedToWait(Guid id, TimeSpan wait)
    {
        lock (_gate)
        {
            var until = clock.UtcNow + (wait < TimeSpan.Zero ? TimeSpan.Zero : wait);
            if (_waitUntil.TryGetValue(id, out var noted) && noted > until)
            {
                return noted;
            }

            _waitUntil[id] = until;
            return until;
        }
    }

    /// <summary>Gets until when an endpoint asked Chainsaw to wait, when that is still ahead.</summary>
    /// <param name="id">The endpoint.</param>
    /// <returns>The time in UTC, or <see langword="null"/>.</returns>
    public DateTimeOffset? WaitUntil(Guid id)
    {
        lock (_gate)
        {
            return _waitUntil.TryGetValue(id, out var until) && until > clock.UtcNow ? until : null;
        }
    }

    /// <summary>Forgets an endpoint that has been removed.</summary>
    /// <param name="id">The endpoint.</param>
    public void Forget(Guid id)
    {
        lock (_gate)
        {
            _next.Remove(id);
            _waitUntil.Remove(id);
        }
    }

    /// <summary>Finds the endpoint whose turn comes first, the earlier in the list on a tie, under the lock.</summary>
    /// <param name="endpoints">The endpoints.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The endpoint and when its turn comes; no endpoint when there are none.</returns>
    private (PlannedEndpoint? Endpoint, DateTimeOffset Ready) Earliest(
        IEnumerable<PlannedEndpoint> endpoints, DateTimeOffset now)
    {
        (PlannedEndpoint? Endpoint, DateTimeOffset Ready) first = (null, DateTimeOffset.MaxValue);
        foreach (var planned in endpoints)
        {
            var ready = ReadyAt(planned.Endpoint.Id, now);
            if (ready < first.Ready)
            {
                first = (planned, ready);
            }
        }

        return first;
    }

    /// <summary>Gives when an endpoint's next request may leave, under the lock.</summary>
    /// <param name="id">The endpoint.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The time, never before now.</returns>
    private DateTimeOffset ReadyAt(Guid id, DateTimeOffset now)
    {
        var ready = now;
        if (_next.TryGetValue(id, out var next) && next > ready)
        {
            ready = next;
        }

        if (_waitUntil.TryGetValue(id, out var until) && until > ready)
        {
            ready = until;
        }

        return ready;
    }
}
