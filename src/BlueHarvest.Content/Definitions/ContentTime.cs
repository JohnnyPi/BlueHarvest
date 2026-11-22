#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// Durations in content are whole numbers with their unit in the field name (contracts §5), converted to sim
/// ticks once, at load. These match <c>Ticks.PerSecond</c> and <c>WorldClock.TicksPerGameMinute</c> in the sim;
/// the content library can't reference the sim, so a sim test checks they agree.
/// </summary>
public static class ContentTime
{
    /// <summary>A tick is 1/100 s of action time.</summary>
    public const long TicksPerActionSecond = 100;

    /// <summary>Six action seconds are one game minute.</summary>
    public const long TicksPerGameMinute = 600;
}
