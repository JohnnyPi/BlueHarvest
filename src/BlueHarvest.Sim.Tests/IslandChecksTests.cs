using BlueHarvest.Sim.World.Generation;
using Xunit;
using Xunit.Abstractions;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Step 24c: seed checks as data (<see cref="IslandChecks"/>). Each check is tried just past and just short of
/// its threshold on the Default island's measures, edited with record "with" copies, so no island is generated
/// for them. The golden islands' full validation (routes included) is pinned from the step 24c run.
/// </summary>
public class IslandChecksTests
{
    private readonly ITestOutputHelper _output;

    public IslandChecksTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> CaseNames => TestIslands.Cases.Keys.Select(k => new object[] { k });

    private static IslandCheckContext Measures(string name) => TestIslands.World(name).Measures();

    private static IslandStats Default => Measures("Default").Stats;

    /// <summary>Default's routes with every pair walkable, so the route check stays out of the stats checks' way.</summary>
    private static IslandReach AllWalkable(string name)
    {
        IslandReach reach = Measures(name).Reach;
        return reach with { Routes = reach.Routes.Select(r => r with { Status = RouteStatus.Ok }).ToList() };
    }

    private static IslandValidation Run(IslandStats stats, IslandReach? reach = null) =>
        IslandChecks.Run(new IslandCheckContext(stats, reach ?? AllWalkable("Default")));

    private static string[] Keys(IslandValidation v) => v.Failures.Concat(v.Warnings).Select(r => r.Key).ToArray();

