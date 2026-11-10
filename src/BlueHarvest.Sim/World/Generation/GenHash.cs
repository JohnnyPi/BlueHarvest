namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Addressed randomness for world generation: every value is a hash of (seed, stream, coordinates),
/// so any chunk can be generated on its own and agrees with its neighbours at the seams.
/// 32-bit arithmetic, bit-identical to the island lab's reference generator (gen.js).
/// </summary>
public static class GenHash
{
    /// <summary>FNV-1a 32-bit over the string's UTF-16 code units.</summary>
    public static uint HashString(string s)
    {
        uint h = 0x811c9dc5;
        foreach (char c in s)
        {
            h ^= (uint)c;
            h = unchecked(h * 0x01000193u);
        }
        return h;
    }

    /// <summary>Integer finaliser (lowbias32): spreads every input bit across the output.</summary>
    public static uint Mix(uint h)
    {
        unchecked
        {
            h ^= h >> 16; h *= 0x7feb352du;
            h ^= h >> 15; h *= 0x846ca68bu;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>Hash of a grid coordinate under seed s.</summary>
    public static uint Hash2(uint s, int x, int y)
    {
        unchecked
        {
            return Mix(((uint)x * 0x27d4eb2du) ^ Mix(((uint)y * 0x165667b1u) ^ s));
        }
    }

    /// <summary>The seed of a named stream ("chunk.cover", "worldgen.anchors").</summary>
    public static uint Stream(uint seed, string name) => Mix(seed ^ HashString(name));

    /// <summary>A hash as a double in [0, 1).</summary>
    public static double Unit(uint h) => h / 4294967296.0;
}

/// <summary>
/// Sequential random numbers for one generation stream (a mulberry32 variant).
/// Used where values are drawn once, in order: global anchors, placement candidates.
/// For per-point values use <see cref="GenHash.Hash2"/> instead, so chunks stay independent.
/// </summary>
public sealed class GenRng
{
    private uint _a;

    public GenRng(uint seed, string name)
    {
        _a = GenHash.Stream(seed, name);
    }

    /// <summary>A double in [0, 1).</summary>
    public double Next()
    {
        unchecked
        {
            _a += 0x6D2B79F5u;
            uint t = (_a ^ (_a >> 15)) * (1u | _a);
            t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}
