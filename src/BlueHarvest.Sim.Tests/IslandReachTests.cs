using BlueHarvest.Sim.World.Generation;
using Xunit;
using Xunit.Abstractions;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Steps 24b and 24c: walking routes between the key sites (docks, airstrip, comms relay, control centre, watch posts).
/// Everything here is asserted; <see cref="GoldenRoutes_Print"/> prints the current rows for pinning columns added later.
/// </summary>
public class IslandReachTests
{
    private readonly ITestOutputHelper _output;

    public IslandReachTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> CaseNames => TestIslands.Cases.Keys.Select(k => new object[] { k });

    private static readonly Dictionary<string, Lazy<IslandReach>> Walked =
        TestIslands.Cases.ToDictionary(kv => kv.Key,
            kv => new Lazy<IslandReach>(() => TestIslands.World(kv.Key).Works.Reachability(IslandReach.AnchorKinds)));

    private static IslandReach Reach(string name) => Walked[name].Value;

    // the step 24c establishing run (17 Nov 2026, with the corner rule), pinned in step 24d;
    // compared column by column, so columns added later don't disturb it
    private const string PinnedColumns =
        "anchors,anchor_pairs,anchor_gaps,tunnel_gaps,deep_gaps,no_ground_gaps,must_ford_pairs,slowest_anchor_minutes,worst_dry_detour";

    private static readonly Dictionary<string, string> Pinned = new()
    {
        ["Default"] = "8,28,0,0,0,0,0,698.718161,1.041022",
        ["Seed12"] = "8,28,0,0,0,0,0,601.924755,1.065566",
        ["SmallNoCliffs"] = "8,28,0,0,0,0,0,370.849828,1.036581",
        ["CoolWet"] = "8,28,0,0,0,0,0,661.139865,1.001917",
    };

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Routes_MatchTheEstablishingRun(string name)
    {
        var got = IslandReport.ReachHeader().Split(',')
            .Zip(IslandReport.ReachRow(Reach(name)).Split(','))
            .ToDictionary(p => p.First, p => p.Second);
        string[] keys = PinnedColumns.Split(','), want = Pinned[name].Split(',');
        Assert.Equal(keys.Length, want.Length);
        for (int k = 0; k < keys.Length; k++)
        {
            Assert.True(got.ContainsKey(keys[k]), $"{name}: column {keys[k]} is gone");
            Assert.True(want[k] == got[keys[k]], $"{name} {keys[k]}: pinned {want[k]}, now {got[keys[k]]}");
        }
    }

