namespace BlueHarvest.Sim.World;

/// <summary>Blood left on a tile by someone bleeding, and when. Predators will smell it (build step 10).</summary>
public readonly record struct BloodSpot(TilePos Position, long Tick);
