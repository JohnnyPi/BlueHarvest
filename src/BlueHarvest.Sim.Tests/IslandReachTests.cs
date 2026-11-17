using BlueHarvest.Sim.World.Generation;
using Xunit;
using Xunit.Abstractions;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Step 24b: walking routes between the key sites (docks, airstrip, comms relay, control centre, watch posts).
/// Everything here is asserted except <see cref="GoldenRoutes_Print"/>, the establishing run that step 24c pins.
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
            }
            else
            {
                Assert.True(double.IsPositiveInfinity(r.Minutes));
                Assert.False(r.MustFord);
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
        _output.WriteLine("// Step 24b establishing run: IslandReach rows for the golden islands.");
        _output.WriteLine("// Columns: " + IslandReport.ReachHeader());
        foreach (string name in TestIslands.Cases.Keys)
            _output.WriteLine($"[\"{name}\"] = \"{IslandReport.ReachRow(Reach(name))}\",");
        foreach (string name in TestIslands.Cases.Keys)
            foreach (AnchorRoute r in Reach(name).Routes.Where(x => x.Status != RouteStatus.Ok))
                _output.WriteLine($"// {name}: {r.From} – {r.To}: {IslandReach.Describe(r.Status)}");
    }
}
