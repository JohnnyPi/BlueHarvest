namespace BlueHarvest.Sim.Core;

/// <summary>Simulation time. One tick is 1/100 of a second of game time.</summary>
public static class Ticks
{
    public const int PerSecond = 100;

    public static long FromSeconds(int seconds) => (long)seconds * PerSecond;

    /// <summary>Formats ticks as game seconds, e.g. 2150 → "21.50s".</summary>
    public static string Format(long ticks) => $"{ticks / PerSecond}.{ticks % PerSecond:D2}s";
}
