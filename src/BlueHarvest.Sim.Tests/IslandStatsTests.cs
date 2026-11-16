using BlueHarvest.Sim.World.Generation;
using Xunit;
using Xunit.Abstractions;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Step 24a: island measures and the validation report. Everything here is asserted except
/// <see cref="GoldenValues_Print"/>, which prints the golden islands' measures for the establishing run;
/// step 24b pins them.
/// </summary>
public class IslandStatsTests
{
    private readonly ITestOutputHelper _output;

    public IslandStatsTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> CaseNames => TestIslands.Cases.Keys.Select(k => new object[] { k });

    // measured once per test run, like the islands themselves (measuring only reads the shared worlds)
    private static readonly Dictionary<string, Lazy<IslandStats>> Measured =
        TestIslands.Cases.ToDictionary(kv => kv.Key, kv => new Lazy<IslandStats>(() => IslandStats.Measure(TestIslands.World(kv.Key))));

    private static IslandStats Stats(string name) => Measured[name].Value;

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Measures_AddUp(string name)
    {
        IslandWorld world = TestIslands.World(name);
        IslandStats s = Stats(name);

        Assert.Equal(world.Params.Seed, s.Seed);
        Assert.Equal(world.Shape.N, s.Cells);
        Assert.True(s.LandFraction > 0 && s.LandFraction < 1, $"land fraction {s.LandFraction}");
        Assert.True(s.MainKm2 > 0);
        Assert.True(s.MainKm2 >= s.LargestIsletKm2);
        Assert.Equal(s.LandKm2, s.MainKm2 + s.OtherLandKm2.Sum(), 6);
        Assert.True(s.DryCells <= s.LandCells);

        Assert.Equal(s.DryCells, s.SlopeCells.Sum());
        Assert.True(s.LowlandCells <= s.DryCells);
        Assert.True(s.WalkableConnectedCells <= s.WalkableCells);
        Assert.True(s.WalkableCells <= s.DryCells);
        Assert.True(s.HighestM > 0);

        Assert.Equal(world.Shape.Rivers.Count, s.Rivers);
        Assert.True(s.RiversToSea <= s.Rivers);
        Assert.Equal(world.Shape.Lakes.Count, s.Lakes);

        Assert.Equal(s.CoastCells, s.BeachCells + s.RockyCells + s.CliffCells);
        Assert.True(s.MainBeachCells <= s.MainCoastCells);
        Assert.True(s.MainCoastCells <= s.CoastCells);

        Assert.Equal(IslandWorks.Facilities.Length, s.Facilities.Count);
        Assert.Equal(world.Works.Sites.Count, s.FacilitiesPlaced);
        Assert.Equal(IslandWorks.PaddockSpeciesList.Length, s.PaddocksWanted);
        Assert.Equal(world.Works.Paddocks.Count, s.PaddocksPlaced);
        Assert.Equal(world.Works.RoadKm, s.RoadKm);
        Assert.Equal(world.Works.Tunnels, s.Tunnels);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Measuring_IsRepeatable(string name)
    {
        IslandStats again = IslandStats.Measure(TestIslands.World(name));
        Assert.Equal(IslandReport.StatsRow(Stats(name)), IslandReport.StatsRow(again));
    }

    [Fact]
    public void DeepAtBaseFlow_StartsAt32CubicMetres()
    {
        // the play map's river bed is 0.3 q^0.4 m deep, and 1.2 m can't be waded: q = 32 m³/s
        Assert.False(IslandStats.DeepAtBaseFlow(1));
        Assert.False(IslandStats.DeepAtBaseFlow(31.9));
        Assert.True(IslandStats.DeepAtBaseFlow(32.1));
        Assert.True(IslandStats.DeepAtBaseFlow(200));
    }

    [Fact]
    public void SlopeBins_SplitAtTheirEdges()
    {
        Assert.Equal(0, IslandStats.SlopeBin(0));
        Assert.Equal(0, IslandStats.SlopeBin(4.99));
        Assert.Equal(1, IslandStats.SlopeBin(5));
        Assert.Equal(4, IslandStats.SlopeBin(37.9));
        Assert.Equal(5, IslandStats.SlopeBin(38));
        Assert.Equal(5, IslandStats.SlopeBin(80));
    }

    [Fact]
    public void Median_OfOddAndEvenCounts()
    {
        Assert.Equal(2.0, IslandReport.Median([3, 1, 2]));
        Assert.Equal(2.5, IslandReport.Median([4, 1, 3, 2]));
        Assert.Equal(0.0, IslandReport.Median([]));
    }

    [Fact]
    public void Report_ShowsSeedsThatGeneratedAndOnesThatFailed()
    {
        IslandSeedResult[] results =
        [
            new IslandSeedResult(7, Stats("Default"), 1.5),
            new IslandSeedResult(8, null, 0.2, "boom, broken"),
        ];
        string md = IslandReport.Markdown(results, new IslandReportSettings(7, 2, 75, 2));
        Assert.Contains("# Island validation report", md);
        Assert.Contains("1 of 2 islands generated", md);
        Assert.Contains("| 8 | boom, broken |", md);
        Assert.Contains("## Measures across the seeds", md);
        Assert.Contains("## Facility placement", md);
        Assert.Contains("## Slopes over all seeds", md);

        string[] lines = IslandReport.Csv(results).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        int fields = lines[0].Split(',').Length;
        Assert.All(lines, line => Assert.Equal(fields, line.Split(',').Length));
        Assert.StartsWith("7,", lines[1]);
        Assert.StartsWith("8,", lines[2]);
        Assert.EndsWith("boom; broken", lines[2]);

        Assert.Equal(IslandReport.StatsHeader().Split(',').Length, IslandReport.StatsRow(Stats("Default")).Split(',').Length);
        Assert.Contains("1 of 2 islands generated.", IslandReport.ConsoleSummary(results));
    }

    /// <summary>
    /// Print mode (decision 7): prints each golden island's measures as a row ready to paste.
    /// Always passes. Step 24b turns these rows into assertions.
    /// </summary>
    [Fact]
    public void GoldenValues_Print()
    {
        _output.WriteLine("// Step 24a establishing run: IslandStats rows for the golden islands.");
        _output.WriteLine("// Columns: " + IslandReport.StatsHeader());
        foreach (string name in TestIslands.Cases.Keys)
            _output.WriteLine($"[\"{name}\"] = \"{IslandReport.StatsRow(Stats(name))}\",");
    }
}
