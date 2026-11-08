using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Actors;

/// <summary>Something an actor chooses to do on its turn.</summary>
public abstract record ActorAction;

/// <summary>Hold still for a number of ticks (at least 1).</summary>
public sealed record WaitAction(int Ticks) : ActorAction;

/// <summary>
/// Step one tile. A blocked move costs a short bump of time instead. With <paramref name="Shove"/>,
/// a large body's straight step that only a vehicle blocks pushes the vehicle along. With
/// <paramref name="Sprint"/>, a single-tile actor runs: faster and louder, if its wounds allow.
/// A rise between the walk limit and the climb limit (people: 0.5-2 m) is climbed: slow and noisy.
/// </summary>
public sealed record MoveAction(Direction Direction, bool Shove = false, bool Sprint = false) : ActorAction;

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
/// First aid: uses a kit to turn the worst red wound (bleeding ones first) yellow and stop its
/// bleeding, on yourself or on <paramref name="PatientId"/> next to you. Needs a kit and a working
/// arm; may fail without enough skill.
/// </summary>
public sealed record TreatAction(int? PatientId = null) : ActorAction;

/// <summary>Cry out: a scream anyone nearby hears, predators included. Panicking people do it.</summary>
public sealed record ScreamAction : ActorAction;

/// <summary>Tell another person (by actor id, within earshot) what to do. Talking is a little noise of its own.</summary>
public sealed record OrderAction(int TargetId, NpcOrder Order) : ActorAction;

/// <summary>Throw an item (a flare) toward a tile. It falls short past the thrower's range and may scatter.</summary>
public sealed record ThrowAction(ItemKind Item, TilePos Target) : ActorAction;

/// <summary>Switch the carried flashlight on or off.</summary>
public sealed record ToggleLightAction : ActorAction;

/// <summary>Rope down to the next tile in a direction: for drops too far to jump. Uses up the rope.</summary>
public sealed record RappelAction(Direction Direction) : ActorAction;
