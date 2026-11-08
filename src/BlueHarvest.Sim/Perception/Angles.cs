using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Perception;

/// <summary>Angle tests in integers, so results never depend on floating-point trig.</summary>
public static class Angles
{
    /// <summary>
    /// Whether the offset (dx, dy) lies within a cone around <paramref name="facing"/>,
    /// where <paramref name="cosPermille"/> is the cosine of the cone's half-angle ×1000.
    /// Offsets on the cone's edge count as inside. (0, 0) is always inside.
    /// </summary>
    public static bool WithinCone(int dx, int dy, Direction facing, int cosPermille)
    {
        if (dx == 0 && dy == 0)
            return true;
        if (cosPermille <= -1000)
            return true;

        var (fx, fy) = facing.Delta();
        long dot = (long)dx * fx + (long)dy * fy;
        long lengthsSquared = ((long)dx * dx + (long)dy * dy) * (fx * fx + fy * fy);
        long c = cosPermille;

        // Want dot / sqrt(lengthsSquared) >= c / 1000. Square both sides, minding signs.
        if (c >= 0)
            return dot >= 0 && dot * dot * 1_000_000 >= c * c * lengthsSquared;
        return dot >= 0 || dot * dot * 1_000_000 <= c * c * lengthsSquared;
    }
}
