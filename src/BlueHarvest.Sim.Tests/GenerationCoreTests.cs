using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 1 of the world-generator port: hashing, random streams, portable maths, noise, grid helpers
/// and the drainage tree. Every expected value was computed by the island lab's reference generator
/// (gen.js, via golden.js) and must match bit for bit: a failure here means the C# port has drifted
/// from the lab, and every island built on top would come out different.
/// </summary>
public class GenerationCoreTests
{
    private static ulong Bits(double v) => BitConverter.DoubleToUInt64Bits(v);

    private static StateHasher AddDouble(StateHasher h, double v) => h.Add(Bits(v));

    [Theory]
    [InlineData("", 2166136261u)]
    [InlineData("worldgen.anchors", 4014042363u)]
    [InlineData("chunk.detail", 3293575253u)]
    [InlineData("Ωψ", 2769970724u)]
    public void HashString_MatchesReference(string s, uint expected)
    {
        Assert.Equal(expected, GenHash.HashString(s));
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(1u, 1753845952u)]
    [InlineData(3735928559u, 3861431939u)]
    [InlineData(4294967295u, 1734902346u)]
    public void Mix_MatchesReference(uint input, uint expected)
    {
        Assert.Equal(expected, GenHash.Mix(input));
    }

    [Theory]
    [InlineData(0u, 0, 0, 0u)]
    [InlineData(7u, -3, 12, 4255965979u)]
    [InlineData(123456789u, 1000000, -2000000, 428407084u)]
    [InlineData(4294967295u, 65535, -1, 4039736281u)]
    public void Hash2_MatchesReference(uint s, int x, int y, uint expected)
    {
        Assert.Equal(expected, GenHash.Hash2(s, x, y));
    }

    [Theory]
    [InlineData(7u, "worldgen.anchors", 1170899987u)]
    [InlineData(4294967295u, "chunk.cover", 2840524829u)]
    public void Stream_MatchesReference(uint seed, string name, uint expected)
    {
        Assert.Equal(expected, GenHash.Stream(seed, name));
    }

    [Fact]
    public void Rng_MatchesReference()
    {
        var rng = new GenRng(7, "worldgen.anchors");
        var h = new StateHasher();
        var first = new List<ulong>();
        for (int i = 0; i < 1000; i++)
        {
            double v = rng.Next();
            if (i < 3) first.Add(Bits(v));
            AddDouble(h, v);
        }
        Assert.Equal(new[] { 0x3FDBCA4CCC000000UL, 0x3FE565E0B6800000UL, 0x3F9FE1252C000000UL }, first.ToArray());
        Assert.Equal(0xF379046CB3752431UL, h.Value);
    }

    [Fact]
    public void Rng_StaysInUnitInterval()
    {
        var rng = new GenRng(123, "test.range");
        for (int i = 0; i < 10_000; i++)
        {
            double v = rng.Next();
            Assert.True(v >= 0 && v < 1);
        }
    }

    private static ulong HashOver(double[] xs, Func<double, double> f)
    {
        var h = new StateHasher();
        foreach (double x in xs) AddDouble(h, f(x));
        return h.Value;
    }

    private static double[] MathInputs() => Enumerable.Range(0, 4001).Select(i => i * 0.0137 - 27.4).ToArray();

    private static double[] PositiveInputs() => MathInputs().Select(x => Math.Abs(x) * 3.1 + 0.001).ToArray();

    [Fact]
    public void Maths_MatchReference()
    {
        double[] xs = MathInputs(), pos = PositiveInputs();
        Assert.Equal(0xB4DC797E2D4CE885UL, HashOver(xs, GenMath.Round));
        Assert.Equal(0x3E59D7FE011560A8UL, HashOver(xs, x => GenMath.Hyp(x, x * 0.7 - 3)));
        Assert.Equal(0x77676BF3B230677FUL, HashOver(xs, GenMath.Exp));
        Assert.Equal(0x19F6DD99DB628EE0UL, HashOver(pos, GenMath.Log));
        Assert.Equal(0x35D99888BA048891UL, HashOver(pos, x => GenMath.Pow(x, 1.6)));
        Assert.Equal(0xEF90C8FCC316D568UL, HashOver(pos, x => GenMath.Pow(x, 1.3)));
        Assert.Equal(0x9CD81D398BE1EFA9UL, HashOver(xs, GenMath.Tanh));
        Assert.Equal(0xB281664D15E126B6UL, HashOver(xs, GenMath.Sin));
        Assert.Equal(0x02746C2EB3BA8EBCUL, HashOver(xs, GenMath.Cos));
        Assert.Equal(0xE295ADBA2FDCFEE6UL, HashOver(xs, GenMath.Atan));
        Assert.Equal(0x111F6413373943B0UL, HashOver(xs, x => GenMath.Atan2(x, 1.7 - x * 0.31)));
        Assert.Equal(0x949F5ECD3B5ED9C6UL, HashOver(xs, x => GenMath.SMax(x, -x * 0.5 + 3, 4)));
        Assert.Equal(0xFC20409111F6F97BUL, HashOver(xs, x => GenMath.SmoothStep(-5, 7, x)));
    }

