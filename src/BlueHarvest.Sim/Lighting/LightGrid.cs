using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Lighting;

/// <summary>
/// Light level per tile, 0-100. This is what creatures see by; the renderer's lights are only for looks.
/// </summary>
public sealed class LightGrid
{
    private readonly byte[] _levels;
    private readonly int _width;

    private LightGrid(byte[] levels, int width)
    {
        _levels = levels;
        _width = width;
    }

    public int LevelAt(TilePos p) => _levels[p.Y * _width + p.X];

    /// <summary>
    /// Starts every outdoor tile at the ambient level (indoor tiles at a third of it), then adds each
    /// light along clear lines from the light to the ground. Contributions add up, capped at 100.
    /// </summary>
    public static LightGrid Compute(WorldMap map, IReadOnlyList<LightSource> lights, int ambientPercent)
    {
        int width = map.Width, height = map.Height;
        var levels = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool indoor = Covers.Get(map.Cover[new TilePos(x, y)]).Indoor;
                levels[y * width + x] = indoor ? ambientPercent / 3 : ambientPercent;
            }
        }

        foreach (var light in lights)
        {
            if (!map.InBounds(light.Position))
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
                }
            }
        }

        var bytes = new byte[levels.Length];
        for (int i = 0; i < levels.Length; i++)
            bytes[i] = (byte)Math.Clamp(levels[i], 0, 100);
        return new LightGrid(bytes, width);
    }
}
