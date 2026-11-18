using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Step 25b-2: facility areas and park maps. The areas and sheets against the island lab
/// (tools/island-lab/golden-areas.js); park maps placed and tagged by the loot table; keycards outside the
/// doors they open; and in the simulation, picking a map up charts its sheet and reading it may turn up
/// something marked on it.
/// </summary>
public class FacilityAreasTests
{
    /// <param name="Land">Land cells, each in one area.</param>
    /// <param name="AreaHash">Every cell's area (−1 at sea), in order.</param>
    /// <param name="SheetHash">Every cell's sheet (−1 at sea), in order.</param>
    /// <param name="Cells">Cells per area, by site index.</param>
    /// <param name="FacilitySheets">The sheet holding each facility.</param>
    /// <param name="Orders">Each area's sheet order for park maps.</param>
    public sealed record Expected(int Land, ulong AreaHash, ulong SheetHash, string Cells, string FacilitySheets, string Orders);

    // node tools/island-lab/golden-areas.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(57835, 0xB9019825A2D9ADE9UL, 0x1A10F430FB5FFF77UL, "6910,3029,4308,586,4196,2720,44,4274,3292,1042,1184,5056,4024,5240,7503,436,916,1476,1018,275,306", "4,4,4,1,2,2,3,3,4,1,3,2,2,0,1,2,1,3,3,0,3", "41302 43120 41320 12043 23401 21403 34102 30412 41320 12304 30421 23401 23014 03142 14230 24103 10432 34210 31204 03412 32041"),
        ["Seed12"] = new(52175, 0x23D582179D5161DFUL, 0x637C423C9ECA7F75UL, "1410,5781,7123,1623,3028,6283,205,935,3017,1121,3929,1340,3764,4505,2218,1797,917,1169,435,1186,389", "4,3,4,1,1,0,0,1,4,3,1,1,4,3,4,1,2,2,0,0,0", "41203 31042 43120 13204 13042 03421 03214 10432 43201 34201 14023 12340 41203 31402 42310 14032 23401 20341 01324 03142 01243"),
        ["SmallNoCliffs"] = new(25206, 0x35B44B9A05242177UL, 0x095F8FCE20E1917FUL, "2457,1526,1348,379,3796,1073,12,2255,1946,929,1102,1284,1092,1724,1276,404,829,904,388,258,224", "4,4,1,3,4,0,4,4,3,3,2,2,2,0,4,2,2,0,4,2,1", "42310 40132 14320 32410 40132 03412 40321 43102 32410 30421 21403 23104 24130 03214 43021 20413 21403 04231 41032 23104 13024"),
        ["CoolWet"] = new(38847, 0x3FEDAE92C2CFC21DUL, 0x26C2B3499395A515UL, "3347,654,4191,427,4724,3776,162,3083,2470,855,2752,499,1113,1473,1203,952,480,3295,1248,500,1643", "4,4,4,3,2,1,0,1,4,1,1,2,1,3,4,1,0,1,4,1,0", "40132 41203 41302 30412 21043 10423 01234 12340 42301 12034 10342 23014 13042 31204 43201 13420 04213 13420 43201 13204 03124"),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    // ---------- the areas, against the island lab ----------

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AreasAndSheets_MatchTheIslandLab(string island)
    {
        IslandWorld world = TestIslands.World(island);
        FacilityAreas areas = world.Areas();
        int n = world.Shape.N;
        var ha = new GenHasher().I(n);
        var hs = new GenHasher().I(n);
        for (int i = 0; i < n; i++)
        {
            ha.I(areas.AreaOf(i));
            hs.I(areas.SheetOf(i));
        }
        Expected e = Golden[island];
        string cells = string.Join(",", Enumerable.Range(0, areas.Count).Select(a => areas.CellsOf(a).Count));
        Assert.Equal(e.Cells, cells); // first, so a mismatch shows which areas differ
        Assert.Equal(e.Land, Enumerable.Range(0, areas.Count).Sum(a => areas.CellsOf(a).Count));
        Assert.Equal(e.AreaHash, ha.Value);
        Assert.Equal(e.SheetHash, hs.Value);
        Assert.Equal(e.FacilitySheets, string.Join(",", Enumerable.Range(0, areas.Count).Select(areas.FacilitySheet)));
        Assert.Equal(e.Orders, string.Join(" ", Enumerable.Range(0, areas.Count).Select(a => string.Concat(areas.SheetOrder(a)))));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryLandCell_HasOneArea_AndEveryMajorFacilityHasOne(string island)
    {
        IslandWorld world = TestIslands.World(island);
        FacilityAreas areas = world.Areas();
        IslandShape s = world.Shape;
        for (int i = 0; i < s.N; i++)
        {
            if (s.Land[i] != 0)
                Assert.InRange(areas.AreaOf(i), 0, areas.Count - 1);
            else
                Assert.Equal(-1, areas.AreaOf(i));
        }
        Assert.Equal(world.Works.Sites.Count, areas.Count);
        for (int a = 0; a < areas.Count; a++)
        {
            if (world.Works.Sites[a].Major)
                Assert.True(areas.CellsOf(a).Count > 0, $"{world.Works.Sites[a].Name} has no area");
            Assert.All(areas.CellsOf(a), c => Assert.Equal(a, areas.AreaOf(c)));
        }
        Assert.Same(areas, world.Areas()); // kept on the island
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AnAreasFiveSheets_CoverItExactlyOnce_InEqualShares(string island)
    {
        IslandWorld world = TestIslands.World(island);
        FacilityAreas areas = world.Areas();
        for (int a = 0; a < areas.Count; a++)
        {
            IReadOnlyList<int> cells = areas.CellsOf(a);
            var covered = new List<int>();
            for (int k = 0; k < FacilityAreas.SheetCount; k++)
            {
                IReadOnlyList<int> sheet = areas.SheetCells(a, k);
                Assert.InRange(sheet.Count, cells.Count / FacilityAreas.SheetCount, cells.Count / FacilityAreas.SheetCount + 1);
                Assert.All(sheet, c => Assert.Equal(k, areas.SheetOf(c)));
                covered.AddRange(sheet);
            }
            Assert.Equal(cells.OrderBy(c => c), covered.OrderBy(c => c));
            if (cells.Count == 0)
                continue;
            IReadOnlyList<int> order = areas.SheetOrder(a);
            Assert.Equal(Enumerable.Range(0, FacilityAreas.SheetCount), order.OrderBy(k => k));
            Assert.Equal(areas.FacilitySheet(a), order[0]);
            // the facility stands in (or nearest) the sheet a first map charts
            Site t = world.Works.Sites[a];
            double D2(int c)
            {
                double dx = world.Shape.CellX(c) - t.X, dy = world.Shape.CellY(c) - t.Y;
                return dx * dx + dy * dy;
            }
            int nearest = cells.OrderBy(D2).ThenBy(c => c).First();
            Assert.Equal(areas.SheetOf(nearest), order[0]);
        }
    }

    // ---------- park maps and keycards in the loot ----------

    private static readonly HashSet<string> MapRooms = new(StringComparer.Ordinal)
    {
        "Lobby", "Security office", "Offices", "Harbourmaster's office", "Operations office", "Gatehouse", "Shift office",
        "Control room", "Array control", "Observation room", "Office", "Map room", "Radio room", "Keeper's office",
        "Elevator lobby", "Cabin hall",
    };

    /// <summary>Every item the plan made, with where its outermost container lies.</summary>
    private static IEnumerable<(Item Item, long X, long Y)> Everything(ItemLedger ledger) =>
        ledger.All.SelectMany(w => w.Item.SelfAndNested().Select(i => (i, w.X, w.Y)));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ParkMaps_LieInFrontOffices_TaggedWithTheirRoomsArea_TakingSheetsInOrder(string island)
    {
        IslandWorld world = TestIslands.World(island);
        FacilityAreas areas = world.Areas();
        RoomIndex rooms = RoomIndex.Build(world);
        var maps = Everything(ItemPlan.Build(world, runSeed: 1)).Where(e => e.Item.Kind == ItemKind.ParkMap).OrderBy(e => e.Item.Id).ToList();
        Assert.NotEmpty(maps);
        var nth = new Dictionary<int, int>();
        foreach (var (map, x, y) in maps)
        {
            IndexedRoom? room = rooms.At(x, y);
            Assert.True(room is not null, $"a park map lies outside every room, at ({x}, {y})");
            Assert.Contains(room!.Name, MapRooms);
            Assert.True(ParkMaps.TryParse(map.Tag, out int area, out int sheet), $"park map {map.Id} has tag {map.Tag ?? "none"}");
            int expected = room.SiteIndex >= 0 ? room.SiteIndex : areas.AreaAt((room.X0 + room.X1) / 2.0, (room.Y0 + room.Y1) / 2.0);
            Assert.Equal(expected, area);
            int k = nth.GetValueOrDefault(area);
            nth[area] = k + 1;
            IReadOnlyList<int> order = areas.SheetOrder(area);
            Assert.Equal(order[k % order.Count], sheet);
        }
        // watch posts hold none
        Assert.DoesNotContain(maps, m => ParkMaps.TryParse(m.Item.Tag, out int a, out _) && world.Works.Sites[a].Kind == "watch" && rooms.At(m.X, m.Y)?.SiteIndex == a);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryFacilityWithSecureRooms_HasACardForItsTopLevel_OutsideThoseDoors(string island)
    {
        IslandWorld world = TestIslands.World(island);
        RoomIndex rooms = RoomIndex.Build(world);
        IslandWorks w = world.Works;
        var top = new Dictionary<int, int>(); // site → its highest keycard level
        foreach (IndexedRoom r in rooms.Rooms.Where(r => r.SiteIndex >= 0 && r.KeycardLevel > 0))
            top[r.SiteIndex] = Math.Max(top.GetValueOrDefault(r.SiteIndex), r.KeycardLevel);
        Assert.NotEmpty(top);
        // over 20 runs every such facility has, at least once, a card of its top level lying outside its doors of that level
        var reachable = new HashSet<int>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            foreach (var (item, x, y) in Everything(ItemPlan.Build(world, seed)))
            {
                if (item.Kind != ItemKind.Keycard || item.Tag is null)
                    continue;
                foreach ((int site, int level) in top)
                {
                    if (item.Tag == w.Sites[site].Name && item.Level >= level && (rooms.At(x, y)?.KeycardLevel ?? 0) < level)
                        reachable.Add(site);
                }
            }
        }
        var missing = top.Keys.Where(s => !reachable.Contains(s)).Select(s => $"{w.Sites[s].Name} (level {top[s]})").ToList();
        Assert.True(missing.Count == 0, "no reachable top-level card in 20 runs: " + string.Join(", ", missing));
    }

    [Fact]
    public void ParkMapTags_RoundTrip()
    {
        Assert.True(ParkMaps.TryParse(ParkMaps.Tag(12, 3), out int area, out int sheet));
        Assert.Equal((12, 3), (area, sheet));
        Assert.False(ParkMaps.TryParse(null, out _, out _));
        Assert.False(ParkMaps.TryParse("Genetics complex", out _, out _));
        Assert.Equal(new[] { 10, 25, 45, 70 }, Enumerable.Range(0, 4).Select(ParkMaps.FindPercent));
        Assert.Equal(10, ParkMaps.FindPercent(-1));
        Assert.Equal(70, ParkMaps.FindPercent(9));
    }

    // ---------- in the simulation ----------

    private const string Island = "SmallNoCliffs";

    private static (Simulation Sim, PlayerController Drive, Actor You, MapKnowledge Map) Reader(int tracking = 0, bool knowledge = true)
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 7);
        var drive = new PlayerController();
        var loadout = new Loadout("reader", new Skills(Tracking: tracking), Array.Empty<(ItemKind, int)>());
        Actor you = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive, Direction.East, loadout: loadout);
        IslandWorld world = TestIslands.World(Island);
        MapKnowledge map = knowledge ? sim.UseMapKnowledge(you, world) : new MapKnowledge(world.Shape.Nx, world.Shape.Ny);
        sim.RunUntilInputNeeded();
        return (sim, drive, you, map);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    private static Item GiveMap(Simulation sim, PlayerController drive, int area, int sheet)
    {
        Item map = sim.CreateItem(ItemKind.ParkMap, tag: ParkMaps.Tag(area, sheet));
        sim.PlaceItem(map, new TilePos(6, 5));
        Do(sim, drive, new PickUpAction(map.Id));
        return map;
    }

