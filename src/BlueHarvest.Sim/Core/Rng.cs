using System.Numerics;

namespace BlueHarvest.Sim.Core;

/// <summary>
/// Deterministic random number generator (xoshiro256**), seeded through SplitMix64.
/// Never use System.Random in the simulation: its sequence is not guaranteed across .NET versions.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        Seed = seed;
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    /// <summary>The seed this generator started from.</summary>
    public ulong Seed { get; }

    /// <summary>Where the stream has got to: its four words (a snapshot saves them, and puts them back).</summary>
    internal (ulong S0, ulong S1, ulong S2, ulong S3) State
    {
        get => (_s0, _s1, _s2, _s3);
        set
        {
            if (value is (0, 0, 0, 0))
                throw new ArgumentException("A xoshiro state can't be all zero.", nameof(value));
            (_s0, _s1, _s2, _s3) = value;
        }
    }

    /// <summary>
    /// Creates an independent stream for one system ("ai:rex", "weather").
    /// Derived from the seed and the name only, so forking never disturbs this
    /// generator, and adding a new system never changes existing streams.
    /// </summary>
    public Rng Fork(string streamName)
    {
        ulong x = Seed ^ StateHasher.HashString(streamName);
        return new Rng(SplitMix64(ref x));
    }

    public ulong NextULong()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform integer in [0, maxExclusive), without modulo bias.</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be positive.");

        ulong range = (ulong)maxExclusive;
        ulong high = Math.BigMul(NextULong(), range, out ulong low);
        if (low < range)
        {
            ulong threshold = (0UL - range) % range;
            while (low < threshold)
                high = Math.BigMul(NextULong(), range, out low);
        }
        return (int)high;
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be greater than minInclusive.");
        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>True with probability numerator / denominator.</summary>
    public bool Chance(int numerator, int denominator) => NextInt(denominator) < numerator;

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
