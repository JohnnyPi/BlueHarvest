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
    [InlineData(MapLayer.Areas)]
    public void DebugPictures_AreOpaqueRepeatableAndDifferFromTheOutline(MapLayer layer)
    {
        IslandWorld w = Small.Value;
        byte[] a = IslandMapImage.Render(w, layer), b = IslandMapImage.Render(w, layer), outline = IslandMapImage.Render(w, MapLayer.Outline);
        Assert.Equal(a, b);
        Assert.NotEqual(outline, a);
        for (int i = 3; i < a.Length; i += 4) Assert.Equal(255, a[i]);
    }
    // ---------- step 25b-3: the survivor's map and the areas layer ----------

    private static (byte, byte, byte) Pixel(byte[] px, int cell) => (px[4 * cell], px[4 * cell + 1], px[4 * cell + 2]);

    [Fact]
    public void KnownPicture_WithNothingKnown_IsTheOutline()
    {
        IslandWorld w = Small.Value;
        var known = new BlueHarvest.Sim.World.MapKnowledge(w.Shape.Nx, w.Shape.Ny);
        Assert.Equal(IslandMapImage.Render(w, MapLayer.Outline), IslandMapImage.RenderKnown(w, known));
    }

    [Fact]
    public void KnownPicture_ShowsSeenCellsAsTheyLook_AndTheRestAsTheOutline()
    {
        IslandWorld w = Small.Value;
        IslandShape s = w.Shape;
        var known = new BlueHarvest.Sim.World.MapKnowledge(s.Nx, s.Ny);
        Site genetics = w.Works.Sites.First(t => t.Kind == "genetics");
        int gx = (int)(genetics.X / s.Cell), gy = (int)(genetics.Y / s.Cell);
        for (int y = gy - 8; y <= gy + 8; y++)
            for (int x = gx - 8; x <= gx + 8; x++)
                known.See(y * s.Nx + x);
        byte[] px = IslandMapImage.RenderKnown(w, known), land = IslandMapImage.Render(w, MapLayer.Biomes), outline = IslandMapImage.Render(w, MapLayer.Outline);
        Assert.Equal(17 * 17, known.SeenCount);
        for (int i = 0; i < s.N; i++)
        {
            Assert.Equal(known.HasSeen(i) ? Pixel(land, i) : Pixel(outline, i), Pixel(px, i));
            Assert.Equal(255, px[4 * i + 3]);
        }
    }

    [Fact]
    public void KnownPicture_ShowsChartedSheetsMuted_AndSeenGroundAsItLooks()
    {
        IslandWorld w = Small.Value;
        IslandShape s = w.Shape;
        FacilityAreas areas = w.Areas();
        int site = w.Works.Sites.ToList().FindIndex(t => t.Kind == "genetics"), sheet = areas.FacilitySheet(site);
        IReadOnlyList<int> cells = areas.SheetCells(site, sheet);
        Assert.NotEmpty(cells);
        var known = new BlueHarvest.Sim.World.MapKnowledge(s.Nx, s.Ny);
        known.Chart(site, sheet);
        known.Chart(999, 0); // a sheet this island hasn't got: ignored
        known.See(cells[0]);
        var inSheet = new HashSet<int>(cells);
        bool[] charted = IslandMapImage.ChartedCells(w, known);
        byte[] px = IslandMapImage.RenderKnown(w, known), land = IslandMapImage.Render(w, MapLayer.Biomes), outline = IslandMapImage.Render(w, MapLayer.Outline);
        for (int i = 0; i < s.N; i++)
        {
            Assert.Equal(inSheet.Contains(i), charted[i]);
            if (known.HasSeen(i))
                Assert.Equal(Pixel(land, i), Pixel(px, i));
            else if (!inSheet.Contains(i))
                Assert.Equal(Pixel(outline, i), Pixel(px, i));
            else
            {
                // greyer and darker than the ground itself
                Assert.True(px[4 * i] + px[4 * i + 1] + px[4 * i + 2] < land[4 * i] + land[4 * i + 1] + land[4 * i + 2], $"cell {i}");
            }
        }
    }

    [Fact]
    public void AreasPicture_GivesEachAreaItsOwnColours_AndTheSeaTheOutlines()
    {
        IslandWorld w = Small.Value;
        IslandShape s = w.Shape;
        FacilityAreas areas = w.Areas();
        byte[] px = IslandMapImage.Render(w, MapLayer.Areas), outline = IslandMapImage.Render(w, MapLayer.Outline);
        Assert.Equal(px, IslandMapImage.RenderAreas(w));
        var areaOf = new Dictionary<(byte, byte, byte), int>();
        var colourOf = new Dictionary<(int, int), (byte, byte, byte)>();
        for (int i = 0; i < s.N; i++)
        {
            Assert.Equal(255, px[4 * i + 3]);
            int a = areas.AreaOf(i);
            if (a < 0)
            {
                Assert.Equal(Pixel(outline, i), Pixel(px, i));
                continue;
            }
            int x = i % s.Nx, y = i / s.Nx;
            bool inside = x > 0 && y > 0 && x < s.Nx - 1 && y < s.Ny - 1
                && new[] { i - 1, i + 1, i - s.Nx, i + s.Nx }.All(j => areas.AreaOf(j) == a && areas.SheetOf(j) == areas.SheetOf(i));
            if (!inside)
                continue;
            (byte, byte, byte) c = Pixel(px, i);
            if (areaOf.TryGetValue(c, out int other))
                Assert.Equal(other, a);
            else
                areaOf[c] = a;
            if (colourOf.TryGetValue((a, areas.SheetOf(i)), out var same))
                Assert.Equal(same, c);
            else
                colourOf[(a, areas.SheetOf(i))] = c;
        }
        Assert.True(areaOf.Values.Distinct().Count() >= areas.Count / 2, $"{areaOf.Values.Distinct().Count()} of {areas.Count} areas have inner cells");
    }
}