    [Fact]
    public void PickingUpAMap_ChartsItsSheet_AndASecondOfTheSameSheetAddsNothing()
    {
        var (sim, drive, you, map) = Reader();
        var charted = new List<(ChartedSheet Sheet, bool New)>();
        sim.MapCharted += (_, _, s, fresh) => charted.Add((s, fresh));
        GiveMap(sim, drive, 3, 1);
        Assert.True(map.HasCharted(3, 1));
        Assert.Equal(new[] { new ChartedSheet(3, 1) }, map.Sheets.ToArray());
        GiveMap(sim, drive, 3, 1);
        Assert.Single(map.Sheets);
        GiveMap(sim, drive, 3, 2);
        Assert.Equal(2, map.Sheets.Count);
        Assert.False(map.HasCharted(4, 1));
        Assert.Equal(new[] { (new ChartedSheet(3, 1), true), (new ChartedSheet(3, 1), false), (new ChartedSheet(3, 2), true) }, charted.ToArray());
        Assert.Equal(3, you.Inventory.All.Count(i => i.Kind == ItemKind.ParkMap));
    }

    [Fact]
    public void WithoutMapKnowledge_PickingUpAMapChartsNothing()
    {
        var (sim, drive, you, _) = Reader(knowledge: false);
        int events = 0;
        sim.MapCharted += (_, _, _, _) => events++;
        GiveMap(sim, drive, 3, 1);
        Assert.Equal(0, events);
        Assert.Null(sim.Knowledge(you));
    }

