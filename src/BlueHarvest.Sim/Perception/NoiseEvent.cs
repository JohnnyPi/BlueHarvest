using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Perception;

/// <summary>A sound made somewhere. Loudness drops by 1 per tile of distance, more through walls and ridges.</summary>
/// <param name="ActorId">Who made it, if anyone.</param>
/// <param name="Kind">Short description, e.g. "footsteps".</param>
public sealed record NoiseEvent(long Tick, TilePos Position, int Loudness, string Kind, int? ActorId);
