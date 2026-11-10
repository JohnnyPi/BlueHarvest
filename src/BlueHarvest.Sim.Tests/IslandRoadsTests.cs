using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 4b of the world-generator port: the road network, checked against the island lab
/// (tools/island-lab/golden-roads.js). If IslandWorksTests fail too, fix those first.
/// </summary>
public class IslandRoadsTests
{
    public sealed record Expected(int Roads, int Bridges, int Tunnels, string Gates, ulong RoadGroup, ulong SummaryGroup);

    // node tools/island-lab/golden-roads.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(24, 7, 2, "seswswsnwwewesnsesses", 0x65D658EE4B93A1F7UL, 0x3CD3578BF6163F09UL),
        ["Seed12"] = new(24, 2, 3, "nnswnwwnsnsneswewnsss", 0xFD645CD4F4398C95UL, 0xBBEB75EE5C88741DUL),
        ["SmallNoCliffs"] = new(24, 3, 0, "ewnsnnwsnnwwwnwessnsw", 0xFCDCA234EB8068A0UL, 0x91BF9ACCAE3B73B3UL),
        ["CoolWet"] = new(25, 6, 3, "eesesenewenessswsnnss", 0xB60075D3102DC205UL, 0x588FDD2A56F6D314UL),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    private static string Gates(IslandWorks w) => new(w.Sites.Select(t => t.Gate).ToArray());

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Counts_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        Expected e = Golden[name];
        Assert.Equal(e.Roads, w.Roads.Count);
        Assert.Equal(e.Bridges, w.Bridges);
        Assert.Equal(e.Tunnels, w.Tunnels);
        Assert.Equal(e.Gates, Gates(w));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Roads_MatchReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        var h = new GenHasher().I(w.Roads.Count);
        foreach (Road rd in w.Roads)
        {
            h.I(rd.Class == RoadClass.Main ? 1 : 0).Ds(rd.Xs).Ds(rd.Ys).Ds(rd.Hs).I(rd.Flags.Length);
            foreach (RoadFlags f in rd.Flags) h.I((int)f);
        }
        h.Is(w.RoadCell);
        Assert.Equal(Golden[name].RoadGroup, h.Value);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Summary_MatchesReference(string name)
    {
        IslandWorks w = TestIslands.Works(name);
        ulong got = new GenHasher().D(w.RoadKm).D(w.MainKm).I(w.Bridges).I(w.Tunnels).S(Gates(w)).Value;
        Assert.Equal(Golden[name].SummaryGroup, got);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EverySite_IsOnTheNetwork(string name)
    {
        IslandShape s = TestIslands.Shape(name);
        IslandWorks w = TestIslands.Works(name);
        foreach (Site t in w.Sites)
        {
            int i = (int)Math.Floor(t.Y / s.Cell) * s.Nx + (int)Math.Floor(t.X / s.Cell);
            Assert.True(w.RoadCell[i] != 0, t.Name + " has no road");
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Roads_AreContinuousAndAboveTheSea(string name)
    {
        IslandShape s = TestIslands.Shape(name);
        IslandWorks w = TestIslands.Works(name);
        double maxStep = s.Cell * GenMath.Sqrt2 + 1;
        foreach (Road rd in w.Roads)
        {
            Assert.True(rd.Xs.Length >= 2);
            for (int k = 0; k < rd.Xs.Length; k++)
            {
                Assert.True(rd.Hs[k] >= 1, "road surface below 1 m");
                if (k > 0) Assert.True(GenMath.Hyp(rd.Xs[k] - rd.Xs[k - 1], rd.Ys[k] - rd.Ys[k - 1]) <= maxStep, "gap in road");
            }
        }
    }
}
