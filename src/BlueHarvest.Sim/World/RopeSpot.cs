namespace BlueHarvest.Sim.World;

/// <summary>A rope left tied at <paramref name="Top"/> after someone rappelled down to <paramref name="Bottom"/>.</summary>
public readonly record struct RopeSpot(TilePos Top, TilePos Bottom);