    [Fact]
    public void Maths_SpotValuesMatchReference()
    {
        Assert.Equal(0x4005BF0A8B14576AUL, Bits(GenMath.Exp(1)));
        Assert.Equal(0x40026BB1BBB55515UL, Bits(GenMath.Log(10)));
        Assert.Equal(0x3FEAED548F090CEEUL, Bits(GenMath.Sin(1)));
        Assert.Equal(0x3FE921FB54442D19UL, Bits(GenMath.Atan(1)));
        Assert.Equal(0x3FDD9353D7568AF4UL, Bits(GenMath.Tanh(0.5)));
    }

    [Fact]
    public void Maths_StayCloseToSystemMath()
    {
        // Not bit-exact by design, but they must be the same functions.
        foreach (double x in MathInputs())
        {
            Assert.Equal(Math.Exp(x), GenMath.Exp(x), Math.Exp(x) * 1e-14);
            Assert.Equal(Math.Tanh(x), GenMath.Tanh(x), 1e-13);
            Assert.Equal(Math.Sin(x), GenMath.Sin(x), 1e-12);
            Assert.Equal(Math.Cos(x), GenMath.Cos(x), 1e-12);
            Assert.Equal(Math.Atan(x), GenMath.Atan(x), 1e-14);
            Assert.Equal(Math.Atan2(x, 1.7 - x * 0.31), GenMath.Atan2(x, 1.7 - x * 0.31), 1e-14);
        }
        foreach (double x in PositiveInputs())
        {
            Assert.Equal(Math.Log(x), GenMath.Log(x), 1e-13);
            Assert.Equal(Math.Pow(x, 1.6), GenMath.Pow(x, 1.6), Math.Pow(x, 1.6) * 1e-14);
        }
        Assert.Equal(-2.0, GenMath.Round(-2.5));
        Assert.Equal(3.0, GenMath.Round(2.5));
    }

    [Fact]
    public void Noise_MatchesReference()
    {
        uint s = GenHash.Stream(7, "test.noise");
        Assert.Equal(2483353791u, s);
        StateHasher hn = new(), hf = new(), hr = new();
        for (int j = 0; j < 48; j++)
        {
            for (int i = 0; i < 48; i++)
            {
                double x = i * 0.731 - 17.3, y = j * 0.613 + 4.1;
                AddDouble(hn, GenNoise.Noise(s, x, y));
                AddDouble(hf, GenNoise.Fbm(s, x, y, 4));
                AddDouble(hr, GenNoise.Ridged(s, x, y, 4));
            }
        }
        Assert.Equal(0xBFD92B479479A96FUL, Bits(GenNoise.Noise(s, 1.25, -3.5)));
        Assert.Equal(0x54526C37987C4225UL, hn.Value);
        Assert.Equal(0xCEB6EA6CC03E506EUL, hf.Value);
        Assert.Equal(0x8A64868D5AF2F980UL, hr.Value);
    }

    [Fact]
    public void Noise_IsContinuousAcrossCellEdges()
    {
        uint s = GenHash.Stream(1, "test.seam");
        for (int k = -5; k <= 5; k++)
        {
            double below = GenNoise.Noise(s, k - 1e-9, 0.37), above = GenNoise.Noise(s, k, 0.37);
            Assert.Equal(below, above, 1e-6);
        }
    }

    private static byte[] TestMask(int nx, int ny)
    {
        var mask = new byte[nx * ny];
        for (int i = 0; i < mask.Length; i++) mask[i] = GenHash.Hash2(123, i, 9) % 3 != 0 ? (byte)1 : (byte)0;
        return mask;
    }

    [Fact]
    public void Chamfer_MatchesReference()
    {
        float[] d = GenGrid.Chamfer(TestMask(40, 30), 40, 30, 128);
        var h = new StateHasher();
        foreach (float v in d) AddDouble(h, v);
        Assert.Equal(0x27FF80950D598CB9UL, h.Value);
    }

    [Fact]
    public void Components_MatchReference()
    {
        var label = new int[40 * 30];
        List<int> sizes = GenGrid.Components(TestMask(40, 30), 40, 30, label);
        var h = new StateHasher();
        foreach (int v in label) h.Add(v);
        Assert.Equal(0xBF7C366E2E0CDFA4UL, h.Value);
        Assert.Equal(2, sizes.Count);
        Assert.Equal(803, sizes.Max());
    }

    [Fact]
    public void BoxBlur_MatchesReference()
    {
        const int nx = 33, ny = 21;
        var f = new double[nx * ny];
        for (int i = 0; i < f.Length; i++) f[i] = GenHash.Hash2(5, i, 1) / 4294967296.0 * 100;
        GenGrid.BoxBlur(f, nx, ny, 3, 2);
        var h = new StateHasher();
        foreach (double v in f) AddDouble(h, v);
        Assert.Equal(0x8A339C2ACFB938B3UL, h.Value);
    }