    /// <summary>The area (and a sheet of it) of the test island holding the most ruins, wrecks and caves.</summary>
    private static (int Area, int Sheet, List<(FeatureKind Kind, int Id, int Sheet)> Features) Richest()
    {
        IslandWorld world = TestIslands.World(Island);
        FacilityAreas areas = world.Areas();
        IslandWorks w = world.Works;
        var all = new List<(FeatureKind Kind, int Id, int Cell)>();
        all.AddRange(w.Ruins.Select(r => (FeatureKind.Ruin, r.Id, areas.CellAt(r.X, r.Y))));
        all.AddRange(w.Wrecks.Select(r => (FeatureKind.Wreck, r.Id, areas.CellAt(r.X, r.Y))));
        all.AddRange(w.Caves.Select(c =>
        {
            (double x, double y) = c.Mouths.Count > 0 ? c.Mouths[0] : (c.Xs[0], c.Ys[0]);
            return (FeatureKind.Cave, c.Id, areas.CellAt(x, y));
        }));
        var best = all.GroupBy(f => areas.AreaOf(f.Cell)).Where(g => g.Key >= 0).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
        var features = best.Select(f => (f.Kind, f.Id, areas.SheetOf(f.Cell))).ToList();
        return (best.Key, features[0].Item3, features);
    }