    [Fact]
    public void TheTable_HasUniqueKeysAndBothSeverities()
    {
        var keys = IslandChecks.All.Select(c => c.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains(IslandChecks.All, c => c.Severity == CheckSeverity.Fail);
        Assert.Contains(IslandChecks.All, c => c.Severity == CheckSeverity.Warn);
        Assert.Equal(IslandChecks.BeachMinShare, IslandReport.DecidedBeachShare);
    }

    [Theory]
    [InlineData("Default", new string[0])]
    [InlineData("Seed12", new[] { "islets" })]
    [InlineData("SmallNoCliffs", new string[0])]
    [InlineData("CoolWet", new[] { "islets" })]
    public void GoldenIslands_PassTheStatsChecks(string name, string[] expected)
    {
        // from the pinned measures: Seed12 has 2 islets and CoolWet 9; everything else is well inside the thresholds
        IslandValidation v = IslandChecks.Run(new IslandCheckContext(Measures(name).Stats, AllWalkable(name)));
        Assert.Equal(expected, Keys(v));
        Assert.True(v.Passed);
    }

    [Theory]
    [InlineData(0.215, false)]
    [InlineData(0.225, true)]
    [InlineData(0.415, true)]
    [InlineData(0.425, false)]
    public void Land_MustBeInItsBand(double share, bool passes)
    {
        IslandStats s = Default with { LandCells = (int)(Default.Cells * share) };
        Assert.Equal(passes, !Keys(Run(s)).Contains("land"));
    }

    [Theory]
    [InlineData(0.295, false)]
    [InlineData(0.305, true)]
    public void Lowland_NeedsThirtyPercent(double share, bool passes)
    {
        IslandStats s = Default with { LowlandCells = (int)(Default.DryCells * share) };
        IslandValidation v = Run(s);
        Assert.Equal(passes, !Keys(v).Contains("lowland"));
        if (!passes) Assert.Contains(v.Failures, f => f.Key == "lowland" && f.Reason.Contains("need 30%"));
    }

    [Theory]
    [InlineData(0.145, false)]
    [InlineData(0.155, true)]
    public void Beach_NeedsFifteenPercent(double share, bool passes)
    {
        IslandStats s = Default with { MainBeachCells = (int)(Default.MainCoastCells * share) };
        Assert.Equal(passes, !Keys(Run(s)).Contains("beach"));
    }

    [Theory]
    [InlineData(0.925, false)]
    [InlineData(0.935, true)]
    public void WalkableGround_NeedsNinetyThreePercent(double share, bool passes)
    {
        IslandStats s = Default with { WalkableConnectedCells = (int)(Default.DryCells * share) };
        Assert.Equal(passes, !Keys(Run(s)).Contains("walkable"));
    }

    [Fact]
    public void AMajorFacilityShort_Fails()
    {
        IslandStats s = Default with { Facilities = Default.Facilities.Select(f => f.Key == "docks" ? f with { Placed = 1 } : f).ToList() };
        IslandValidation v = Run(s);
        Assert.Contains(v.Failures, f => f.Key == "facilities" && f.Reason.Contains("1 of 2"));
        Assert.False(v.Passed);
    }

    [Fact]
    public void AMinorFacilityShort_OnlyWarns_UnlessNoneArePlaced()
    {
        IslandStats one = Default with { Facilities = Default.Facilities.Select(f => f.Key == "watch" ? f with { Placed = 2 } : f).ToList() };
        IslandValidation v = Run(one);
        Assert.Equal(new[] { "facilities-minor" }, Keys(v));
        Assert.True(v.Passed);

        IslandStats none = Default with { Facilities = Default.Facilities.Select(f => f.Key == "watch" ? f with { Placed = 0 } : f).ToList() };
        Assert.Contains(Run(none).Failures, f => f.Key == "facilities");
    }

    [Fact]
    public void AMissingPaddock_Fails()
    {
        IslandValidation v = Run(Default with { PaddocksPlaced = Default.PaddocksWanted - 1 });
        Assert.Equal(new[] { "paddocks" }, v.Failures.Select(f => f.Key).ToArray());
    }

    [Fact]
    public void RiversThatMissTheSea_OrInlandPits_OnlyWarn()
    {
        IslandValidation short1 = Run(Default with { RiversToSea = Default.Rivers - 1 });
        Assert.Equal(new[] { "rivers" }, Keys(short1));
        Assert.True(short1.Passed);

        IslandValidation pit = Run(Default with { PitCells = 1, CoastalPitCells = 0 });
        Assert.Equal(new[] { "rivers" }, Keys(pit));
        Assert.True(pit.Passed);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void Islets_ThreeToEight_OrAWarning(int islets, bool passes)
    {
        IslandStats s = Default with { OtherLandKm2 = Enumerable.Repeat(1.0, islets).ToList() };
        IslandValidation v = Run(s);
        Assert.Equal(passes, !Keys(v).Contains("islets"));
        Assert.True(v.Passed);
    }

    [Fact]
    public void AKeySitePairThatCantBeWalked_Fails()
    {
        IslandReach walkable = AllWalkable("Default");
        IslandReach gap = walkable with
        {
            Routes = walkable.Routes.Select((r, i) => i == 0 ? r with { Status = RouteStatus.NeedsTunnel } : r).ToList(),
        };
        IslandValidation v = Run(Default, gap);
        Assert.Contains(v.Failures, f => f.Key == "anchors" && f.Reason.Contains("1 need a tunnel"));
        Assert.True(Run(Default, walkable).Passed);
    }

    [Fact]
    public void ABrokenIsland_FailsWithNamedReasons()
    {
        // its own island, never a shared one: the land mask is cleared after generation
        IslandWorld world = IslandWorld.Generate(new IslandParams { Seed = 3, Scale = 50 });
        Array.Clear(world.Shape.Land);
        IslandValidation v = world.Validate();
        string[] failed = v.Failures.Select(f => f.Key).ToArray();
        Assert.Contains("land", failed);
        Assert.Contains("lowland", failed);
        Assert.Contains("beach", failed);
        Assert.Contains("walkable", failed);
        Assert.False(v.Passed);
        Assert.Contains("land 0% of the frame", v.FailureSummary);
    }

    [Fact]
    public void Validate_RunsOnceAndAgreesWithTheTable()
    {
        IslandWorld world = TestIslands.World("Seed12");
        IslandValidation first = world.Validate();
        Assert.Same(first, world.Validate());
        Assert.Same(world.Measures(), world.Measures());
        Assert.Equal(Keys(IslandChecks.Run(world.Measures())), Keys(first));
    }

    [Fact]
    public void Report_HasTheChecksSection()
    {
        IslandWorld world = TestIslands.World("Seed12");
        IslandCheckContext m = world.Measures();
        IslandSeedResult[] results =
        [
            new IslandSeedResult(12, m.Stats, 1.0, Reach: m.Reach, Validation: world.Validate()),
            new IslandSeedResult(13, null, 0.2, "boom"),
        ];
        string md = IslandReport.Markdown(results, new IslandReportSettings(12, 2, 75, 1));
        Assert.Contains("## Checks", md);
        Assert.Contains("| Islets (`islets`) | Warn | 1 | 12 |", md);

        string[] lines = IslandReport.Csv(results).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int fields = lines[0].Split(',').Length;
        Assert.All(lines, line => Assert.Equal(fields, line.Split(',').Length));
        Assert.Contains("passed,failed_checks,warned_checks", lines[0]);
        Assert.Contains("Checks: ", IslandReport.ConsoleSummary(results));
    }

    // the step 24c establishing run (17 Nov 2026), pinned in step 24d
    [Theory]
    [InlineData("Default", "1,,")]
    [InlineData("Seed12", "1,,islets")]
    [InlineData("SmallNoCliffs", "1,,")]
    [InlineData("CoolWet", "1,,islets")]
    public void GoldenIslands_MatchTheEstablishingRun(string name, string pinned)
    {
        Assert.Equal(pinned, IslandReport.ChecksRow(TestIslands.World(name).Validate()));
    }

    /// <summary>Prints each golden island's full validation as a row ready to paste. Always passes.</summary>
    [Fact]
    public void GoldenValidation_Print()
    {
        _output.WriteLine("// Validation rows for the golden islands.");
        _output.WriteLine("// Columns: " + IslandReport.ChecksHeader());
        foreach (string name in TestIslands.Cases.Keys)
            _output.WriteLine($"[\"{name}\"] = \"{IslandReport.ChecksRow(TestIslands.World(name).Validate())}\",");
        foreach (string name in TestIslands.Cases.Keys)
        {
            IslandValidation v = TestIslands.World(name).Validate();
            foreach (CheckResult r in v.Failures.Concat(v.Warnings)) _output.WriteLine($"// {name}: {r.Severity} {r}");
        }
    }
}