    [Fact]
    public void ADiagonalWall_CantBeSlippedThroughAtItsCorners()
    {
        // a wall one cell thick along a diagonal of the walking grid splits it in two: stepping diagonally
        // across it means squeezing between two wall cells, which the corner rule refuses
        IslandWorks works = TestIslands.World("Default").Works;
        int nx = works.WalkNx, ny = works.WalkNy;
        foreach (bool falling in new[] { true, false })
        {
            int k = falling ? nx / 2 - ny / 2 : nx / 2 + ny / 2;
            int Side(int cell) => falling ? cell % nx - cell / nx - k : cell % nx + cell / nx - k;
            int SiteSide(int site) => Side(works.WalkCellAt(works.Sites[site].X, works.Sites[site].Y));
            IslandReach reach = works.Reachability(IslandReach.AnchorKinds, cell => Side(cell) == 0);
            // well clear of the wall: a site walks from a cell up to 5 cells from it, which moves Side by up to 10
            var across = reach.Routes.Where(r =>
            {
                int a = SiteSide(r.FromSite), b = SiteSide(r.ToSite);
                return Math.Sign(a) != Math.Sign(b) && Math.Abs(a) > 12 && Math.Abs(b) > 12;
            }).ToList();
            if (across.Count == 0) continue;
            Assert.All(across, r => Assert.True(r.Status is RouteStatus.NoRoute or RouteStatus.NoGround,
                $"{r.From} – {r.To} got across the wall: {r.Status}"));
            return;
        }
        Assert.Fail("neither diagonal wall had key sites well apart on both sides");
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryPairOfKeySites_IsListedOnce(string name)
    {
        IslandWorks works = TestIslands.World(name).Works;
        IslandReach reach = Reach(name);
        var keySites = Enumerable.Range(0, works.Sites.Count)
            .Where(a => IslandReach.AnchorKinds.Contains(works.Sites[a].Kind)).ToList();
        Assert.Equal(keySites, reach.AnchorSites.ToList());
        Assert.Equal(8, keySites.Count); // 2 docks, airstrip, comms relay, control centre, 3 watch posts
        Assert.Equal(keySites.Count * (keySites.Count - 1) / 2, reach.Routes.Count);
        Assert.Equal(reach.Routes.Count, reach.Routes.Select(r => (r.FromSite, r.ToSite)).Distinct().Count());
        Assert.All(reach.Routes, r => Assert.True(r.FromSite < r.ToSite));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void WithNothingShut_TheRoutesMatchTheWalkTable(string name)
    {
        // the refactored timing must give exactly the generator's own numbers
        IslandWorks works = TestIslands.World(name).Works;
        foreach (AnchorRoute r in Reach(name).Routes)
            Assert.Equal(works.Walk[r.FromSite][r.ToSite], r.MinutesAnyWay);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void StatusesAgreeWithTheTimes(string name)
    {
        foreach (AnchorRoute r in Reach(name).Routes)
        {
            if (r.Status == RouteStatus.Ok)
            {
                Assert.False(double.IsPositiveInfinity(r.Minutes));
                Assert.True(r.Minutes >= r.MinutesAnyWay - 1e-9, $"{r.From} – {r.To}: shutting cells made it faster");
                if (r.MustFord) Assert.True(double.IsPositiveInfinity(r.DryMinutes));
                else Assert.True(r.DryMinutes >= r.Minutes - 1e-9, $"{r.From} – {r.To}: the dry route is faster");
            }
            else
            {
                Assert.True(double.IsPositiveInfinity(r.Minutes));
                Assert.False(r.MustFord);
                Assert.True(double.IsPositiveInfinity(r.DryMinutes));
            }
            if (r.Status is RouteStatus.NoGround or RouteStatus.NoRoute) Assert.True(double.IsPositiveInfinity(r.MinutesAnyWay));
            else Assert.False(double.IsPositiveInfinity(r.MinutesAnyWay));
        }
    }

    [Fact]
    public void ShuttingEveryCell_LeavesNoRoute()
    {
        IslandReach reach = TestIslands.World("Default").Works.Reachability(IslandReach.AnchorKinds, _ => true);
        Assert.NotEmpty(reach.Routes);
        Assert.All(reach.Routes, r =>
        {
            Assert.True(r.Status is RouteStatus.NoRoute or RouteStatus.NoGround, $"{r.From} – {r.To}: {r.Status}");
            Assert.True(double.IsPositiveInfinity(r.MinutesAnyWay));
        });
        Assert.False(reach.FullyConnected);
        Assert.Equal(reach.Routes.Count, reach.GroundGaps);
        Assert.Equal(reach.Anchors.Count, reach.Unreached.Count);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void TunnelCells_AreRoadCells(string name)
    {
        IslandWorks works = TestIslands.World(name).Works;
        int tunnelCells = 0;
        for (int i = 0; i < works.TunnelCell.Length; i++)
        {
            if (works.TunnelCell[i] == 0) continue;
            tunnelCells++;
            Assert.NotEqual(0, works.RoadCell[i]);
        }
        if (works.Tunnels > 0) Assert.True(tunnelCells > 0, $"{works.Tunnels} tunnels but no tunnel cells");
    }

    [Fact]
    public void Report_HasTheRoutesSection()
    {
        IslandSeedResult[] results =
        [
            new IslandSeedResult(7, IslandStats.Measure(TestIslands.World("Default")), 1.5, Reach: Reach("Default")),
            new IslandSeedResult(8, null, 0.2, "boom"),
        ];
        string md = IslandReport.Markdown(results, new IslandReportSettings(7, 2, 75, 2));
        Assert.Contains("## Walking routes between key sites", md);
        Assert.Contains("Every pair walkable", md);

        string[] lines = IslandReport.Csv(results).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int fields = lines[0].Split(',').Length;
        Assert.All(lines, line => Assert.Equal(fields, line.Split(',').Length));
        Assert.Contains("anchor_gaps", lines[0]);
        Assert.Equal(IslandReport.ReachHeader().Split(',').Length, IslandReport.ReachRow(Reach("Default")).Split(',').Length);
        Assert.Contains("Key-site routes:", IslandReport.ConsoleSummary(results));
    }

    /// <summary>
    /// Print mode (decision 7): each golden island's routes as a row ready to paste, then the pairs that
    /// can't be walked today. Always passes. Step 24c turns the rows into assertions.
    /// </summary>
    [Fact]
    public void GoldenRoutes_Print()
    {
        _output.WriteLine("// IslandReach rows for the golden islands (all current columns).");
        _output.WriteLine("// Columns: " + IslandReport.ReachHeader());
        foreach (string name in TestIslands.Cases.Keys)
            _output.WriteLine($"[\"{name}\"] = \"{IslandReport.ReachRow(Reach(name))}\",");
        foreach (string name in TestIslands.Cases.Keys)
            foreach (AnchorRoute r in Reach(name).Routes.Where(x => x.Status != RouteStatus.Ok))
                _output.WriteLine($"// {name}: {r.From} – {r.To}: {IslandReach.Describe(r.Status)}");
    }
}
