using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;
using Cover = BlueHarvest.Sim.World.Generation.Cover;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Stage 6a: island tiles as game tiles, the streaming map window and the .bhmap export.
/// </summary>
public class IslandWindowTests
{
    private const string Island = "SmallNoCliffs";

    private static (double X, double Y) Genetics()
    {
        Site gen = TestIslands.Works(Island).Sites.First(t => t.Kind == "genetics");
        return (gen.X, gen.Y);
    }

    private static IslandWindow WindowAt(double x, double y, int chunks = 4) =>
        new(TestIslands.World(Island), TestIslands.Chunker(Island), x, y, chunks);

    private static void AssertSameTiles(IslandWindow a, IslandWindow b)
    {
        // compare every island tile both windows hold
        for (int y = 0; y < a.Size; y++)
        {
            for (int x = 0; x < a.Size; x++)
            {
                var pa = new TilePos(x, y);
                var pb = new TilePos(x + a.OriginX - b.OriginX, y + a.OriginY - b.OriginY);
                if (!b.Map.InBounds(pb)) continue;
                Assert.Equal(b.Map.Cover[pb], a.Map.Cover[pa]);
                Assert.Equal(b.Map.Heights[pb], a.Map.Heights[pa]);
                Assert.Equal(b.Map.Structures.RoofDm(pb), a.Map.Structures.RoofDm(pa));
                Assert.Equal(b.Map.Structures.DoorAt(pb), a.Map.Structures.DoorAt(pa));
                Assert.Equal(b.Map.Flow[pb], a.Map.Flow[pa]);
            }
        }
    }

    [Fact]
    public void Window_IsCentredOnWholeChunks()
    {
        (double x, double y) = Genetics();
        IslandWindow w = WindowAt(x, y);
        Assert.Equal(256, w.Size);
        Assert.Equal(256, w.Map.Width);
        Assert.Equal(0, w.OriginX % 64);
        Assert.Equal(0, w.OriginY % 64);
        TilePos p = w.ToLocal(x, y);
        Assert.True(w.Map.InBounds(p));
        Assert.False(w.NearEdge(p));
        (double bx, double by) = w.ToWorld(p);
        Assert.Equal(Math.Floor(x) + 0.5, bx);
        Assert.Equal(Math.Floor(y) + 0.5, by);
    }

    [Fact]
    public void Compound_BecomesWallsRoofedFloorsAndClosedDoors()
    {
        (double x, double y) = Genetics();
        WorldMap map = WindowAt(x, y).Map;
        int walls = 0, roofedFloor = 0, closedDoors = 0, fence = 0, equipment = 0;
        for (int j = 0; j < map.Height; j++)
        {
            for (int i = 0; i < map.Width; i++)
            {
                var p = new TilePos(i, j);
                CoverType c = map.Cover[p];
                if (c == CoverType.Wall) walls++;
                if (c == CoverType.Floor && map.IsIndoor(p)) roofedFloor++;
                if (c == CoverType.Door && map.Structures.DoorAt(p) == DoorState.Closed) closedDoors++;
                if (c == CoverType.Fence) fence++;
                if (c == CoverType.Equipment) equipment++;
                if (c != CoverType.Door) Assert.Equal(DoorState.Open, map.Structures.DoorAt(p));
            }
        }
        Assert.True(walls > 200, $"walls {walls}");
        Assert.True(roofedFloor > 500, $"roofed floor {roofedFloor}");
        Assert.True(closedDoors > 10, $"doors {closedDoors}");
        Assert.True(fence > 100, $"fence {fence}");
        Assert.True(equipment > 50, $"equipment {equipment}");
    }

    [Fact]
    public void OpenSea_IsDeepWaterAtSeaLevel()
    {
        WorldMap map = WindowAt(200, 200, 2).Map;
        for (int j = 0; j < map.Height; j++)
        {
            for (int i = 0; i < map.Width; i++)
            {
                var p = new TilePos(i, j);
                Assert.Equal(CoverType.DeepWater, map.Cover[p]);
                Assert.Equal(0, map.Heights[p]);
            }
        }
    }

