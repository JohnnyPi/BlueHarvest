using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>The whole-island entry point and the map pictures the front end shows.</summary>
public class IslandWorldTests
{
    private static readonly List<string> Stages = [];

    private static readonly Lazy<IslandWorld> Small = new(() =>
        IslandWorld.Generate(TestIslands.Cases["SmallNoCliffs"], stage => { lock (Stages) Stages.Add(stage); }));

    [Fact]
    public void Generate_BuildsEveryStageAndReportsProgress()
    {
        IslandWorld w = Small.Value;
        Assert.Equal(336, w.Shape.Nx);
        Assert.Equal(w.Shape.N, w.Climate.Biomes.Length);
        Assert.Equal(21, w.Works.Sites.Count);
        Assert.Equal(336 * 128.0, w.WidthM);
        lock (Stages) Assert.Equal(3, Stages.Count);
    }

    [Fact]
    public void Generate_MatchesTheStagesRunSeparately()
    {
        IslandWorld w = Small.Value;
        IslandShape shape = IslandShape.Generate(TestIslands.Cases["SmallNoCliffs"]);
        IslandWorks separate = IslandWorks.Compute(shape, IslandClimate.Compute(shape));
        Assert.Equal(separate.Sites.Select(t => (t.Name, t.X, t.Y)), w.Works.Sites.Select(t => (t.Name, t.X, t.Y)));
        Assert.Equal(separate.RoadKm, w.Works.RoadKm);
    }

    [Fact]
    public void OutlinePicture_ShowsOnlySeaLandAndCoast()
    {
        IslandWorld w = Small.Value;
        byte[] px = IslandMapImage.Render(w, MapLayer.Outline);
        Assert.Equal(w.Shape.N * 4, px.Length);
        var colors = new HashSet<(byte, byte, byte)>();
        for (int i = 0; i < w.Shape.N; i++)
        {
            colors.Add((px[4 * i], px[4 * i + 1], px[4 * i + 2]));
            Assert.Equal(255, px[4 * i + 3]);
        }
        Assert.Equal(3, colors.Count);
    }

    [Theory]
    [InlineData(MapLayer.Biomes)]
    [InlineData(MapLayer.Height)]
    [InlineData(MapLayer.Rain)]
    [InlineData(MapLayer.Regions)]
    public void DebugPictures_AreOpaqueRepeatableAndDifferFromTheOutline(MapLayer layer)
    {
        IslandWorld w = Small.Value;
        byte[] a = IslandMapImage.Render(w, layer), b = IslandMapImage.Render(w, layer), outline = IslandMapImage.Render(w, MapLayer.Outline);
        Assert.Equal(a, b);
        Assert.NotEqual(outline, a);
        for (int i = 3; i < a.Length; i += 4) Assert.Equal(255, a[i]);
    }
}
