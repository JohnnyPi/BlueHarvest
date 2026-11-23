using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Events;

namespace BlueHarvest.Sim;

/// <summary>
/// The one owner of a loaded world (contracts §6, P3a): its content registry and item catalog, the surface
/// simulation, the world revision, the event ledger and the requests already processed. The game, the runner and the
/// tests make a session from a registry and then the simulation in it; nothing reads items or events from anywhere else.
/// <para>
/// <b>Requests.</b> Every change to the world is a request: one actor's turn (<see cref="Simulation.Step"/>), the world
/// moving on to a time (<see cref="Simulation.RunUntil"/>), a call from outside, or one submitted here. Its events are
/// held until it finishes, then committed together: each gets the next <see cref="SimEvent.EventId"/>, all share the
/// next <see cref="WorldRevision"/>, they go into <see cref="Events"/>, and then the observers see them stage by stage
/// (<see cref="ObserverStage"/>), the simulation's C# events last of all. So whoever listens sees the request's final
/// state: a bite's injury and the death it caused arrive together, after both have happened. Anything an observer
/// changes is a new request, run after the current one has been seen by everyone.
/// </para>
/// <para>
/// Still in the simulation for now, reached through <see cref="Surface"/>: the clock, the RNG, the actors and the item
/// ledger. P3b moves what the save needs.
/// </para>
/// </summary>
public sealed class WorldSession
{
    private static readonly ObserverStage[] Stages = Enum.GetValues<ObserverStage>();

    private readonly List<Action<SimEvent>>[] _observers;
    private readonly List<(SimEventPayload Payload, long Tick)> _pending = new();
    private readonly Queue<(Action Request, string? Cause)> _queued = new();
    private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
    private readonly List<string> _processedOrder = new();
    private Simulation? _surface;
    private int _depth;
    private bool _committing;
    private string? _cause;
    private SimEvent? _observing;

    /// <summary>A session on this registry's content. Throws if its items can't be used by the sim.</summary>
    public WorldSession(ContentRegistry content, int eventCapacity = EventLog.DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        Catalog = ItemCatalog.For(content);
        Events = new EventLog(eventCapacity);
        _observers = Stages.Select(_ => new List<Action<SimEvent>>()).ToArray();
    }

    /// <summary>The content the session plays with.</summary>
    public ContentRegistry Content { get; }

    /// <summary>The content's fingerprint: what a save is locked to.</summary>
    public string Fingerprint => Content.Fingerprint;

    /// <summary>Every kind of item, from <see cref="Content"/>.</summary>
    public ItemCatalog Catalog { get; }

    /// <summary>Whether the surface simulation has been made yet.</summary>
    public bool HasSurface => _surface is not null;

    /// <summary>The island's surface: the simulation made in this session. Throws if there's none yet.</summary>
    public Simulation Surface => _surface ?? throw new InvalidOperationException("This session has no simulation yet.");

    /// <summary>The current tick (0 before the simulation exists).</summary>
    public long Tick => _surface?.CurrentTick ?? 0;

    /// <summary>
    /// Goes up by one each time a request commits events; the events of one request share it. A request that changed
    /// nothing anyone is told about (an actor standing still) doesn't move it.
    /// </summary>
    public ulong WorldRevision { get; private set; }

    /// <summary>The last event ID handed out (0 before any).</summary>
    public ulong LastEventId { get; private set; }

    /// <summary>The event ledger.</summary>
    public EventLog Events { get; }

    /// <summary>The request IDs already committed by <see cref="CommitOnce"/>, in order: P3b saves them.</summary>
    public IReadOnlyList<string> ProcessedRequests => _processedOrder;

    /// <summary>Whether a request with this ID has been committed.</summary>
    public bool IsProcessed(string requestId) => _processed.Contains(requestId);

    internal void Attach(Simulation simulation)
    {
        if (_surface is not null)
            throw new InvalidOperationException("This session already has a simulation: make a new session for each one.");
        _surface = simulation;
    }

