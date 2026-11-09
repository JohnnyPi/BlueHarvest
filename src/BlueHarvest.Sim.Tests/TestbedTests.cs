using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Checks on the real testbed file. These test structure, not layout, so editing the map
/// in Godot shouldn't break them unless the map itself is broken.
/// </summary>
public class TestbedTests
{
    private static readonly string TestbedPath = FindUp(Path.Combine("godot", "Maps", "testbed.bhmap"))
        ?? throw new FileNotFoundException("Could not find godot/Maps/testbed.bhmap above the test folder.");

    private static string Text => File.ReadAllText(TestbedPath);

    [Fact]
    public void Parses_WithOnePlayerAndOneRex()
    {
        var data = MapFile.Parse(Text);
        Assert.Single(data.Markers, m => m.Kind == MarkerKind.Player);
        Assert.Single(data.Markers, m => m.Kind == MarkerKind.Rex);
    }

    [Fact]
    public void ActorsStartOnPassableTiles()
    {
        var data = MapFile.Parse(Text);
        // Lights can sit anywhere, and sparks belong on a fence.
        foreach (var marker in data.Markers.Where(m => m.Kind is not (MarkerKind.Light or MarkerKind.Sparks)))
        {
            var cover = Covers.Get(data.Map.Cover[marker.Position]);
            Assert.False(cover.BlocksMovement, $"{marker.Kind} at {marker.Position} starts on {cover.Name}.");
        }
    }

    [Fact]
    public void RoundTripsThroughSerializer()
    {
        string once = MapFile.Serialize(MapFile.Parse(Text));
        string twice = MapFile.Serialize(MapFile.Parse(once));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void RunsDeterministically()
    {
        var data = MapFile.Parse(Text);
        var a = ScenarioBuilder.Build(data, seed: 42);
        var b = ScenarioBuilder.Build(MapFile.Parse(Text), seed: 42);
        a.RunUntil(Ticks.FromSeconds(120));
        b.RunUntil(Ticks.FromSeconds(120));
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void PlayerMarker_GetsThePlayerController()
    {
        var player = new PlayerController();
        var sim = ScenarioBuilder.Build(MapFile.Parse(Text), seed: 1, player);
        Assert.Single(sim.Actors, a => a.Controller == player);
        Assert.Equal(StepResult.AwaitingInput, sim.RunUntilInputNeeded());
    }

    [Fact]
    public void RexAndNpcsGetBrains()
    {
        var sim = ScenarioBuilder.Build(MapFile.Parse(Text), seed: 1, new PlayerController());
        Assert.IsType<CreatureBrain>(Assert.Single(sim.Actors, a => a.Species == Species.Rex).Controller);
        Assert.All(sim.Actors.Where(a => a.Glyph == 'h'), a => Assert.IsType<HumanBrain>(a.Controller));
    }

    [Fact]
    public void LightsAndAmbient_ComeFromTheFile()
    {
        var data = MapFile.Parse(Text);
        var sim = ScenarioBuilder.Build(data, seed: 1);
        // Sparks bring a flickering light of their own.
        int lightMarkers = data.Markers.Count(m => m.Kind is MarkerKind.Light or MarkerKind.Headlight or MarkerKind.Sparks);

        Assert.Equal(lightMarkers, sim.Lights.Count);
        Assert.Equal(data.AmbientLight, sim.AmbientLightPercent);
    }

    [Fact]
    public void HeadlightFacing_PointsOutOfTheVehicleFront()
    {
        var map = new WorldMap(6, 2);
        for (int x = 1; x <= 4; x++)
        {
            map.Cover[new TilePos(x, 0)] = CoverType.Vehicle;
            map.Cover[new TilePos(x, 1)] = CoverType.Vehicle;
        }

        Assert.Equal(Direction.East, ScenarioBuilder.HeadlightFacing(map, new TilePos(4, 0)));
        Assert.Equal(Direction.West, ScenarioBuilder.HeadlightFacing(map, new TilePos(1, 1)));
        Assert.Null(ScenarioBuilder.HeadlightFacing(map, new TilePos(0, 0)));
    }

    private static string? FindUp(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
