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
        ["Default"] = new(1, 3, 4, 378, 943,
            0x6508374AFCC44764UL, 0x37E3C245F42A4A83UL, 0xD0C5EB116F22B575UL, 0xB301EA2198D0630FUL, 0xA8EFAE773E8ECC38UL, 0xDD40B49FE75CF0F4UL),
        ["Seed12"] = new(1, 4, 4, 426, 1071,
            0xBE53A533F0F77AFEUL, 0x3FFC2CDC5FDF426AUL, 0x5BB6B75E2D98BC6DUL, 0x79619430822C9CB8UL, 0x1F6927CF213E0A71UL, 0x9E7CAB42D3E1B619UL),
        ["SmallNoCliffs"] = new(1, 4, 4, 426, 1059,
            0x79B4FB44B1F168B5UL, 0xED9950036F9BDC11UL, 0x91B51C3DE5CF3D73UL, 0x72798B2F0121278AUL, 0x3E4EC70B4BC044F5UL, 0x70028310F2921208UL),
        ["CoolWet"] = new(1, 4, 4, 394, 977,
            0x430F46135D894E82UL, 0xBFD927F62D1B195DUL, 0xA52FA6B8B00DAA55UL, 0xFCF31881161173C3UL, 0x7940B602CBAB3184UL, 0xBA0EB92AA391EE3DUL),
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
        Assert.Equal(e.Labels, w.RuinLabelStart); // the transit stage's labels: the authored ruins' come after (P4d)
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
        var h = new GenHasher().I(w.RuinLabelStart);
        foreach (string s in w.Labels.Take(w.RuinLabelStart)) h.S(s);
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
    public void MonorailKey_IsAtTheCourt_InCentralAdministrationsSecurityLobby()
    {
        IslandWorks w = TestIslands.Works("Default");
        Objective key = Assert.Single(w.Objectives);
        Assert.Equal("monorail-key", key.Key);
        Assert.Equal("Court of Unfinished Measures", key.Site);
        Assert.Equal("Central Administration entrance · Security lobby", key.Room);
        Assert.NotNull(w.Monorail);
        Assert.All(w.Monorail!.Stations, st => Assert.True(st.Shaft >= 20, st.Name));
    }
}
