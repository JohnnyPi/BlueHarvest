using BlueHarvest.Sim.World.Generation;
using Xunit;
using Xunit.Abstractions;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Steps 24a and 24b: island measures and the validation report. Everything here is asserted except
/// <see cref="GoldenValues_Print"/>, which prints the golden islands' current rows: the establishing run for
/// columns added after the pins below.
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

    // the step 24a establishing run (16 Nov 2026), pinned in step 24b, plus the two pit columns from the
    // step 24b run, pinned in step 24c; re-pinned from the island lab for the Crosswake program (P4c:
    // node tools/island-lab/checks.js --print); compared column by column, so columns added later don't disturb it
    private const string PinnedColumns =
        "land_pct,land_km2,main_km2,islets,largest_islet_km2,rivers,rivers_to_sea_pct,largest_river_q,deep_river_cells,lakes,lake_km2,pit_cells,highest_m,lowland_pct,steep_pct,walkable_pct,walkable_connected_pct,beach_pct,beach_all_pct,facilities_short,paddocks_placed,road_km,bridges,tunnels,slope_bin0_cells,slope_bin1_cells,slope_bin2_cells,slope_bin3_cells,slope_bin4_cells,slope_bin5_cells,placed_court,placed_docks,placed_airstrip,placed_comms,placed_genetics,placed_raptors,placed_orgone,placed_orchard,placed_weather,placed_choir,placed_drowned,placed_geothermal,placed_depot,placed_watch,placed_yard,placed_survey,placed_outstation,coastal_pit_cells,inland_pit_cells";

    private static readonly Dictionary<string, string> Pinned = new()
    {
        ["Default"] = "31.52579,1679.966208,1671.839744,3,5.111808,150,100,6.06677,0,1,0.294912,0,1162.126102,42.403847,2.467835,98.387616,97.924287,34.134078,33.907147,0,5,189.765291,2,1,56315,29163,13298,1213,913,1617,1,2,1,1,1,1,1,1,1,1,1,1,2,3,1,4,2,0,0",
        ["Seed12"] = "28.518409,854.8352,852.819968,2,1.622016,102,100,2.017199,0,1,0.57344,0,1191.598906,37.491369,3.716916,97.543153,97.332183,24.569241,25.077208,0,5,157.355163,4,5,17099,20318,11461,1324,683,1255,1,2,1,1,1,1,1,1,1,1,1,1,2,3,1,4,5,0,0",
        ["SmallNoCliffs"] = "30.999115,412.975104,409.321472,3,1.916928,44,100,1.882016,0,2,0.57344,1,1153.002823,36.800286,1.557348,99.555044,98.72075,17.216643,15.714286,0,5,99.936575,6,2,5672,6695,10028,2384,341,51,1,2,1,1,1,1,1,1,1,1,1,1,2,3,1,4,5,1,0",
        ["CoolWet"] = "33.125272,636.469248,630.767616,9,1.343488,105,100,2.955048,0,1,0.589824,0,1178.215784,39.792842,4.243642,97.191518,96.47265,22.502031,21.901408,0,5,123.011504,8,2,12430,12668,10511,1555,602,1045,1,2,1,1,1,1,1,1,1,1,1,1,2,3,1,4,3,0,0",
    };

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Measures_MatchTheEstablishingRun(string name)
    {
        string[] keys = PinnedColumns.Split(',');
        string[] want = Pinned[name].Split(',');
        Assert.Equal(keys.Length, want.Length);
        var got = IslandReport.StatsHeader().Split(',')
            .Zip(IslandReport.StatsRow(Stats(name)).Split(','))
            .ToDictionary(p => p.First, p => p.Second);
        var wrong = new List<string>();
        for (int k = 0; k < keys.Length; k++)
        {
            if (!got.TryGetValue(keys[k], out string? value)) wrong.Add($"{keys[k]}: missing");
            else if (value != want[k]) wrong.Add($"{keys[k]}: pinned {want[k]}, now {value}");
        }
        Assert.True(wrong.Count == 0, $"{name}: " + string.Join("; ", wrong));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Pits_AreAllOnTheCoast(string name)
    {
        // a pit needs a cell with no lower neighbour, which flooding leaves only on the coast below sea level
        IslandStats s = Stats(name);
        Assert.Equal(s.PitCells, s.CoastalPitCells);
        Assert.Equal(0, s.InlandPitCells);
        Assert.Equal(s.Rivers, s.RiversToSea);
    }

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
    /// Print mode (decision 7): prints each golden island's current measures as a row ready to paste.
    /// Always passes. The pinned columns are asserted above; columns added since are pinned from this output.
    /// </summary>
    [Fact]
    public void GoldenValues_Print()
    {
        _output.WriteLine("// IslandStats rows for the golden islands (all current columns).");
        _output.WriteLine("// Columns: " + IslandReport.StatsHeader());
        foreach (string name in TestIslands.Cases.Keys)
            _output.WriteLine($"[\"{name}\"] = \"{IslandReport.StatsRow(Stats(name))}\",");
    }
}
