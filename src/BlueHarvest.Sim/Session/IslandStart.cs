namespace BlueHarvest.Sim;

/// <summary>
/// How an island expedition starts: where the survivor lands (island metres), the run's seed (loot, power, weather and
/// the sim's own randomness), the hour, and how many 64 m chunks the streamed window is across.
/// </summary>
public sealed record IslandStart(double X, double Y, ulong RunSeed, int StartHour = 8, int ChunksAcross = 3);
