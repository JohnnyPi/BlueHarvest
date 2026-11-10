using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 2 of the world-generator port: the island shape. Each case builds a whole island and hashes
/// it in groups; the expected hashes come from the island lab's reference generator
/// (tools/island-lab/golden-shape.js). The first group that fails is where the port drifted —
/// later groups usually fail too, because everything downstream depends on it.
/// </summary>
public class IslandShapeTests
{
    public sealed record Expected(int Nx, int Ny, int Islets, int Lakes, int Rivers, int Falls,
        ulong Anchors, ulong Landmass, ulong Coast, ulong Erosion, ulong Tree, ulong LakeGroup, ulong RiverGroup, ulong FallGroup, ulong CoastClasses);

    private static readonly Dictionary<string, IslandParams> Cases = new()
    {
        ["Default"] = new IslandParams(),
        ["Seed12"] = new IslandParams { Seed = 12 },
        ["SmallNoCliffs"] = new IslandParams { Seed = 44, Scale = 50, Wind = 100, Cliffs = 0, Iterations = 5, Pali = 0.5 },
    };

    // node tools/island-lab/golden-shape.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(504, 363, 4, 1, 101, 74,
            0x447EB7BCC0266D08UL, 0x09B3AF551E8BEF44UL, 0xA6CD6CBB370FBC1FUL, 0x2E354CBFC927026CUL, 0x7ED0986FBD2D89E5UL, 0x5E547CA2F18E1273UL, 0x7D028D1ACBD2E03BUL, 0x5DFE5BD196C8C55EUL, 0xDDA8CBF165A8E182UL),
        ["Seed12"] = new(504, 363, 3, 1, 102, 84,
            0x1BBF708AC56AB717UL, 0x067C9F0F80193222UL, 0xB5CC90C9138E6042UL, 0xEDA93E795225F629UL, 0x2CF75CDA3ADE2E8EUL, 0xDDC6E94E573EA175UL, 0xC9E82F0B8AA2FDD7UL, 0x6EF9B19ECD75BB73UL, 0x05583919485D5F7BUL),
        ["SmallNoCliffs"] = new(336, 242, 3, 2, 44, 17,
            0x5850EE1E9F1A5CAEUL, 0x07068763515188B1UL, 0x25EAD217B29B0C1DUL, 0x99D090EF101A971AUL, 0xCDE3328EFC7F7B3AUL, 0xCA13BF47F879E296UL, 0xAA16AEC51396608BUL, 0xEA33F90D7C9ACF76UL, 0x71B7BA3E4ADABAC2UL),
    };

    public static IEnumerable<object[]> CaseNames => Cases.Keys.Select(k => new object[] { k });

    private static readonly Dictionary<string, IslandShape> Built = new();
    private static readonly object BuildLock = new();

    private static IslandShape Shape(string name)
    {
        lock (BuildLock)
        {
            if (!Built.TryGetValue(name, out IslandShape? shape))
            {
                shape = IslandShape.Generate(Cases[name]);
                Built[name] = shape;
            }
            return shape;
        }
    }

    private sealed class Hash
    {
        private readonly StateHasher _h = new();
        public ulong Value => _h.Value;
        public Hash D(double v) { _h.Add(BitConverter.DoubleToUInt64Bits(v)); return this; }
        public Hash I(long v) { _h.Add(v); return this; }
        public Hash U(uint v) { _h.Add((ulong)v); return this; }
        /// <summary>NaN bit patterns differ between runtimes, so NaN hashes as a marker.</summary>
        public Hash Nan(double v) => double.IsNaN(v) ? I(-7) : D(v);
        public Hash Ds(double[] a) { I(a.Length); foreach (double v in a) D(v); return this; }
        public Hash Ds(float[] a) { I(a.Length); foreach (float v in a) D(v); return this; }
        public Hash Is(byte[] a) { I(a.Length); foreach (byte v in a) I(v); return this; }
        public Hash Is(int[] a) { I(a.Length); foreach (int v in a) I(v); return this; }
        public Hash Is(List<int> a) { I(a.Count); foreach (int v in a) I(v); return this; }
    }

    private static ulong AnchorsHash(IslandAnchors a)
    {
        var h = new Hash().D(a.S).D(a.W).D(a.H).D(a.Along.X).D(a.Along.Y).D(a.Across.X).D(a.Across.Y);
        foreach (Shield s in new[] { a.OldShield, a.YoungShield })
        {
            h.Ds(s.Ridge.Xs).Ds(s.Ridge.Ys).D(s.Ridge.Total).D(s.Peak).D(s.WPos).D(s.WNeg)
                .D(s.PaliSign).D(s.PaliStrength).D(s.PaliAt).U(s.PaliNoise);
        }
        LineSegment g = a.Saddle.Segment;
        h.D(g.Ax).D(g.Ay).D(g.Dx).D(g.Dy).D(g.L2).D(g.Len).D(g.C0).D(a.Saddle.W).D(a.Saddle.Crest);
        Caldera c = a.Caldera;
        Vent v = a.Vent;
        h.D(c.X).D(c.Y).D(c.R).D(c.Depth).D(c.Rim).D(v.X).D(v.Y).D(v.H).D(v.R);
        h.I(a.Islets.Count);
        foreach (Islet i in a.Islets) h.D(i.X).D(i.Y).D(i.H).D(i.R);
        foreach (uint s in new[] { a.NWarpX, a.NWarpY, a.NRidge, a.NDetail, a.NCoast, a.NLava, a.NErod, a.NCliff, a.NBiome, a.NReef, a.NTile, a.NTile2, a.NCover })
            h.U(s);
        return h.Value;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Shape_HasExpectedSize(string name)
    {
        IslandShape w = Shape(name);
        Expected e = Golden[name];
        Assert.Equal((e.Nx, e.Ny), (w.Nx, w.Ny));
        Assert.Equal(e.Islets, w.Anchors.Islets.Count);
        Assert.Equal(e.Lakes, w.Lakes.Count);
        Assert.Equal(e.Rivers, w.Rivers.Count);
        Assert.Equal(e.Falls, w.Falls.Count);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Anchors_MatchReference(string name) =>
        Assert.Equal(Golden[name].Anchors, AnchorsHash(Shape(name).Anchors));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Landmass_MatchesReference(string name)
    {
        IslandShape w = Shape(name);
        ulong got = new Hash().Ds(w.OldWeight).Ds(w.Lava).Ds(w.Erodibility).Ds(w.PaliCut).Value;
        Assert.Equal(Golden[name].Landmass, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Coast_MatchesReference(string name)
    {
        IslandShape w = Shape(name);
        ulong got = new Hash().Ds(w.Height0).Is(w.Land).Is(w.Ocean).Ds(w.DistLand).Ds(w.DistSea).D(w.CutKm2).Value;
        Assert.Equal(Golden[name].Coast, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Erosion_MatchesReference(string name)
    {
        IslandShape w = Shape(name);
        ulong got = new Hash().Ds(w.Height).Ds(w.Filled).Ds(w.Rain).Value;
        Assert.Equal(Golden[name].Erosion, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void DrainageTree_MatchesReference(string name)
    {
        IslandShape w = Shape(name);
        DrainageTree t = w.Tree;
        var h = new Hash().Is(t.Receiver).Ds(t.ReceiverDistance);
        for (int i = 0; i < t.N; i++) if (t.Receiver[i] == DrainageTree.Outlet) h.I(t.Mouth[i]);
        h.I(t.StackCount);
        for (int i = 0; i < t.StackCount; i++) h.I(t.Stack[i]);
        Assert.Equal(Golden[name].Tree, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Lakes_MatchReference(string name)
    {
        IslandShape w = Shape(name);
        var h = new Hash().I(w.Lakes.Count);
        foreach (Lake l in w.Lakes) h.I(l.Id).D(l.AreaKm2).D(l.Depth).D(l.Level).D(l.Cx).D(l.Cy);
        h.I(w.N);
        foreach (float v in w.LakeLevel) h.Nan(v);
        Assert.Equal(Golden[name].LakeGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Rivers_MatchReference(string name)
    {
        IslandShape w = Shape(name);
        var h = new Hash().Ds(w.Flow).Ds(w.ToSea).Is(w.IsRiver).Is(w.Heads).I(w.Rivers.Count);
        foreach (River r in w.Rivers)
            h.I(r.End == RiverEnd.Sea ? 1 : 0).I(r.Head).Ds(r.Xs).Ds(r.Ys).Ds(r.Qs).Ds(r.Hs);
        h.D(w.StreamLength);
        Assert.Equal(Golden[name].RiverGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Falls_MatchReference(string name)
    {
        IslandShape w = Shape(name);
        var h = new Hash().I(w.Falls.Count);
        foreach (Waterfall f in w.Falls) h.D(f.X).D(f.Y).D(f.Drop).D(f.Q).I(f.IntoSea ? 1 : 0).I(f.OnRiver ? 1 : 0);
        Assert.Equal(Golden[name].FallGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CoastClasses_MatchReference(string name)
    {
        IslandShape w = Shape(name);
        ulong got = new Hash().Is(w.Coast).Is(w.Sea).Ds(w.CoastNx).Ds(w.CoastNy).Ds(w.CoastExposure).Ds(w.CliffMask)
            .D(w.SeaCliffTop).I(w.SeaCliffCells).I(w.PaliCells).D(w.PaliDrop).Value;
        Assert.Equal(Golden[name].CoastClasses, got);
    }

    [Fact]
    public void Shape_IsDeterministic()
    {
        var p = new IslandParams { Seed = 3, Scale = 40, Iterations = 2 };
        IslandShape a = IslandShape.Generate(p), b = IslandShape.Generate(p);
        Assert.Equal(new Hash().Ds(a.Height).Value, new Hash().Ds(b.Height).Value);
    }

    [Fact]
    public void Rivers_NeverRunUphill()
    {
        IslandShape w = Shape("Default");
        foreach (River r in w.Rivers)
            for (int k = 1; k < r.Hs.Length; k++)
                Assert.True(r.Hs[k] <= r.Hs[k - 1]);
    }
}
