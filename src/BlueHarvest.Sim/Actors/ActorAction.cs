using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>Something an actor chooses to do on its turn.</summary>
public abstract record ActorAction;

/// <summary>Hold still for a number of ticks (at least 1).</summary>
public sealed record WaitAction(int Ticks) : ActorAction;

/// <summary>Step one tile. A blocked move costs a short bump of time instead.</summary>
public sealed record MoveAction(Direction Direction) : ActorAction;
