using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>An endpoint's turn: when its request may leave, and how long the caller waits for it.</summary>
/// <param name="Endpoint">The endpoint whose turn it is.</param>
/// <param name="StartsAt">When its request may leave, in UTC.</param>
/// <param name="Wait">How long from now, never negative.</param>
internal sealed record EndpointTurn(PlannedEndpoint Endpoint, DateTimeOffset StartsAt, TimeSpan Wait);

/// <summary>
/// Each endpoint's own pace (decided 2026-11-07: rate limits are always respected). A request takes a turn: the
/// earliest time the endpoint's pace allows and it has not asked Chainsaw to wait (<c>429</c>, <c>Retry-After</c>).
/// Turns are taken in advance, so that lookups that run at the same time line up instead of asking at once, and a
/// lookup that may ask several endpoints takes the one whose turn comes first, so that the lookups are spread over
/// them rather than asking one too fast. The members are thread-safe.
/// </summary>
/// <param name="clock">The time now.</param>
internal sealed class EndpointPacer(IClock clock)
{
    /// <summary>Guards the times.</summary>
    private readonly Lock _gate = new();

    /// <summary>When each endpoint's next request may leave by its pace.</summary>
    private readonly Dictionary<Guid, DateTimeOffset> _next = [];

    /// <summary>Until when each endpoint asked Chainsaw to wait.</summary>
    private readonly Dictionary<Guid, DateTimeOffset> _waitUntil = [];

    /// <summary>
    /// Takes the turn that comes first among endpoints, the earlier in the list on a tie, unless even that one is
    /// further away than the caller will wait; then nothing is taken.
    /// </summary>
    /// <param name="endpoints">The endpoints that may be asked, in the order of the chain's mode.</param>
    /// <param name="longest">The longest the caller waits.</param>
    /// <returns>The turn, or <see langword="null"/> and the earliest time one comes when it is too far away.</returns>
    public (EndpointTurn? Turn, DateTimeOffset? Earliest) Take(
        IReadOnlyList<PlannedEndpoint> endpoints, TimeSpan longest)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        lock (_gate)
        {
            var now = clock.UtcNow;
            PlannedEndpoint? first = null;
            var earliest = DateTimeOffset.MaxValue;
            foreach (var planned in endpoints)
            {
                var ready = ReadyAt(planned.Endpoint.Id, now);
                if (ready < earliest)
                {
                    (first, earliest) = (planned, ready);
                }
            }

            if (first is null)
            {
                return (null, null);
            }

            if (earliest - now > longest)
            {
                return (null, earliest);
            }

            _next[first.Endpoint.Id] = earliest + first.Endpoint.Interval;
            return (new EndpointTurn(first, earliest, earliest - now), null);
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
