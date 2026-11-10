using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World.Generation;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// The islands the generator tests are built from, each built once per test run and shared between
/// test classes. Names and parameters match the CASES lists in tools/island-lab/golden-*.js.
/// </summary>
internal static class TestIslands
{
    public static readonly Dictionary<string, IslandParams> Cases = new()
    {
        ["Default"] = new IslandParams(),
        ["Seed12"] = new IslandParams { Seed = 12 },
        ["SmallNoCliffs"] = new IslandParams { Seed = 44, Scale = 50, Wind = 100, Cliffs = 0, Iterations = 5, Pali = 0.5 },
        ["CoolWet"] = new IslandParams { Seed = 9, Scale = 60, SeaTemp = 21, CloudBase = 500, Wetlands = 1.5, Rain = 1.3 },
    };

    private static readonly Dictionary<string, Lazy<IslandWorld>> Worlds =
        Cases.ToDictionary(kv => kv.Key, kv => new Lazy<IslandWorld>(() => IslandWorld.Generate(kv.Value)));

    private static readonly Dictionary<string, Lazy<IslandChunker>> Chunkers =
        Cases.ToDictionary(kv => kv.Key, kv => new Lazy<IslandChunker>(() => new IslandChunker(World(kv.Key))));

    public static IslandWorld World(string name) => Worlds[name].Value;

    public static IslandShape Shape(string name) => World(name).Shape;

    public static IslandClimate Climate(string name) => World(name).Climate;

    public static IslandWorks Works(string name) => World(name).Works;

    public static IslandChunker Chunker(string name) => Chunkers[name].Value;
}

/// <summary>StateHasher over generator output, matching the hasher in tools/island-lab/golden-*.js.</summary>
internal sealed class GenHasher
{
    private readonly StateHasher _h = new();
    public ulong Value => _h.Value;
    public GenHasher D(double v) { _h.Add(BitConverter.DoubleToUInt64Bits(v)); return this; }
    public GenHasher I(long v) { _h.Add(v); return this; }
    public GenHasher U(uint v) { _h.Add((ulong)v); return this; }
    /// <summary>A string as its length and UTF-16 code units.</summary>
    public GenHasher S(string s) { I(s.Length); foreach (char ch in s) I(ch); return this; }
    /// <summary>NaN bit patterns differ between runtimes, so NaN hashes as a marker.</summary>
    public GenHasher Nan(double v) => double.IsNaN(v) ? I(-7) : D(v);
    public GenHasher Ds(double[] a) { I(a.Length); foreach (double v in a) D(v); return this; }
    public GenHasher Ds(float[] a) { I(a.Length); foreach (float v in a) D(v); return this; }
    public GenHasher Is(byte[] a) { I(a.Length); foreach (byte v in a) I(v); return this; }
    public GenHasher Is(int[] a) { I(a.Length); foreach (int v in a) I(v); return this; }
    public GenHasher Is(List<int> a) { I(a.Count); foreach (int v in a) I(v); return this; }
}