    /// <summary>
    /// Sees every committed event at a stage, in commit order, after the earlier stages have seen the whole request.
    /// Dispose the result to stop.
    /// </summary>
    public IDisposable Observe(ObserverStage stage, Action<SimEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var list = _observers[(int)stage];
        list.Add(observer);
        return new Subscription(() => list.Remove(observer));
    }

    /// <summary>
    /// Runs a change to the world as a request of its own. Submitted while another request is running or being
    /// observed, it waits and runs after that one has been seen by every observer; otherwise it runs now.
    /// </summary>
    public void Submit(Action request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Submit(request, null);
    }

    /// <summary>
    /// Runs a request that must happen only once (a reward paid, a death recorded, an extraction): if a request with
    /// this ID has already been committed this does nothing and returns false. Its events give the ID as their
    /// <see cref="SimEvent.Cause"/>. The ID is recorded as soon as the request is accepted.
    /// </summary>
    public bool CommitOnce(string requestId, Action request)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestId);
        ArgumentNullException.ThrowIfNull(request);
        if (!_processed.Add(requestId))
            return false;
        _processedOrder.Add(requestId);
        Submit(request, requestId);
        return true;
    }

    private void Submit(Action request, string? cause)
    {
        if (_depth > 0 || _committing)
        {
            _queued.Enqueue((request, cause ?? ObservedCause));
            return;
        }
        BeginRequest(cause);
        try
        {
            request();
        }
        finally
        {
            EndRequest();
        }
    }

    /// <summary>The cause of a request made while an event is being observed.</summary>
    private string? ObservedCause => _observing is { } e ? $"event:{e.EventId}" : null;

    /// <summary>Starts a request (they nest: the outermost one commits).</summary>
    internal void BeginRequest(string? cause = null)
    {
        if (_depth++ == 0 && _pending.Count == 0)
            _cause = cause ?? ObservedCause;
    }

    /// <summary>Ends a request; the outermost commits its events.</summary>
    internal void EndRequest()
    {
        if (_depth == 0)
            throw new InvalidOperationException("No request to end.");
        if (--_depth == 0)
            Commit();
    }

    /// <summary>Holds an event for the request it belongs to; outside any request it commits at once.</summary>
    internal void Emit(SimEventPayload payload, long tick)
    {
        if (_pending.Count == 0 && _depth == 0)
            _cause = ObservedCause;
        _pending.Add((payload, tick));
        if (_depth == 0)
            Commit();
    }

    private void Commit()
    {
        if (_committing)
            return; // the loop below picks it up
        _committing = true;
        try
        {
            while (true)
            {
                if (_pending.Count > 0)
                {
                    CommitPending();
                    continue;
                }
                if (_queued.TryDequeue(out var next))
                {
                    _depth++;
                    _cause = next.Cause;
                    try
                    {
                        next.Request();
                    }
                    finally
                    {
                        _depth--;
                    }
                    continue;
                }
                break;
            }
        }
        finally
        {
            _committing = false;
            _observing = null;
        }
    }

    private void CommitPending()
    {
        var batch = _pending.ToArray();
        _pending.Clear();
        string? cause = _cause;
        _cause = null;
        WorldRevision++;
        var events = new SimEvent[batch.Length];
        for (int i = 0; i < batch.Length; i++)
        {
            events[i] = new SimEvent(++LastEventId, WorldRevision, batch[i].Tick, cause, batch[i].Payload);
            Events.Append(events[i]);
        }
        foreach (ObserverStage stage in Stages)
        {
            var observers = _observers[(int)stage].ToArray(); // an observer may stop observing as it goes
            foreach (SimEvent e in events)
            {
                _observing = e;
                if (stage == ObserverStage.Presentation)
                    _surface?.Present(e.Payload);
                foreach (var observer in observers)
                    observer(e);
            }
        }
        _observing = null;
    }

    private sealed class Subscription(Action stop) : IDisposable
    {
        private Action? _stop = stop;

        public void Dispose()
        {
            _stop?.Invoke();
            _stop = null;
        }
    }
}
