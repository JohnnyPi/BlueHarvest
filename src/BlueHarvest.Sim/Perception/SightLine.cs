using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Perception;

/// <summary>What a line between two points passed through.</summary>
/// <param name="Visibility">0-100: how much of the line got through cover and terrain.</param>
/// <param name="SolidTiles">Fully solid tiles (walls, boulders) the line passed below the top of.</param>
/// <param name="TerrainBlocked">The ground rose above the line somewhere (a ridge or cliff).</param>
/// <param name="IndoorMismatch">One end is indoors and the other outdoors.</param>
public readonly record struct LineTrace(int Visibility, int SolidTiles, bool TerrainBlocked, bool IndoorMismatch)
{
    public static readonly LineTrace Clear = new(100, 0, false, false);
}

/// <summary>
/// Traces lines across the tile grid in 2.5D. Heights are absolute, in decimeters.
/// All integer math, so results are identical on every machine.
/// </summary>
public static class SightLine
{
    /// <summary>
    /// Traces from one point to another. Tiles between the ends are checked against the line's
    /// height there: ground above the line blocks it; cover taller than the line thins it by its opacity.
    /// With <paramref name="includeTargetCover"/>, cover on the target's own tile that stands taller than
    /// the target point also thins it, which is what makes hiding in jungle work.
    /// </summary>
    public static LineTrace Trace(WorldMap map, TilePos from, int fromZ, TilePos to, int toZ, bool includeTargetCover)
    {
        bool mismatch = Covers.Get(map.Cover[from]).Indoor != Covers.Get(map.Cover[to]).Indoor;
        int visibility = mismatch ? 0 : 100;
        int solid = 0;
        bool terrain = false;

        int x = from.X, y = from.Y;
        int dx = Math.Abs(to.X - from.X), dy = -Math.Abs(to.Y - from.Y);
        int sx = from.X < to.X ? 1 : -1, sy = from.Y < to.Y ? 1 : -1;
        int err = dx + dy;
        int steps = Math.Max(dx, -dy);

        for (int k = 1; k < steps; k++)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }

            var tile = new TilePos(x, y);
            int lineZ = fromZ + (toZ - fromZ) * k / steps;
            int ground = map.Heights[tile];
            if (lineZ < ground)
            {
                terrain = true;
                visibility = 0;
                continue;
            }

            var cover = Covers.Get(map.Cover[tile]);
            if (cover.SightOpacityPercent > 0 && lineZ < ground + cover.SightHeightDm)
            {
                visibility = visibility * (100 - cover.SightOpacityPercent) / 100;
                if (cover.SightOpacityPercent >= 100)
                    solid++;
            }
        }

        if (includeTargetCover && visibility > 0 && steps > 0)
        {
            var cover = Covers.Get(map.Cover[to]);
            if (cover.SightOpacityPercent > 0 && toZ < map.Heights[to] + cover.SightHeightDm)
                visibility = visibility * (100 - cover.SightOpacityPercent) / 100;
        }

        return new LineTrace(visibility, solid, terrain, mismatch);
    }
}
