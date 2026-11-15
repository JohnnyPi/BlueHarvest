using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Lighting;

/// <summary>
/// Light level per tile, 0-100. This is what creatures see by; the renderer's lights are only for looks.
/// </summary>
public sealed class LightGrid
{
    private readonly byte[] _levels;
    private readonly byte[] _red;
    private readonly int _width;

    private LightGrid(byte[] levels, byte[] red, int width)
    {
        _levels = levels;
        _red = red;
        _width = width;
    }

    public int LevelAt(TilePos p) => _levels[p.Y * _width + p.X];

    /// <summary>
    /// How much of a tile's light is red (emergency lights, flares), 0–100. For the screen only:
    /// creatures see by <see cref="LevelAt"/>, whatever its colour.
    /// </summary>
    public int RedPercentAt(TilePos p) => _red[p.Y * _width + p.X];

    /// <summary>
    /// Starts every outdoor tile at the ambient level (indoor tiles at a third of it), then adds each
    /// light along clear lines from the light to the ground. Contributions add up, capped at 100.
    /// </summary>
    public static LightGrid Compute(WorldMap map, IReadOnlyList<LightSource> lights, int ambientPercent)
    {
        int width = map.Width, height = map.Height;
        var levels = new int[width * height];
        var red = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool indoor = map.IsIndoor(new TilePos(x, y));
                levels[y * width + x] = indoor ? ambientPercent / 3 : ambientPercent;
            }
        }

        foreach (var light in lights)
        {
            if (!light.On || !map.InBounds(light.Position))
                continue;

            int r = light.RadiusTiles;
            long r2 = (long)r * r;
            int sourceZ = map.Heights[light.Position] + light.HeightDm;

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    var tile = new TilePos(light.Position.X + dx, light.Position.Y + dy);
                    long d2 = (long)dx * dx + (long)dy * dy;
                    if (!map.InBounds(tile) || d2 > r2)
                        continue;
                    if (light.Facing is { } facing
                        && (d2 == 0 || !Angles.WithinCone(dx, dy, facing, light.ConeCosPermille)))
                        continue; // cone lights shine ahead, not onto their own tile

                    int visibility = d2 == 0
                        ? 100
                        : SightLine.Trace(map, light.Position, sourceZ, tile, map.Heights[tile] + 1, includeTargetCover: false).Visibility;
                    if (visibility == 0)
                        continue;

                    long added = light.IntensityPercent * (r2 - d2) / r2 * visibility / 100;
                    int index = tile.Y * width + tile.X;
                    levels[index] = (int)Math.Min(100, levels[index] + added);
                    if (light.Red)
                        red[index] = (int)Math.Min(1000, red[index] + added);
                }
            }
        }

        var bytes = new byte[levels.Length];
        var redShare = new byte[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            bytes[i] = (byte)Math.Clamp(levels[i], 0, 100);
            if (red[i] > 0)
                redShare[i] = (byte)Math.Clamp(red[i] * 100 / Math.Max(1, levels[i]), 0, 100);
        }
        return new LightGrid(bytes, redShare, width);
    }
}
