using BlueHarvest.Sim.Power;
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

/// <summary>
/// Work a piece of power equipment (by device id in Simulation.Power) from inside or next to it:
/// throw a switch, start a generator, open a valve, repair a line. <paramref name="Arg"/> picks the
/// breaker for line operations. What can be done is listed by PowerGrid.Choices.
/// </summary>
public sealed record OperateAction(int DeviceId, PowerOp Op, int Arg = -1) : ActorAction;

/// <summary>Holds a lit flare to the tile that way (or the actor's own, with no direction) to set it alight. Uses up a flare.</summary>
public sealed record IgniteAction(Direction? Direction = null, ItemKind Source = ItemKind.Flare) : ActorAction;

/// <summary>
/// Picks up an item within reach (your tile or the next, at your level): lying on the ground, in an
/// open container, or on a body. It goes where the inventory has room (see Inventory.PlaceFor).
/// </summary>
public sealed record PickUpAction(int ItemId) : ActorAction;

/// <summary>Puts down a carried item (from a hand, a pocket or a bag) on your own tile.</summary>
public sealed record DropAction(int ItemId) : ActorAction;

/// <summary>Puts a carried item into a container: one you carry, or an open one within reach.</summary>
public sealed record PutAction(int ItemId, int ContainerId) : ActorAction;

/// <summary>Opens a container with a door or lid within reach (a locker, a crate, a fridge), or shuts it if it's open.</summary>
public sealed record OpenAction(int ItemId) : ActorAction;

/// <summary>Puts two carried things together: loads rounds, fits batteries or a magazine, or makes something (see Recipes).</summary>
public sealed record CombineAction(int ItemId, int WithId) : ActorAction;

/// <summary>Takes a carried item out of what it's in (a battery from a flashlight, a can from your pack) into your hands or pockets.</summary>
public sealed record TakeOutAction(int ItemId) : ActorAction;

/// <summary>Empties what something is loaded with: rounds out of a magazine or a gun, a magazine out of a pistol, batteries out of a light.</summary>
public sealed record UnloadAction(int ItemId) : ActorAction;

/// <summary>Uses a carried item on its own: eats it, drinks from it, puts it on a wound, reads it (see Recipes.UseOf).</summary>
public sealed record UseItemAction(int ItemId) : ActorAction;

/// <summary>Forces a locked container within reach with a crowbar: slow and loud.</summary>
public sealed record PryAction(int ItemId) : ActorAction;

/// <summary>Puts out fire in a direction with a carried extinguisher: the tile ahead, the two beside it, and the one beyond.</summary>
public sealed record ExtinguishAction(Direction Direction) : ActorAction;

/// <summary>Pours fuel from a carried jerry can: on your own tile, or a trail of up to four tiles in a direction (a fuse).</summary>
public sealed record PourFuelAction(Direction? Direction = null) : ActorAction;
