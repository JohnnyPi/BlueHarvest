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
        ["Default"] = new(102537, 0xEEB55BFA1E39C576UL, 0xC38DE77EBCF718BBUL, "5932,530,1429,5656,1724,3988,9082,5710,5500,10695,567,14550,6966,3888,11617,460,430,803,3348,437,393,584,1072,4276,2900", "8,4,5,0,5,8,0,1,5,0,2,5,7,8,5,3,0,5,9,7,5,4,8,9,5", "8135024976 4315267980 5716493820 0246791358 5394687021 8761402539 0741632598 1904785362 5816493720 0972361458 2904375618 5682734901 7380124569 8314279056 5429783610 3245107689 0715948632 5492381760 9183270564 7053641982 5230794681 4062318579 8053674219 9468273501 5806712943"),
        ["Seed12"] = new(52175, 0x06E579A10DC165ACUL, 0x5E994B0DBB1C9371UL, "2871,254,4811,3897,667,1608,4149,5347,2487,2616,299,8430,2795,4534,2830,519,382,400,674,339,218,322,342,251,257,204,288,384", "3,9,8,9,2,9,0,5,1,3,6,4,8,6,9,0,0,1,9,1,7,6,3,2,2,4,0,3", "3961287054 9168053742 8316495270 9632057481 2307584169 9347856201 0853927164 5087916432 1832604759 3842069715 6419870235 4238710596 8145297036 6187345902 9627345180 0493582716 0362548179 1620374859 9136284057 1053796428 7152438069 6529780314 3057941268 2568914730 2796108345 4379801265 0349218765 3529408671"),
        ["SmallNoCliffs"] = new(25206, 0x8B5A7DD72253E89CUL, 0x782AFEEDFC16722AUL, "1316,1370,819,1612,120,679,1772,3858,1758,636,192,2972,1564,1834,1264,474,257,224,632,122,107,86,140,18,128,448,400,404", "6,6,7,3,6,1,0,7,6,3,4,3,7,1,8,9,5,3,9,3,3,4,2,8,3,9,3,4", "6283457190 6084915327 7143628905 3248519607 6548901372 1034986527 0564978321 7483910562 6294137805 3057429618 4158967203 3576190248 7924513860 1327695408 8635490271 9760584132 5176408392 3429057816 9148506327 3721650498 3701269485 4632980157 2639571048 8194025763 3429806751 9430728156 3598621407 4021897536"),
        ["CoolWet"] = new(38847, 0x11286CF67746710EUL, 0x1125FBDCDAF531B3UL, "3448,741,672,1156,252,2490,738,1822,3729,4770,313,7789,2833,802,768,189,508,225,2133,470,776,457,264,986,208,308", "2,9,5,9,2,9,0,3,4,2,3,5,9,5,4,0,2,7,9,4,5,4,1,9,2,8", "2608741539 9146205837 5981370426 9045126783 2106478593 9580642371 0697152834 3962148507 4523901867 2567093814 3094517682 5893061724 9304715682 5971823046 4573208691 0793485162 2489657103 7385426019 9352016874 4326805791 5831702496 4032617859 1974826350 9183076524 2064831759 8940156723"),
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
        "Elevator lobby", "Cabin hall", "Recording booths", "Chart room", "Potting room", "Controller room",
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
            Assert.True(ParkMaps.TryParse(map.Tag, world.Works, out int area, out int sheet), $"park map {map.Id} has tag {map.Tag ?? "none"}");
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
    public void ParkMapTags_RoundTrip_ByTheSitesStableKey()
    {
        Assert.Equal("area=site.watch:2;sheet=3", ParkMaps.Tag("site.watch:2", 3));
        Assert.True(ParkMaps.TryParse(ParkMaps.Tag("site.watch:2", 3), out string key, out int sheet));
        Assert.Equal(("site.watch:2", 3), (key, sheet));
        Assert.False(ParkMaps.TryParse(null, out string _, out _));
        Assert.False(ParkMaps.TryParse("Genetics complex", out string _, out _));
        Assert.False(ParkMaps.TryParse("area=12", out string _, out _));
        Assert.False(ParkMaps.TryParse("area=;sheet=1", out string _, out _));
        // on an island the key finds its site (A3: not the site's place in the list), and a key the island lacks doesn't
        IslandWorks works = TestIslands.Works("SmallNoCliffs");
        Site last = works.Sites[^1];
        Assert.True(ParkMaps.TryParse(ParkMaps.Tag(last, 4), works, out int area, out int s2));
        Assert.Equal((last.Id, 4), (area, s2));
        Assert.False(ParkMaps.TryParse(ParkMaps.Tag("site.nuclear:0", 1), works, out _, out _));
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
        Item map = sim.CreateItem(ItemKind.ParkMap, tag: ParkMaps.Tag(TestIslands.Works(Island).Sites[area], sheet));
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
