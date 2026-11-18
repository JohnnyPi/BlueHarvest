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
        ["Default"] = new(57835, 0x7059E17911153E8AUL, 0xB3D5C050312106C1UL, "6910,3029,4308,586,4196,2559,205,4274,3292,1042,1184,5056,4024,5240,7503,436,916,1476,1018,275,306", "8,9,9,3,5,4,5,7,8,2,7,5,5,1,3,4,2,7,6,0,7", "8135024976 9315264780 9716438520 3204679158 5394687021 4761802539 5741630298 7904853162 8516493720 2973061458 7904356218 5682734901 5380124697 1342879056 3429786105 4251073689 2715094863 7492538160 6183279054 0537641982 7230946815"),
        ["Seed12"] = new(52175, 0xA85E10593157EABCUL, 0xE6E615B32213F585UL, "1410,5781,7123,1623,3028,6120,368,935,3017,1121,3929,1340,3764,4505,2218,1797,917,1169,435,1186,389", "9,6,8,2,2,0,1,2,8,6,3,2,9,6,9,3,5,5,0,0,0", "9612870354 6918053742 8316495270 2630574891 2307584169 0347856291 1853927604 2085791643 8326014759 6834209715 3419870265 2387410596 9814527036 6187345902 9627345180 3490582716 5362480179 5620374819 0136284957 0531796428 0175243869"),
        ["SmallNoCliffs"] = new(25206, 0xB246C7FCDF31CB74UL, 0x1131FE9747E5A62FUL, "2457,1526,1348,379,3796,968,117,2255,1946,929,1102,1284,1092,1724,1276,404,829,904,388,258,224", "8,8,2,7,8,1,6,9,7,6,4,5,5,0,8,5,4,1,9,5,3", "8236457190 8049156327 2143689075 7243851960 8549061372 1034986527 6549708321 9748310562 7294136805 6057429183 4158967203 5763190248 5924138670 0327691548 8635490271 5760841932 4176083592 1429057836 9148506327 5723160498 3701269485"),
        ["CoolWet"] = new(38847, 0xE440642D6B25017EUL, 0xDC436C696A527E3FUL, "3347,654,4191,427,4724,3597,341,3083,2470,855,2752,499,1113,1473,1203,952,480,3295,1248,500,1643", "8,8,9,6,4,3,2,3,9,3,2,5,3,7,8,2,1,3,8,2,1", "8607415329 8194620537 9813570426 6045127839 4106278593 3589064271 2697105834 3962148507 9523018674 3256709814 2039451768 5893061724 3047195682 7918230456 8573206491 2793485160 1428965703 3854276019 8359201674 2368054791 1837024965"),
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
    public void EveryFootprint_IsItsFacilitys_AndTheRaptorPenHoldsItsRadius(string island)
    {
        IslandWorld world = TestIslands.World(island);
        FacilityAreas areas = world.Areas();
        IslandShape s = world.Shape;
        IReadOnlyList<Site> sites = world.Works.Sites;
        int pen = Enumerable.Range(0, sites.Count).Single(a => sites[a].Kind == "raptors");
        double r = FacilityAreas.MinRadiusM["raptors"];
        int held = 0;
        for (int i = 0; i < s.N; i++)
        {
            if (s.Land[i] == 0)
                continue;
            double x = s.CellX(i), y = s.CellY(i);
            int owner = -1;
            double best = double.PositiveInfinity;
            for (int a = 0; a < sites.Count; a++)
            {
                double dx = sites[a].X - x, dy = sites[a].Y - y;
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= sites[a].R && dx * dx + dy * dy < best) { owner = a; best = dx * dx + dy * dy; }
            }
            if (owner >= 0)
            {
                Assert.True(areas.AreaOf(i) == owner, $"cell {i} in {sites[owner].Name}'s footprint belongs to {sites[areas.AreaOf(i)].Name}");
                continue;
            }
            double px = sites[pen].X - x, py = sites[pen].Y - y;
            if (px * px + py * py <= r * r)
            {
                Assert.Equal(pen, areas.AreaOf(i));
                held++;
            }
        }
        Assert.True(held >= 100, $"the pen holds only {held} cells within {r} m");
        Assert.False(sites[pen].Major); // the generator's kind, and the area's: minor, times doubled outside its radius
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void AnAreasTenSheets_CoverItExactlyOnce_InEqualShares(string island)
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
        "Lab offices", "Director's office", "Briefing room", "Customs", "Loading office", "Check-in", "Departure lounge",
        "Main control room", "Workshop", "Radar room", "Laboratory", "Wet lab", "Dispatch", "Instrument room", "Bunk room", "Lookout",
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
        // common: twenty or more on every golden island
        Assert.True(maps.Count >= 20, $"only {maps.Count} park maps");
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