    [Fact]
    public void Heap_PopOrderMatchesReference_IncludingTies()
    {
        var heap = new GenHeap(4000);
        var rng = new GenRng(11, "test.heap");
        for (int i = 0; i < 2000; i++) heap.Push(Math.Floor(rng.Next() * 50) / 5, i);
        var h = new StateHasher();
        while (heap.Count > 0) h.Add(heap.Pop());
        Assert.Equal(0x682F76AC34AFE185UL, h.Value);
    }

    [Fact]
    public void Heap_PopsInKeyOrder()
    {
        var heap = new GenHeap(100);
        var rng = new GenRng(5, "test.heap.order");
        var keys = new double[100];
        for (int i = 0; i < 100; i++) { keys[i] = rng.Next(); heap.Push(keys[i], i); }
        double last = double.NegativeInfinity;
        while (heap.Count > 0)
        {
            double k = keys[heap.Pop()];
            Assert.True(k >= last);
            last = k;
        }
    }

    [Fact]
    public void Chaikin_MatchesReference()
    {
        var rng = new GenRng(3, "test.chaikin");
        var pts = new List<PolyPoint>();
        for (int i = 0; i < 12; i++) pts.Add(new PolyPoint(rng.Next() * 1000, rng.Next() * 1000, rng.Next() * 5, rng.Next() * 300));
        List<PolyPoint> smooth = GenGrid.Chaikin(pts, 2);
        var h = new StateHasher();
        h.Add(smooth.Count);
        foreach (PolyPoint p in smooth)
        {
            AddDouble(h, p.X); AddDouble(h, p.Y); AddDouble(h, p.Q); AddDouble(h, p.H);
        }
        Assert.Equal(42, smooth.Count);
        Assert.Equal(pts[0], smooth[0]);
        Assert.Equal(pts[^1], smooth[^1]);
        Assert.Equal(0x8F6C696E40BA1C06UL, h.Value);
    }

    [Fact]
    public void Drainage_MatchesReference()
    {
        const int nx = 48, ny = 40, n = nx * ny;
        const double cell = 128;
        var height = new double[n];
        var land = new byte[n];
        for (int y = 0; y < ny; y++)
        {
            for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x;
                double dx = (x - 23.5) / 20, dy = (y - 19.5) / 16, r2 = dx * dx + dy * dy;
                land[i] = r2 < 1 ? (byte)1 : (byte)0;
                height[i] = (1 - r2) * 400 + GenHash.Hash2(77, x, y) / 4294967296.0 * 60;
            }
        }
        var tree = new DrainageTree(nx, ny, cell);
        var filled = new double[n];
        Flood.Fill(height, land, nx, ny, filled, new byte[n], new GenHeap(n), new int[n]);
        tree.Build(filled, land);

        var hf = new StateHasher();
        foreach (double v in filled) AddDouble(hf, v);
        var ht = new StateHasher();
        for (int i = 0; i < n; i++)
        {
            ht.Add(tree.Receiver[i]);
            AddDouble(ht, tree.ReceiverDistance[i]);
            if (tree.Receiver[i] == DrainageTree.Outlet) ht.Add(tree.Mouth[i]);
        }
        var hs = new StateHasher();
        hs.Add(tree.StackCount);
        for (int i = 0; i < tree.StackCount; i++) hs.Add(tree.Stack[i]);

        Assert.Equal(1008, land.Count(v => v != 0));
        Assert.Equal(1008, tree.StackCount);
        Assert.Equal(0x13BE2B7CB3DADCBDUL, hf.Value);
        Assert.Equal(0x1A6301B51EF74B75UL, ht.Value);
        Assert.Equal(0xEA14269F9050B9E0UL, hs.Value);
    }

    [Fact]
    public void Drainage_EveryLandCellReachesTheSea()
    {
        const int nx = 48, ny = 40, n = nx * ny;
        var height = new double[n];
        var land = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int x = i % nx, y = i / nx;
            land[i] = x > 2 && y > 2 && x < nx - 3 && y < ny - 3 ? (byte)1 : (byte)0;
            height[i] = GenHash.Hash2(31, x, y) / 4294967296.0 * 100; // all pits
        }
        var tree = new DrainageTree(nx, ny, 100);
        var filled = new double[n];
        Flood.Fill(height, land, nx, ny, filled, new byte[n], new GenHeap(n), new int[n]);
        tree.Build(filled, land);
        for (int i = 0; i < n; i++)
        {
            if (land[i] == 0) { Assert.Equal(DrainageTree.NotLand, tree.Receiver[i]); continue; }
            int c = i, steps = 0;
            while (tree.Receiver[c] >= 0) { c = tree.Receiver[c]; Assert.True(++steps < n); }
            Assert.Equal(DrainageTree.Outlet, tree.Receiver[c]);
            Assert.True(tree.Mouth[c] >= 0);
        }
    }
}
