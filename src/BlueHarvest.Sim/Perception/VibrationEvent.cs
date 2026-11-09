using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Perception;

/// <summary>
/// A shake of the ground: a footfall, something crashing or falling. Walls don't stop it; the ground
/// does: it loses 2 per tile, and its strength already includes how well the ground where it started
/// carries it (rock and decks carry, mud damps).
/// </summary>
/// <param name="ActorId">Who made it, if anyone.</param>
public sealed record VibrationEvent(long Tick, TilePos Position, int Strength, int? ActorId);
