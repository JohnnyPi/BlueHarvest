namespace BlueHarvest.Sim.Time;

/// <summary>
/// Orders turns by tick. Actors due on the same tick act in the order they were scheduled,
/// which keeps every run deterministic.
/// </summary>
public sealed class Scheduler
{
    private readonly PriorityQueue<int, (long Tick, long Sequence)> _queue = new();
    private long _nextSequence;

    public int Count => _queue.Count;

    /// <summary>Every turn waiting, with its tick and place in line, and the next place in line (for a snapshot).</summary>
    internal (IReadOnlyList<(int ActorId, long Tick, long Sequence)> Queue, long NextSequence) Capture() =>
        (_queue.UnorderedItems.Select(e => (e.Element, e.Priority.Tick, e.Priority.Sequence))
            .OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToList(), _nextSequence);

    /// <summary>Puts back a captured line exactly (a loaded snapshot).</summary>
    internal void Restore(IEnumerable<(int ActorId, long Tick, long Sequence)> queue, long nextSequence)
    {
        _queue.Clear();
        foreach (var (actorId, tick, sequence) in queue)
        {
            if (sequence >= nextSequence)
                throw new ArgumentException($"Turn {sequence} is past the next place in line, {nextSequence}.", nameof(queue));
            _queue.Enqueue(actorId, (tick, sequence));
        }
        _nextSequence = nextSequence;
    }

    public void Schedule(int actorId, long tick) => _queue.Enqueue(actorId, (tick, _nextSequence++));

    public bool TryPeek(out int actorId, out long tick)
    {
        if (_queue.TryPeek(out actorId, out var priority))
        {
            tick = priority.Tick;
            return true;
        }
        tick = 0;
        return false;
    }

    public bool TryPeekTick(out long tick) => TryPeek(out _, out tick);

    public bool TryDequeue(out int actorId, out long tick)
    {
        if (_queue.TryDequeue(out actorId, out var priority))
        {
            tick = priority.Tick;
            return true;
        }
        tick = 0;
        return false;
    }
}