    [Theory]
    [InlineData(Cover.Deep, 30.0, CoverType.DeepWater)]
    [InlineData(Cover.River, 0.4, CoverType.ShallowWater)]
    [InlineData(Cover.River, 2.0, CoverType.DeepWater)]
    [InlineData(Cover.Beach, 0.0, CoverType.Sand)]
    [InlineData(Cover.Cloud, 0.0, CoverType.DenseJungle)]
    [InlineData(Cover.Road, 0.0, CoverType.Track)]
    [InlineData(Cover.BigWall, 0.0, CoverType.Wall)]
    [InlineData(Cover.Console, 0.0, CoverType.Equipment)]
    public void Covers_MapOntoGameCovers(Cover cover, double depth, CoverType expected) =>
        Assert.Equal(expected, IslandTiles.ToGameCover(cover, depth));

    [Fact]
    public void Heights_SnapToHalfMetres()
    {
        Assert.Equal(0, IslandTiles.ToHeightDm(-3.2));
        Assert.Equal(0, IslandTiles.ToHeightDm(0.2));
        Assert.Equal(5, IslandTiles.ToHeightDm(0.3));
        Assert.Equal(2125, IslandTiles.ToHeightDm(212.4));
        Assert.Equal(11000, IslandTiles.ToHeightDm(1100));
    }

    [Theory]
    [InlineData(70, 0)]
    [InlineData(-70, 0)]
    [InlineData(0, 70)]
    [InlineData(-70, -70)]
    [InlineData(200, -130)]
    public void Recenter_GivesTheSameTilesAsAFreshWindow(int moveX, int moveY)
    {
        (double x, double y) = Genetics();
        IslandWindow w = WindowAt(x, y);
        TilePos start = w.ToLocal(x, y);
        (int dx, int dy) = w.Recenter(new TilePos(start.X + moveX, start.Y + moveY));
        Assert.True(dx != 0 || dy != 0);
        Assert.Equal(0, dx % 64);
        Assert.Equal(0, dy % 64);
        TilePos now = w.ToLocal(x + moveX, y + moveY);
        Assert.Equal(new TilePos(start.X + moveX - dx, start.Y + moveY - dy), now);
        Assert.False(w.NearEdge(now));
        AssertSameTiles(w, WindowAt(x + moveX, y + moveY));
    }

    [Fact]
    public void Recenter_DoesNothingAwayFromTheEdge()
    {
        (double x, double y) = Genetics();
        IslandWindow w = WindowAt(x, y);
        Assert.Equal((0, 0), w.Recenter(w.ToLocal(x, y)));
    }

    [Fact]
    public void Recenter_KeepsChangesToTilesStillInView()
    {
        (double x, double y) = Genetics();
        IslandWindow w = WindowAt(x, y);
        TilePos p = w.ToLocal(x, y);
        w.Map.Cover[p] = CoverType.Rubble;
        w.Map.Structures.SetDeck(p, 20);
        (int dx, int dy) = w.Recenter(new TilePos(138, p.Y)); // one chunk east of the middle
        Assert.Equal((64, 0), (dx, dy));
        var moved = new TilePos(p.X - dx, p.Y - dy);
        Assert.Equal(CoverType.Rubble, w.Map.Cover[moved]);
        Assert.Equal(20, w.Map.Structures.DeckDm(moved));
    }

