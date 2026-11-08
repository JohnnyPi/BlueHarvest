using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>Something an actor chooses to do on its turn.</summary>
public abstract record ActorAction;

/// <summary>Hold still for a number of ticks (at least 1).</summary>
public sealed record WaitAction(int Ticks) : ActorAction;

/// <summary>
/// Step one tile. A blocked move costs a short bump of time instead. With <paramref name="Shove"/>,
/// a large body's straight step that only a vehicle blocks pushes the vehicle along.
/// </summary>
public sealed record MoveAction(Direction Direction, bool Shove = false) : ActorAction;

/// <summary>
/// Bite whatever is in reach of the head (see Simulation.BiteZone).
/// <paramref name="Investigative"/> marks a test bite on something the biter hasn't identified.
/// </summary>
public sealed record BiteAction(bool Investigative = false) : ActorAction;

/// <summary>
/// Open or close the door on the neighbouring tile in <paramref name="Direction"/>. Walking into a
/// closed door opens it too; this is how doors get shut again.
/// </summary>
public sealed record UseDoorAction(Direction Direction) : ActorAction;

/// <summary>Climb the ladder on this tile: up to its deck from the ground, or down from the deck.</summary>
public sealed record ClimbAction : ActorAction;

/// <summary>
/// Give yourself first aid: uses a kit to turn the worst red wound (bleeding ones first) yellow and
/// stop its bleeding. Needs a kit and a working arm; may fail without enough skill.
/// </summary>
public sealed record TreatAction : ActorAction;
