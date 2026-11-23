using BlueHarvest.Sim.Actors;

namespace BlueHarvest.Sim.Events;

/// <summary>
/// <c>actor.acted</c>: an actor took its turn with this action (after a blackout has swapped it for a wait). The
/// events after it in the same request are what came of it. It has no C# event on <see cref="Simulation"/>.
/// </summary>
public sealed record ActorActedPayload(Actor Actor, ActorAction Action) : SimEventPayload
{
    public const string TypeName = "actor.acted";

    public override string Type => TypeName;

    public override Actor? Who => Actor;
}