    [Fact]
    public void Changes_ComeBackWhenTheWindowReturns()
    {
        (double x, double y) = Genetics();
        IslandWindow w = WindowAt(x, y);
        TilePos p = w.ToLocal(x, y);
        w.Map.Cover[p] = CoverType.Rubble;
        w.Map.Structures.SetDoor(p, DoorState.Locked);
        Assert.Equal(0, w.Changes.TileCount); // still in the window: the map holds it
        Assert.Equal(1, w.AllChanges().TileCount);

        // walk right away: every chunk of the window scrolls out
        (int dx, int dy) = w.Recenter(new TilePos(p.X + 640, p.Y));
        Assert.Equal((640, 0), (dx, dy));
        Assert.Equal(1, w.Changes.ChunkCount);
        Assert.Equal(1, w.Changes.TileCount);
        (int cx, int cy) = w.Changes.Chunks[0];
        Assert.Equal(((int)Math.Floor(x / 64), (int)Math.Floor(y / 64)), (cx, cy));
        TileChange kept = Assert.Single(w.Changes.For(cx, cy));
        Assert.Equal(CoverType.Rubble, kept.Cover);
        Assert.Equal(DoorState.Locked, kept.Door);
        Assert.Equal(1, w.AllChanges().TileCount);

        // and back: the tile is as it was left, and the map holds it again
        w.Recenter(w.ToLocal(x, y));
        TilePos back = w.ToLocal(x, y);
        Assert.Equal(p, back);
        Assert.Equal(CoverType.Rubble, w.Map.Cover[back]);
        Assert.Equal(DoorState.Locked, w.Map.Structures.DoorAt(back));
        Assert.Equal(0, w.Changes.TileCount);
        Assert.Equal(1, w.AllChanges().TileCount);
    }

    [Fact]
    public void UntouchedChunks_KeepNothing()
    {
        (double x, double y) = Genetics();
        IslandWindow w = WindowAt(x, y);
        TilePos p = w.ToLocal(x, y);
        w.Recenter(new TilePos(p.X + 640, p.Y));
        Assert.Equal(0, w.Changes.ChunkCount);
        Assert.Equal(0, w.AllChanges().TileCount);
    }

    [Fact]
    public void ATileChangedBack_IsNotAChange()
    {
        (double x, double y) = Genetics();
        IslandWindow w = WindowAt(x, y);
        TilePos p = w.ToLocal(x, y);
        CoverType made = w.Map.Cover[p];
        w.Map.Cover[p] = CoverType.Rubble;
        w.Map.Cover[p] = made;
        w.Recenter(new TilePos(p.X + 640, p.Y));
        Assert.Equal(0, w.Changes.TileCount);
    }

    [Fact]
    public void AWindowStartedWithChanges_ShowsThem()
    {
        // a loaded game: the same island, with what was done to it
        (double x, double y) = Genetics();
        IslandWindow first = WindowAt(x, y);
        TilePos p = first.ToLocal(x, y);
        first.Map.Cover[p] = CoverType.Rubble;
        var loaded = new IslandWindow(TestIslands.World(Island), TestIslands.Chunker(Island), x, y, 4, first.AllChanges());
        Assert.Equal(CoverType.Rubble, loaded.Map.Cover[p]);
        Assert.Equal(1, loaded.AllChanges().TileCount);
    }

    [Fact]
    public void Rivers_RunDownstreamAndOnlyWaterHasACurrent()
    {
        // a window on the middle of the island's longest river
        River river = TestIslands.Shape(Island).Rivers.OrderByDescending(r => r.Xs.Length).First();
        int mid = river.Xs.Length / 2;
        IslandWindow w = WindowAt(river.Xs[mid], river.Ys[mid]);
        int running = 0, checkedPairs = 0, rises = 0;
        for (int y = 0; y < w.Size; y++)
        {
            for (int x = 0; x < w.Size; x++)
            {
                var p = new TilePos(x, y);
                WaterFlow flow = w.Map.Flow[p];
                if (flow.IsNone) continue;
                running++;
                Assert.True(w.Map.Cover[p] is CoverType.ShallowWater or CoverType.DeepWater, $"{w.Map.Cover[p]} at {p} has a current");
                Assert.InRange(flow.SpeedDm, 1, 61);
                // six metres on along the current, the water is no higher (rivers never rise downstream)
                double speed = Math.Sqrt(flow.X * flow.X + flow.Y * flow.Y);
                var on = new TilePos(x + (int)Math.Round(flow.X / speed * 6), y + (int)Math.Round(flow.Y / speed * 6));
                if (!w.Map.InBounds(on) || w.Map.Flow[on].IsNone) continue;
                checkedPairs++;
                if (w.Map.Heights[on] > w.Map.Heights[p]) rises++;
            }
        }
        Assert.True(running > 50, $"only {running} tiles of running water around the river");
        Assert.True(checkedPairs > 20, $"only {checkedPairs} pairs to compare");
        // where a tributary joins, half-metre rounding can put the main river a step higher: allow a few
        Assert.True(rises * 20 <= checkedPairs, $"{rises} of {checkedPairs} tiles run uphill");
    }

