namespace BlueHarvest.Sim.Snapshots;

/// <summary>
/// A saved expedition that can't be put back here, and why: another format, generator version or pack set, another
/// island, or a file that isn't a whole snapshot. Nothing is regenerated or substituted in its place (contracts §8).
/// </summary>
public sealed class SnapshotRefusedException(string message) : Exception(message);
