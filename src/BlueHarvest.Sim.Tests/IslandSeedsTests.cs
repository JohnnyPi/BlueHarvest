using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Step 24d: the explorer's Random button asks <see cref="IslandSeeds.FirstPassing"/> for an island. Every seed
/// at the base scale passes today, so the checks are replaced with a stand-in to make a seed fail. The islands
/// are generated at half scale to keep the tests quick.
/// </summary>
public class IslandSeedsTests
{
    private static readonly IslandParams Small = new() { Scale = 50 };

    private static IslandValidation Failing(IslandWorld world) => new(
        [new CheckResult("lowland", "Lowland", CheckSeverity.Fail, $"lowland test failure on seed {world.Params.Seed}")], []);

    private static IslandValidation Passing(IslandWorld world) => new([], []);

    [Fact]
    public void AFailingSeed_IsSkipped_AndTheNextIsShown()
    {
        SeedSearch found = IslandSeeds.FirstPassing(Small, [3, 4], validate: w => w.Params.Seed == 3 ? Failing(w) : Passing(w));
        Assert.True(found.Found);
        Assert.Equal(4u, found.World.Params.Seed);
        Assert.Equal(50, found.World.Params.Scale);
        SkippedSeed skipped = Assert.Single(found.Skipped);
        Assert.Equal(3u, skipped.Seed);
        Assert.Equal("skipped seed 3: lowland test failure on seed 3", skipped.Describe());
    }

    [Fact]
    public void ASingleSeed_IsShownEvenWhenItFails()
    {
        SeedSearch found = IslandSeeds.FirstPassing(Small, [3], validate: Failing);
        Assert.False(found.Found);
        Assert.Equal(3u, found.World.Params.Seed);
        Assert.Empty(found.Skipped);
    }

    [Fact]
    public void WithTheRealChecks_TheFirstSeedThatPassesIsKept()
    {
        var stages = new List<string>();
        SeedSearch found = IslandSeeds.FirstPassing(Small, [3, 4], stage => { lock (stages) stages.Add(stage); });
        Assert.Equal(found.Found, found.World.Validate().Passed);
        if (found.Found) Assert.Equal(found.Skipped.Count == 0 ? 3u : 4u, found.World.Params.Seed);
        Assert.Contains("Checking the island", stages);
    }

    [Fact]
    public void NoSeeds_IsAnError()
    {
        Assert.Throws<ArgumentException>(() => IslandSeeds.FirstPassing(Small, []));
    }
}
