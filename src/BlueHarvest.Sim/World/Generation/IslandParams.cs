namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Island generation settings. Defaults match the island lab (gen.js DEFAULTS); later stages add theirs.
/// Lengths are metres unless noted.
/// </summary>
public sealed record IslandParams
{
    public uint Seed { get; init; } = 7;
    /// <summary>Island size as a percentage of Oahu's frame (86 × 62 km).</summary>
    public double Scale { get; init; } = 75;
    /// <summary>Coarse grid cell size.</summary>
    public double Cell { get; init; } = 128;
    /// <summary>Height of the younger shield's crest.</summary>
    public double Peak { get; init; } = 1100;
    /// <summary>Height of the saddle plain between the shields.</summary>
    public double Saddle { get; init; } = 240;
    /// <summary>Coastline roughness.</summary>
    public double Rough { get; init; } = 0.6;
    /// <summary>Domain warp in km (at full scale).</summary>
    public double Warp { get; init; } = 5;
    /// <summary>Trade wind direction, degrees the wind comes from (60 = from the ENE).</summary>
    public double Wind { get; init; } = 60;
    /// <summary>Rain multiplier.</summary>
    public double Rain { get; init; } = 1;
    /// <summary>Erosion strength.</summary>
    public double Erosion { get; init; } = 1;
    public int Iterations { get; init; } = 8;
    /// <summary>A depression this deep (m) and this large (km²) stays a lake.</summary>
    public double LakeDepth { get; init; } = 40;
    public double LakeArea { get; init; } = 0.15;
    /// <summary>Mean discharge (m³/s) at which a stream counts as a river.</summary>
    public double RiverQ { get; init; } = 0.1;
    /// <summary>Strength of the pali (windward wall), 0–1.</summary>
    public double Pali { get; init; } = 0.85;
    /// <summary>Sea-cliff retreat in km (at full scale).</summary>
    public double Cliffs { get; init; } = 1.2;
}
