namespace BlueHarvest.Sim.Events;

/// <summary>
/// A committed event (contracts §4): its ID in the session, the world revision of the request that made it, when it
/// happened, what led to it, and what it says. Events from one request share a revision; IDs are never reused.
/// </summary>
public sealed class SimEvent
{
    private IReadOnlyList<string>? _subjects;

    public SimEvent(ulong eventId, ulong worldRevision, long tick, string? cause, SimEventPayload payload)
    {
        EventId = eventId;
        WorldRevision = worldRevision;
        Tick = tick;
        Cause = cause;
        Payload = payload;
    }

    /// <summary>The session's sequence number for this event, from 1.</summary>
    public ulong EventId { get; }

    /// <summary>The revision the world reached when the request that made this event committed.</summary>
    public ulong WorldRevision { get; }

    /// <summary>The tick it happened at.</summary>
    public long Tick { get; }

    /// <summary>The event's type, dotted and lower case (<c>item.picked-up</c>).</summary>
    public string Type => Payload.Type;

    /// <summary>Who did it (<c>actor:3</c>), or null.</summary>
    public string? Actor => Payload.Who is { } who ? SimEventPayload.ActorRef(who) : null;

    /// <summary>What it was done to, as instance IDs.</summary>
    public IReadOnlyList<string> Subjects => _subjects ??= Payload.Subjects();

    /// <summary>
    /// What led to it: the request ID of the request that made it (<see cref="WorldSession.CommitOnce"/>), or
    /// <c>event:&lt;id&gt;</c> when an observer of that event made the request; null for the world simply running.
    /// </summary>
    public string? Cause { get; }

    /// <summary>The rest, typed.</summary>
    public SimEventPayload Payload { get; }

    public override string ToString() => $"#{EventId} r{WorldRevision} t{Tick} {Type}";
}