    [Fact]
    public void ReadingAMap_RollsOnceWithTrackingsOdds_AndFindsOnlyInItsSheetOrArea()
    {
        var (area, sheet, features) = Richest();
        Assert.True(features.Count >= 2, $"the richest area has only {features.Count} features");
        var (sim, drive, you, map) = Reader(tracking: 3);
        var reads = new List<(Item Map, FoundFeature? Found)>();
        sim.ParkMapRead += (_, m, f) => reads.Add((m, f));
        int finds = 0;
        for (int i = 0; i < 12; i++)
        {
            int inSheetBefore = features.Count(f => f.Sheet == sheet && !map.Knows(f.Kind, f.Id));
            int unknownBefore = features.Count(f => !map.Knows(f.Kind, f.Id));
            Item m = GiveMap(sim, drive, area, sheet);
            Do(sim, drive, new UseItemAction(m.Id));
            Do(sim, drive, new DropAction(m.Id)); // pockets hold only so many maps
            var (read, found) = reads[^1];
            Assert.Same(m, read);
            bool hit = ParkMaps.ReadRoll(sim.Rng.Seed, m.Id, you.Id) < ParkMaps.FindPercent(3);
            if (!hit || unknownBefore == 0)
            {
                Assert.Null(found);
                continue;
            }
            Assert.NotNull(found);
            FoundFeature f = found!.Value;
            finds++;
            Assert.Equal(FoundHow.FromMap, f.How);
            var spot = features.Single(x => x.Kind == f.Kind && x.Id == f.Id);
            if (inSheetBefore > 0)
                Assert.Equal(sheet, spot.Sheet); // the map's own sheet first
            Assert.Equal(f, map.Feature(f.Kind, f.Id));
        }
        Assert.True(finds > 0, "twelve reads at 70% found nothing");
        // reading a map again rolls nothing
        int before = reads.Count;
        Do(sim, drive, new PickUpAction(reads[0].Map.Id));
        Do(sim, drive, new UseItemAction(reads[0].Map.Id));
        Assert.Contains(reads[0].Map, you.Inventory.All);
        Assert.Equal(before, reads.Count);
        Assert.Equal(12, map.MapsRead.Count);
    }

    [Fact]
    public void ReadingAMap_GivesTheSameFindsForTheSameRun()
    {
        string Run()
        {
            var (area, sheet, _) = Richest();
            var (sim, drive, _, map) = Reader(tracking: 1);
            for (int i = 0; i < 8; i++)
            {
                Item m = GiveMap(sim, drive, area, sheet);
                Do(sim, drive, new UseItemAction(m.Id));
                Do(sim, drive, new DropAction(m.Id));
            }
            return string.Join(";", map.Features.Select(f => $"{f.Kind}{f.Id}{f.How}{f.Tick}")) + " " + sim.ComputeStateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
