using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Lighting;

/// <summary>
/// A light. Brightness falls off with the square of distance out to <see cref="RadiusTiles"/>.
/// With <see cref="Facing"/> set it's a cone (headlights, flashlights); otherwise it shines all round.
/// </summary>
/// <param name="HeightDm">Height above the ground it stands on.</param>
/// <param name="IntensityPercent">Light added at the source itself.</param>
/// <param name="ConeCosPermille">Cosine of the cone's half-angle ×1000; ignored without a facing.</param>
public sealed record LightSource(
    TilePos Position,
    int HeightDm,
    int RadiusTiles,
    int IntensityPercent,
    Direction? Facing = null,
    int ConeCosPermille = -1000)
{
    /// <summary>Battery emergency light on a 3 m pole.</summary>
    public static LightSource Emergency(TilePos position) => new(position, HeightDm: 30, RadiusTiles: 6, IntensityPercent: 60);

    /// <summary>Vehicle headlight: low, long and narrow (about 37° either side).</summary>
    public static LightSource Headlight(TilePos position, Direction facing) =>
        new(position, HeightDm: 10, RadiusTiles: 16, IntensityPercent: 90, Facing: facing, ConeCosPermille: 800);
}
