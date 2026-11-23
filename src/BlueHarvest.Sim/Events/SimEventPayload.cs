using BlueHarvest.Sim.Actors;

namespace BlueHarvest.Sim.Events;

/// <summary>
/// What an event says, typed (contracts §4): one record per kind of event, in <c>SimEventPayloads.cs</c>. The
/// envelope (<see cref="SimEvent"/>) adds the ID, revision, tick and cause when the event commits.
/// </summary>
public abstract record SimEventPayload
{
    /// <summary>The event's type, dotted and lower case: <c>item.picked-up</c>, <c>actor.died</c>.</summary>
    public abstract string Type { get; }

    /// <summary>Who did it, or who it happened to when nobody else did (the one who died, fell or woke): or nobody.</summary>
    public virtual Actor? Who => null;

    /// <summary>The instance IDs of what it was done to (<c>actor:3</c>, <c>item:17</c>), besides <see cref="Who"/>.</summary>
    public IReadOnlyList<string> Subjects()
    {
        var subjects = new List<string>();
        AddSubjects(subjects);
        return subjects;
    }

    /// <summary>Adds the instance IDs of the other actors and items the event names.</summary>
    protected virtual void AddSubjects(List<string> subjects)
    {
    }

    protected static void Add(List<string> subjects, Actor? actor)
    {
        if (actor is not null)
            subjects.Add(ActorRef(actor));
    }

    protected static void Add(List<string> subjects, Item? item)
    {
        if (item is not null)
            subjects.Add(ItemRef(item));
    }

    protected static void Add(List<string> subjects, IEnumerable<Actor> actors)
    {
        foreach (Actor actor in actors)
            subjects.Add(ActorRef(actor));
    }

    /// <summary>An actor's instance reference in an event: <c>actor:&lt;id&gt;</c>.</summary>
    public static string ActorRef(Actor actor) => $"actor:{actor.Id}";

    /// <summary>An item's instance reference in an event: <c>item:&lt;id&gt;</c>.</summary>
    public static string ItemRef(Item item) => $"item:{item.Id}";
}
