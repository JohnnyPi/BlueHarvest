using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Lighting;

/// <summary>
/// A light. Brightness falls off with the square of distance out to <see cref="RadiusTiles"/>.
/// With <see cref="Facing"/> set it's a cone (headlights, flashlights); otherwise it shines all round.
/// </summary>
/// <param name="HeightDm">Height above the ground it stands on.</param>
/// <param name="IntensityPercent">Light added at the source itself.</param>
/// <param name="ConeCosPermille">Cosine of the cone's half-angle ×1000; ignored without a facing.</param>
/// <param name="On">Off lights (a switched-off flashlight, a burnt-out flare) light nothing and aren't seen.</param>
public sealed record LightSource(
    TilePos Position,
    int HeightDm,
    int RadiusTiles,
    int IntensityPercent,
    Direction? Facing = null,
    int ConeCosPermille = -1000,
    bool On = true)
{
    /// <summary>Battery emergency light on a 3 m pole.</summary>
    public static LightSource Emergency(TilePos position) => new(position, HeightDm: 30, RadiusTiles: 6, IntensityPercent: 60);

    /// <summary>Vehicle headlight: low, long and narrow (about 37° either side).</summary>
    public static LightSource Headlight(TilePos position, Direction facing) =>
        new(position, HeightDm: 10, RadiusTiles: 16, IntensityPercent: 90, Facing: facing, ConeCosPermille: 800);

    /// <summary>A road flare on the ground: bright, red, all round, short reach.</summary>
    public static LightSource Flare(TilePos position) => new(position, HeightDm: 2, RadiusTiles: 7, IntensityPercent: 90);

    /// <summary>A hand-held flashlight: a narrow beam (about 30° either side) at chest height.</summary>
    public static LightSource Flashlight(TilePos position, Direction facing) =>
        new(position, HeightDm: 12, RadiusTiles: 10, IntensityPercent: 70, Facing: facing, ConeCosPermille: 866);
}
