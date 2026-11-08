using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Perception;

/// <summary>What a line between two points passed through.</summary>
/// <param name="Visibility">0-100: how much of the line got through cover, terrain and roofs.</param>
/// <param name="SolidTiles">Fully solid tiles (walls, closed doors, boulders) the line passed below the top of.</param>
/// <param name="TerrainBlocked">The ground rose above the line somewhere (a ridge or cliff).</param>
/// <param name="RoofBlocked">The line went through a roof: into or out of a building other than below the roof line.</param>
public readonly record struct LineTrace(int Visibility, int SolidTiles, bool TerrainBlocked, bool RoofBlocked)
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
    /// Roofs block a line that crosses into or out of a roofed tile above the roof line, so a building
    /// is only seen into through its doorways (and over its walls, never through its roof).
    /// With <paramref name="includeTargetCover"/>, cover on the target's own tile that stands taller than
    /// the target point also thins it, which is what makes hiding in jungle work.
    /// </summary>
    public static LineTrace Trace(WorldMap map, TilePos from, int fromZ, TilePos to, int toZ, bool includeTargetCover)
    {
        int visibility = 100;
        int solid = 0;
        bool terrain = false;
        bool roof = false;

        int x = from.X, y = from.Y;
        int dx = Math.Abs(to.X - from.X), dy = -Math.Abs(to.Y - from.Y);
        int sx = from.X < to.X ? 1 : -1, sy = from.Y < to.Y ? 1 : -1;
        int err = dx + dy;
        int steps = Math.Max(dx, -dy);

        var previous = from;
        int previousZ = fromZ;
        for (int k = 1; k < steps; k++)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }

            var tile = new TilePos(x, y);
            int lineZ = fromZ + (toZ - fromZ) * k / steps;
            if (CrossesRoof(map, previous, previousZ, tile, lineZ))
            {
                roof = true;
                visibility = 0;
            }
            previous = tile;
            previousZ = lineZ;

            int ground = map.Heights[tile];
            if (lineZ < ground)
            {
                terrain = true;
                visibility = 0;
                continue;
            }

            var cover = map.CoverAt(tile);
            if (cover.SightOpacityPercent > 0 && lineZ < ground + cover.SightHeightDm)
            {
                visibility = visibility * (100 - cover.SightOpacityPercent) / 100;
                if (cover.SightOpacityPercent >= 100)
                    solid++;
            }
        }

        if (steps > 0 && CrossesRoof(map, previous, previousZ, to, toZ))
        {
            roof = true;
            visibility = 0;
        }

        if (includeTargetCover && visibility > 0 && steps > 0)
        {
            var cover = map.CoverAt(to);
            if (cover.SightOpacityPercent > 0 && toZ < map.Heights[to] + cover.SightHeightDm)
                visibility = visibility * (100 - cover.SightOpacityPercent) / 100;
        }

        return new LineTrace(visibility, solid, terrain, roof);
    }

    /// <summary>
    /// Whether the line between two neighbouring samples passes through a roof. A point is inside when
    /// its tile is roofed and it's below the roof. Going from outside to inside (or back) is only
    /// possible below the roof line of the roofed side.
    /// </summary>
    public static bool CrossesRoof(WorldMap map, TilePos a, int za, TilePos b, int zb)
    {
        int roofA = map.Structures.RoofDm(a), roofB = map.Structures.RoofDm(b);
        int topA = map.Heights[a] + roofA, topB = map.Heights[b] + roofB;
        bool insideA = roofA > 0 && za < topA;
        bool insideB = roofB > 0 && zb < topB;
        if (insideA == insideB)
            return false;
        return insideB ? za >= topB : zb >= topA;
    }
}
