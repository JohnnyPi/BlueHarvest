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
