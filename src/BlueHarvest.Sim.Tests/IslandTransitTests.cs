using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 4c of the world-generator port: walking times and transit (skyway, summit lift, tram, monorail
/// with its elevators and the access key), checked against the island lab (tools/island-lab/golden-transit.js).
/// If IslandRoadsTests fail too, fix those first.
/// </summary>
public class IslandTransitTests
{
    public sealed record Expected(int Lifts, int TramStops, int Stations, int Labels, int Prims,
        ulong WalkGroup, ulong LiftGroup, ulong TramGroup, ulong MonorailGroup, ulong WalkTransitGroup, ulong AllPrimsGroup);

    // node tools/island-lab/golden-transit.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(1, 5, 4, 323, 863,
            0x4631DD3A335FF450UL, 0x6DE6CAB94222A487UL, 0xE88681A3D9B3BF99UL, 0xEE105248EB246947UL, 0xCC719AE45B823DCAUL, 0x869F8F69286A0604UL),
        ["Seed12"] = new(1, 5, 4, 323, 858,
            0x9CC902BC9204FB0CUL, 0xE433868004CA4768UL, 0xC5DF7F3D2A96BBDEUL, 0x056B75C26DFCACBFUL, 0xD5AC1204B8BA5652UL, 0xFF4E18490A2216CFUL),
        ["SmallNoCliffs"] = new(2, 5, 4, 330, 865,
            0x92B319A27741ABF5UL, 0xE903AC210E6E8208UL, 0xCE8D8A063A89B00AUL, 0x63E0117754FDF23AUL, 0x3355E2C8AAC931D1UL, 0xB8B7D55FA476265EUL),
        ["CoolWet"] = new(1, 5, 4, 323, 837,
            0xE8C95C6E00A2470AUL, 0x01D04671DD1A1C89UL, 0x844A7BDC674C3828UL, 0xDF6F5966668B7B75UL, 0xB5967A5DD6E53897UL, 0xCD1605C7D53076E8UL),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    private static GenHasher Matrix(GenHasher h, double[][] m)
    {
        h.I(m.Length);
        foreach (double[] row in m) h.Ds(row);
        return h;
    }

    private static GenHasher Nearest(GenHasher h, IEnumerable<NearestSite?> list)
    {
        NearestSite?[] a = list.ToArray();
        h.I(a.Length);
        foreach (NearestSite? q in a)
        {
            if (q != null) h.I(q.Id).D(q.Minutes);
            else h.I(-1);
        }
        return h;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Counts_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        Expected e = Golden[name];
        Assert.Equal(e.Lifts, w.Lifts.Count);
        Assert.Equal(e.TramStops, w.Tram?.Stops.Count ?? 0);
        Assert.Equal(e.Stations, w.Monorail?.Stations.Count ?? 0);
        Assert.Equal(e.Labels, w.Labels.Count);
        Assert.Equal(e.Prims, w.Prims.Count);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void WalkingTimes_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        ulong got = Nearest(Matrix(new GenHasher(), w.Walk), w.Sites.Select(t => t.Nearest)).Value;
        Assert.Equal(Golden[name].WalkGroup, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Lifts_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Lifts.Count);
        foreach (Lift l in w.Lifts)
        {
            h.S(l.Name).S(l.From).S(l.To).D(l.X1).D(l.Y1).D(l.X2).D(l.Y2).D(l.Km).D(l.Crest).I(l.Towers.Count);
            foreach ((double x, double y) in l.Towers) h.D(x).D(y);
        }
        Assert.Equal(Golden[name].LiftGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Tram_MatchesReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher();
        if (w.Tram is { } t)
        {
            h.I(1).Ds(t.Xs).Ds(t.Ys).D(t.Km).I(t.Stops.Count);
            foreach (TramStop q in t.Stops) h.S(q.Name).D(q.X).D(q.Y);
        }
        else h.I(0);
        Assert.Equal(Golden[name].TramGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void MonorailAndObjectives_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher();
        if (w.Monorail is { } m)
        {
            h.I(1).D(m.Km).D(m.MinCover).D(m.Deepest).I(m.Stations.Count);
            foreach (MonorailStation q in m.Stations) h.S(q.Name).D(q.X).D(q.Y).D(q.Z).D(q.Shaft).S(q.Power);
            h.I(m.Vents.Count);
            foreach ((int x, int y) in m.Vents) h.D(x).D(y);
        }
        else h.I(0);
        h.I(w.Objectives.Count);
        foreach (Objective o in w.Objectives) h.S(o.Key).S(o.Site).S(o.Room).D(o.X).D(o.Y);
        Assert.Equal(Golden[name].MonorailGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void TimesWithTransit_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        ulong got = Nearest(Matrix(new GenHasher(), w.WalkTransit), w.Sites.Select(t => t.NearestTransit)).Value;
        Assert.Equal(Golden[name].WalkTransitGroup, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AllLabelsAndPrims_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Labels.Count);
        foreach (string s in w.Labels) h.S(s);
        h.I(w.Prims.Count);
        foreach (Prim q in w.Prims.InOrder()) IslandWorksTests.HashPrim(h, q);
        Assert.Equal(Golden[name].AllPrimsGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Transit_NeverMakesATripSlower(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        for (int a = 0; a < w.Sites.Count; a++)
            for (int b = 0; b < w.Sites.Count; b++)
                Assert.True(w.WalkTransit[a][b] <= w.Walk[a][b] + 1e-9);
    }

    [Fact]
    public void MonorailKey_IsInTheGeneticsSecurityOffice()
    {
        IslandWorks w = TestIslands.Works("Default");
        Objective key = Assert.Single(w.Objectives);
        Assert.Equal("monorail-key", key.Key);
        Assert.Equal("Genetics complex", key.Site);
        Assert.Equal("Administration and security · Security office", key.Room);
        Assert.NotNull(w.Monorail);
        Assert.All(w.Monorail!.Stations, st => Assert.True(st.Shaft >= 20, st.Name));
    }
}
