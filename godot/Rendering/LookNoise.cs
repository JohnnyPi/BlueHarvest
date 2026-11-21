#nullable enable
using System;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// Seeded randomness for looks only: a hash and smooth value noise over island metres. The guide's
/// determinism contract: a choice depends on the island's seed, the tile's island position, the
/// profile and a salt, never on where the streamed map lies, and it never touches the simulation's
/// random streams. Floats are fine here; nothing in the simulation reads these values.
/// </summary>
public static class LookNoise
{
    /// <summary>A well-mixed 32-bit hash of (seed, x, y, salt).</summary>
    public static uint Hash(int seed, int x, int y, int salt)
    {
        uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(salt * 1274126177) + (uint)seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h = (h ^ (h >> 16)) * 2246822519u;
        return h ^ (h >> 15);
    }

    /// <summary>0 to 1 (exclusive), from the hash.</summary>
    public static float Unit(int seed, int x, int y, int salt) => (Hash(seed, x, y, salt) >> 8) / 16777216f;

    /// <summary>Whether a per-mille chance comes up for this tile.</summary>
    public static bool Chance(int seed, int x, int y, int salt, int perMille) =>
        perMille > 0 && Hash(seed, x, y, salt) % 1000u < (uint)perMille;

    /// <summary>
    /// Smooth noise, 0 to 1, with features about <paramref name="scaleM"/> metres across: a lattice of
    /// hashed values blended with smoothstep, plus a finer octave for ragged edges.
    /// </summary>
    public static float Field(int seed, int x, int y, float scaleM, int salt)
    {
        float coarse = Value(seed, x + 0.5, y + 0.5, scaleM, salt);
        float fine = Value(seed, x + 0.5, y + 0.5, scaleM / 2.7f, salt + 101);
        return coarse * 0.72f + fine * 0.28f;
    }

    private static float Value(int seed, double x, double y, float scale, int salt)
    {
        double u = x / scale, v = y / scale;
        int ix = (int)Math.Floor(u), iy = (int)Math.Floor(v);
        float fx = Smooth((float)(u - ix)), fy = Smooth((float)(v - iy));
        float a = Unit(seed, ix, iy, salt), b = Unit(seed, ix + 1, iy, salt);
        float c = Unit(seed, ix, iy + 1, salt), d = Unit(seed, ix + 1, iy + 1, salt);
        float top = a + (b - a) * fx, bottom = c + (d - c) * fx;
        return top + (bottom - top) * fy;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
