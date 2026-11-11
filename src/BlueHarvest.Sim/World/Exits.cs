namespace BlueHarvest.Sim.World;

/// <summary>
/// An escape hatch: a person who steps onto it (on the ground, not a deck) leaves the map and the
/// run counts as an escape. Placed where the Rex can't follow: deep jungle, a building's floor hatch,
/// the culvert at the foot of the retaining wall.
/// </summary>
public sealed record ExitPoint(TilePos Position, string Name);

/// <summary>
/// A shorted section of fence that crackles and flickers while it stands: a light and a noise with
/// nobody behind them, so a creature investigates and test-bites it, and a bite snaps it. Goes dead
/// when its tile is no longer a sagging fence.
/// </summary>
public sealed class SparkingFence
{
    /// <summary>A crackle this often while it's live.</summary>
    public const int CrackleTicks = 150;

    /// <summary>How loud a crackle is at the source: smashing-loud, so it carries through a storm.</summary>
    public const int CrackleLoudness = 40;

    internal SparkingFence(TilePos position, int lightIndex)
    {
        Position = position;
        LightIndex = lightIndex;
    }

    public TilePos Position { get; internal set; }

    /// <summary>Its flickering light in Simulation.Lights (turned off, never removed, when it dies).</summary>
    public int LightIndex { get; }

    public bool Live { get; internal set; } = true;

    /// <summary>When it went dead (the fence broke), or null while it's live.</summary>
    public long? DiedTick { get; internal set; }
}
