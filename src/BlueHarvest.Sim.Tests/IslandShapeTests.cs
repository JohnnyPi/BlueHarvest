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

    // node tools/island-lab/golden-shape.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(672, 484, 4, 1, 150, 122,
            0x4CDDFB1DFA542324UL, 0x9612B97791695DFCUL, 0xB83E778AC964E189UL, 0xC993819DF8BE3746UL, 0x316AABE6C39551FAUL, 0x691BC3FE370DC8C7UL, 0x77C85D11309A33ECUL, 0x3B4838822A0C8A0BUL, 0x6132DE83B9B67B3FUL),
        ["Seed12"] = new(504, 363, 3, 1, 102, 84,
            0x1BBF708AC56AB717UL, 0x067C9F0F80193222UL, 0x5A529D01472A7507UL, 0x6CE52FD23F94311CUL, 0x2CF75CDA3ADE2E8EUL, 0xDDC6E94E573EA175UL, 0xC9E82F0B8AA2FDD7UL, 0x6EF9B19ECD75BB73UL, 0x7B4E17206A26521BUL),
        ["SmallNoCliffs"] = new(336, 242, 3, 2, 44, 17,
            0x5850EE1E9F1A5CAEUL, 0x07068763515188B1UL, 0xF19010ABA5D1A8ADUL, 0xB37ABE53B481957AUL, 0xCDE3328EFC7F7B3AUL, 0xCA13BF47F879E296UL, 0xAA16AEC51396608BUL, 0xEA33F90D7C9ACF76UL, 0x3578F9D809DE9B2BUL),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    private static IslandShape Shape(string name) => TestIslands.Shape(name);

    private static ulong AnchorsHash(IslandAnchors a)
    {
        var h = new GenHasher().D(a.S).D(a.W).D(a.H).D(a.Along.X).D(a.Along.Y).D(a.Across.X).D(a.Across.Y);
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
        ulong got = new GenHasher().Ds(w.OldWeight).Ds(w.Lava).Ds(w.Erodibility).Ds(w.PaliCut).Value;
        Assert.Equal(Golden[name].Landmass, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Coast_MatchesReference(string name)
    {
        IslandShape w = Shape(name);
        ulong got = new GenHasher().Ds(w.Height0).Is(w.Land).Is(w.Ocean).Ds(w.DistLand).Ds(w.DistSea).D(w.CutKm2).Value;
        Assert.Equal(Golden[name].Coast, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Erosion_MatchesReference(string name)
    {
        IslandShape w = Shape(name);
        ulong got = new GenHasher().Ds(w.Height).Ds(w.Filled).Ds(w.Rain).Value;
        Assert.Equal(Golden[name].Erosion, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void DrainageTree_MatchesReference(string name)
    {
        IslandShape w = Shape(name);
        DrainageTree t = w.Tree;
        var h = new GenHasher().Is(t.Receiver).Ds(t.ReceiverDistance);
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
        var h = new GenHasher().I(w.Lakes.Count);
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
        var h = new GenHasher().Ds(w.Flow).Ds(w.ToSea).Is(w.IsRiver).Is(w.Heads).I(w.Rivers.Count);
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
        var h = new GenHasher().I(w.Falls.Count);
        foreach (Waterfall f in w.Falls) h.D(f.X).D(f.Y).D(f.Drop).D(f.Q).I(f.IntoSea ? 1 : 0).I(f.OnRiver ? 1 : 0);
        Assert.Equal(Golden[name].FallGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void CoastClasses_MatchReference(string name)
    {
        IslandShape w = Shape(name);
        ulong got = new GenHasher().Is(w.Coast).Is(w.Sea).Ds(w.CoastNx).Ds(w.CoastNy).Ds(w.CoastExposure).Ds(w.CliffMask)
            .D(w.SeaCliffTop).I(w.SeaCliffCells).I(w.PaliCells).D(w.PaliDrop).Value;
        Assert.Equal(Golden[name].CoastClasses, got);
    }

    [Fact]
    public void Shape_IsDeterministic()
    {
        var p = new IslandParams { Seed = 3, Scale = 40, Iterations = 2 };
        IslandShape a = IslandShape.Generate(p), b = IslandShape.Generate(p);
        Assert.Equal(new GenHasher().Ds(a.Height).Value, new GenHasher().Ds(b.Height).Value);
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