    [Fact]
    public void TheOpenSea_HasNoCurrent()
    {
        IslandWindow w = WindowAt(200, 200, 2);
        for (int y = 0; y < w.Size; y++)
            for (int x = 0; x < w.Size; x++)
                Assert.True(w.Map.Flow[new TilePos(x, y)].IsNone);
    }

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(5, 0.01)]
    [InlineData(40, 0.2)]
    public void RiverSpeed_IsFasterOnSteeperReaches(double q, double drop)
    {
        double flat = IslandChunker.RiverSpeed(q, 0), here = IslandChunker.RiverSpeed(q, drop), steeper = IslandChunker.RiverSpeed(q, drop + 0.05);
        Assert.InRange(here, 0.2, 6);
        Assert.True(here >= flat);
        Assert.True(steeper >= here);
    }

    [Fact]
    public void Scroll_MovesEveryLayerAndBlanksWhatComesIn()
    {
        var map = new WorldMap(4, 3);
        var p = new TilePos(2, 1);
        map.Heights[p] = 35; map.Cover[p] = CoverType.Wall; map.Structures.SetRoof(p, 30); map.Structures.SetDoor(p, DoorState.Locked); map.Structures.SetLadder(p, true);
        map.Flow[p] = new WaterFlow(12, -5);
        map.Scroll(1, -1);
        var q = new TilePos(1, 2);
        Assert.Equal(new WaterFlow(12, -5), map.Flow[q]);
        Assert.Equal(WaterFlow.None, map.Flow[p]);
        Assert.Equal(35, map.Heights[q]);
        Assert.Equal(CoverType.Wall, map.Cover[q]);
        Assert.Equal(30, map.Structures.RoofDm(q));
        Assert.Equal(DoorState.Locked, map.Structures.DoorAt(q));
        Assert.True(map.Structures.HasLadder(q));
        Assert.Equal(0, map.Heights[p]);
        Assert.Equal(CoverType.Grass, map.Cover[p]);
        map.Scroll(-1, 1);
        Assert.Equal(35, map.Heights[p]);
        map.Scroll(9, 0);
        Assert.Equal(0, map.Heights[p]);
    }

    [Fact]
    public void Export_RoundTripsThroughTheMapFile()
    {
        (double x, double y) = Genetics();
        IslandWindow w = WindowAt(x, y, 2);
        string text = IslandWindow.ExportBhmap(TestIslands.World(Island), TestIslands.Chunker(Island), x, y, 2);
        MapData data = MapFile.Parse(text);
        Assert.Equal(128, data.Map.Width);
        MapMarker player = Assert.Single(data.Markers);
        Assert.Equal(MarkerKind.Player, player.Kind);
        Assert.False(data.Map.CoverAt(player.Position).BlocksMovement);
        for (int j = 0; j < 128; j++)
        {
            for (int i = 0; i < 128; i++)
            {
                var p = new TilePos(i, j);
                Assert.Equal(w.Map.Cover[p], data.Map.Cover[p]);
                Assert.Equal(w.Map.Heights[p], data.Map.Heights[p]);
                Assert.Equal(w.Map.Structures.RoofDm(p), data.Map.Structures.RoofDm(p));
                Assert.Equal(w.Map.Structures.DoorAt(p), data.Map.Structures.DoorAt(p));
            }
        }
    }

    [Fact]
    public void NewCovers_HaveSensibleRules()
    {
        Assert.True(Covers.Get(CoverType.DeepWater).BlocksMovement);
        Assert.False(Covers.Get(CoverType.ShallowWater).BlocksMovement);
        Assert.True(Covers.Get(CoverType.ShallowWater).MoveCostPercent > Covers.Get(CoverType.Mud).MoveCostPercent);
        Assert.True(Covers.Get(CoverType.Equipment).BlocksMovement);
        Assert.Equal(CoverType.Mud, Covers.Get(CoverType.Reeds).BrokenInto);
    }
}
