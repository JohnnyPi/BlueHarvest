namespace BlueHarvest.Sim.Events;

/// <summary>
/// The session's event ledger: every committed event in order. It keeps the most recent <see cref="Capacity"/> in
/// memory and counts the rest; P3b decides what the save keeps.
/// </summary>
public sealed class EventLog
{
    /// <summary>How many recent events are kept by default.</summary>
    public const int DefaultCapacity = 8192;

    private readonly Queue<SimEvent> _recent = new();

    public EventLog(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>How many of the latest events are kept.</summary>
    public int Capacity { get; }

    /// <summary>How many events have been committed in all.</summary>
    public long Count { get; private set; }

    /// <summary>The latest events, oldest first.</summary>
    public IReadOnlyCollection<SimEvent> Recent => _recent;

    /// <summary>The last committed event, or null.</summary>
    public SimEvent? Last { get; private set; }

    internal void Append(SimEvent e)
    {
        if (Last is { } last && (e.EventId <= last.EventId || e.WorldRevision < last.WorldRevision))
            throw new InvalidOperationException($"Event {e} comes before {last}.");
        if (_recent.Count == Capacity)
            _recent.Dequeue();
        _recent.Enqueue(e);
        Last = e;
        Count++;
    }
}
