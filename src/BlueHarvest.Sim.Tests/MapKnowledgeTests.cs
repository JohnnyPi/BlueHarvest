using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;
using Xunit.Abstractions;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Step 25b-1: map knowledge. The island-scale view rule (MapSight) against the island lab
/// (tools/island-lab/golden-knowledge.js); MapKnowledge's bookkeeping and merging; and the
/// simulation keeping a survivor's map as they walk.
/// </summary>
public class MapKnowledgeTests
{
    private readonly ITestOutputHelper _output;

    public MapKnowledgeTests(ITestOutputHelper output) => _output = output;

    /// <param name="Seen">Cells seen after both walks and the two cave visits.</param>
    /// <param name="SeenHash">The seen cells, in order.</param>
    /// <param name="Ruins">Ruins found.</param>
    /// <param name="Wrecks">Wrecks found.</param>
    /// <param name="Caves">Caves found.</param>
    /// <param name="FeaturesHash">Every feature found (kind, id, how, tick), by kind and id.</param>
    /// <param name="Trail">Steps in the trail.</param>
    /// <param name="TrailHash">The trail (cell, tick).</param>
    /// <param name="Sites">Sites entered, by index.</param>
    /// <param name="FirstCaveHow">How the first cave was found from 20 m off its first mouth (0 seen).</param>
    /// <param name="SecondCaveHow">How the second cave was found from standing in its first mouth (1 entered).</param>
    public sealed record Expected(int Seen, ulong SeenHash, int Ruins, int Wrecks, int Caves, ulong FeaturesHash,
        int Trail, ulong TrailHash, string Sites, int FirstCaveHow, int SecondCaveHow);

    // node tools/island-lab/golden-knowledge.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(1556, 0x3FC6133E5C58F805UL, 0, 0, 2, 0x95252A935B677E67UL, 493, 0x6152CD8C4EB61DD4UL, "0,1,9,18", 0, 1),
        ["Seed12"] = new(1272, 0xC2623A9F398547F7UL, 0, 3, 2, 0xFE3143CBB17638FEUL, 384, 0x110C9E9EF67548BDUL, "0,1,9", 0, 1),
        ["SmallNoCliffs"] = new(556, 0xD326B646EFD791CEUL, 0, 0, 2, 0x585AA34051E0E4AFUL, 176, 0xEC4CD771B5DFD696UL, "0,1,9,10", 0, 1),
        ["CoolWet"] = new(722, 0x1438895CA08C3AD9UL, 0, 0, 2, 0x7ABD607DC9D70F8BUL, 227, 0x073201F5922A5BBDUL, "0,1,9", 0, 1),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    private static readonly Dictionary<string, Lazy<FeatureIndex>> Features =
        TestIslands.Cases.Keys.ToDictionary(k => k, k => new Lazy<FeatureIndex>(() => FeatureIndex.Build(TestIslands.World(k))));

    private static MapKnowledge Empty(IslandWorld world) => new(world.Shape.Nx, world.Shape.Ny);

    /// <summary>Looks from a spot as the simulation would: the range for it, then everything seen from it.</summary>
    private static (List<FoundFeature> Found, List<int> Sites) Look(MapKnowledge k, IslandWorld world, FeatureIndex f, double x, double y, bool day, long tick) =>
        MapSight.See(k, world, f, x, y, MapSight.Range(world, x, y, day, indoors: false), tick);

    /// <summary>The lab's scripted walk: straight from a to b in 8 m steps, looking at each, on tiles' middles.</summary>
    private static long Walk(MapKnowledge k, IslandWorld world, FeatureIndex f, Site a, Site b, bool day, long tick0)
    {
        double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        int steps = Math.Max(1, (int)Math.Ceiling(len / 8));
        for (int s = 0; s <= steps; s++)
        {
            double x = Math.Floor(a.X + (b.X - a.X) * s / steps) + 0.5, y = Math.Floor(a.Y + (b.Y - a.Y) * s / steps) + 0.5;
            Look(k, world, f, x, y, day, tick0 + s * 100);
        }
        return tick0 + (steps + 1) * 100;
    }

    // ---------- the view rule, against the island lab ----------

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ScriptedWalks_MatchTheIslandLab(string island)
    {
        IslandWorld world = TestIslands.World(island);
        FeatureIndex f = Features[island].Value;
        IslandWorks w = world.Works;
        Site docks = w.Sites.First(t => t.Kind == "docks"), court = w.Sites.First(t => t.Kind == "court"), genetics = w.Sites.First(t => t.Kind == "genetics");
        MapKnowledge k = Empty(world);
        long tick = Walk(k, world, f, docks, court, day: true, 0);
        tick = Walk(k, world, f, court, genetics, day: false, tick);
        Cave c0 = w.Caves[0], c1 = w.Caves[1];
        double fx = Math.Floor(c0.Mouths[0].X + 20) + 0.5, fy = Math.Floor(c0.Mouths[0].Y) + 0.5;
        Look(k, world, f, fx, fy, day: true, tick);
        Look(k, world, f, c1.Mouths[0].X, c1.Mouths[0].Y, day: true, tick + 100);

        Expected e = Golden[island];
        Assert.Equal(e.Seen, k.SeenCount);
        var hs = new GenHasher().I(k.SeenCount);
        foreach (int c in k.SeenCells())
            hs.I(c);
        Assert.Equal(e.SeenHash, hs.Value);
        Assert.Equal(e.Ruins, k.Features.Count(x => x.Kind == FeatureKind.Ruin));
        Assert.Equal(e.Wrecks, k.Features.Count(x => x.Kind == FeatureKind.Wreck));
        Assert.Equal(e.Caves, k.Features.Count(x => x.Kind == FeatureKind.Cave));
        var hf = new GenHasher().I(k.Features.Count);
        foreach (FoundFeature x in k.Features)
            hf.I((int)x.Kind).I(x.Id).I((int)x.How).I(x.Tick);
        Assert.Equal(e.FeaturesHash, hf.Value);
        Assert.Equal(e.Trail, k.Trail.Count);
        var ht = new GenHasher().I(k.Trail.Count);
        foreach (TrailStep s in k.Trail)
            ht.I(s.Cell).I(s.Tick);
        Assert.Equal(e.TrailHash, ht.Value);
        Assert.Equal(e.Sites, string.Join(",", k.Sites));
        Assert.Equal((FoundHow)e.FirstCaveHow, k.Feature(FeatureKind.Cave, c0.Id)?.How);
        Assert.Equal((FoundHow)e.SecondCaveHow, k.Feature(FeatureKind.Cave, c1.Id)?.How);
    }

    [Fact]
    public void Range_Is250InDaylight_60AtNightIndoorsOrInJungle_AndHalfAgainFromHighGround()
    {
        IslandWorld world = TestIslands.World("Default");
        IslandShape s = world.Shape;
        double Above(int i)
        {
            int cx = i % s.Nx, cy = i / s.Nx;
            double sum = 0;
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && cx + dx >= 0 && cy + dy >= 0 && cx + dx < s.Nx && cy + dy < s.Ny)
                    {
                        sum += (float)s.Height[(cy + dy) * s.Nx + cx + dx];
                        n++;
                    }
            return (float)s.Height[i] - sum / n;
        }
        bool Dense(int i) => (Biome)world.Climate.Biomes[i] is Biome.Moist or Biome.Wet or Biome.Cloud;
        bool Land(int i) => s.Land[i] != 0;
        int open = Enumerable.Range(0, s.N).First(i => Land(i) && !Dense(i) && Above(i) < 10);
        int dense = Enumerable.Range(0, s.N).First(i => Land(i) && Dense(i) && Above(i) < 10);
        int high = Enumerable.Range(0, s.N).First(i => Land(i) && !Dense(i) && Above(i) >= 25);

        Assert.Equal(250, MapSight.Range(world, s.CellX(open), s.CellY(open), daylight: true, indoors: false));
        Assert.Equal(60, MapSight.Range(world, s.CellX(open), s.CellY(open), daylight: false, indoors: false));
        Assert.Equal(60, MapSight.Range(world, s.CellX(open), s.CellY(open), daylight: true, indoors: true));
        Assert.Equal(60, MapSight.Range(world, s.CellX(dense), s.CellY(dense), daylight: true, indoors: false));
        Assert.Equal(375, MapSight.Range(world, s.CellX(high), s.CellY(high), daylight: true, indoors: false));
        Assert.Equal(90, MapSight.Range(world, s.CellX(high), s.CellY(high), daylight: false, indoors: false));
    }

    [Fact]
    public void TheCellYouStandIn_IsAlwaysSeen_AndARangeReachesCellsByTheirNearestEdge()
    {
        IslandWorld world = TestIslands.World("Default");
        double cell = world.Shape.Cell;
        FeatureIndex f = Features["Default"].Value;
        int cx = world.Shape.Nx / 2, cy = world.Shape.Ny / 2;
        MapKnowledge k = Empty(world);
        // 10 m inside the west edge of a cell, seeing 20 m: that cell and its west neighbour only
        MapSight.See(k, world, f, cx * cell + 10, cy * cell + cell / 2, 20, 0);
        Assert.Equal(2, k.SeenCount);
        Assert.True(k.HasSeen(cx, cy));
        Assert.True(k.HasSeen(cx - 1, cy));
        // seeing nothing still sees where you stand
        MapKnowledge k2 = Empty(world);
        MapSight.See(k2, world, f, (cx + 5) * cell + cell / 2, cy * cell + cell / 2, 0, 0);
        Assert.Equal(1, k2.SeenCount);
        Assert.True(k2.HasSeen(cx + 5, cy));
    }

    // ---------- features ----------

    [Fact]
    public void ARuinAndAWreck_AreFoundWhenTheirCellIsSeen()
    {
        IslandWorld world = TestIslands.World("Default");
        FeatureIndex f = Features["Default"].Value;
        Ruin ruin = world.Works.Ruins[0];
        Wreck wreck = world.Works.Wrecks[0];
        MapKnowledge k = Empty(world);
        var (found, _) = MapSight.See(k, world, f, ruin.X, ruin.Y, 0, 50);
        Assert.Contains(new FoundFeature(FeatureKind.Ruin, ruin.Id, FoundHow.Seen, 50), found);
        Assert.False(k.Knows(FeatureKind.Wreck, wreck.Id));
        MapSight.See(k, world, f, wreck.X, wreck.Y, 0, 60);
        Assert.Equal(FoundHow.Seen, k.Feature(FeatureKind.Wreck, wreck.Id)?.How);
        // looking again finds nothing new
        var (again, _) = MapSight.See(k, world, f, ruin.X, ruin.Y, 0, 70);
        Assert.Empty(again);
        Assert.Equal(50, k.Feature(FeatureKind.Ruin, ruin.Id)?.Tick);
    }

    [Fact]
    public void ACave_IsFoundOnlyWithin30mOfAnOpening_AndEnteredFromItsMouth()
    {
        IslandWorld world = TestIslands.World("Default");
        FeatureIndex f = Features["Default"].Value;
        Cave cave = world.Works.Caves.First(c => c.Mouths.Count > 0);
        (double mx, double my) = cave.Mouths[0];
        // pick a direction from the mouth with no other opening (of any cave, this one's included) within 40 m of the far spot
        (double X, double Y) dir = new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) }
            .First(d => f.OpeningsNear(mx + d.Item1 * 31, my + d.Item2 * 31, 40).All(o => o.CaveId == cave.Id && o.X == mx && o.Y == my && !o.Skylight));
        MapKnowledge far = Empty(world);
        MapSight.See(far, world, f, mx + dir.X * 31, my + dir.Y * 31, 0, 0);
        Assert.False(far.Knows(FeatureKind.Cave, cave.Id), "31 m off the mouth is too far");
        MapKnowledge near = Empty(world);
        MapSight.See(near, world, f, mx + dir.X * 29, my + dir.Y * 29, 0, 0);
        Assert.Equal(FoundHow.Seen, near.Feature(FeatureKind.Cave, cave.Id)?.How);
        MapKnowledge inside = Empty(world);
        MapSight.See(inside, world, f, mx + dir.X * 3, my + dir.Y * 3, 0, 0);
        Assert.Equal(FoundHow.Entered, inside.Feature(FeatureKind.Cave, cave.Id)?.How);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void TheFeatureIndex_HoldsEveryRuinWreckAndOpening(string island)
    {
        IslandWorld world = TestIslands.World(island);
        FeatureIndex f = Features[island].Value;
        IslandWorks w = world.Works;
        foreach (Ruin r in w.Ruins)
            Assert.Contains(r.Id, f.RuinsIn(f.CellAt(r.X, r.Y)));
        foreach (Wreck r in w.Wrecks)
            Assert.Contains(r.Id, f.WrecksIn(f.CellAt(r.X, r.Y)));
        foreach (Cave c in w.Caves)
        {
            foreach ((double x, double y) in c.Mouths)
                Assert.Contains(f.OpeningsNear(x, y, 0.01), o => o.CaveId == c.Id && !o.Skylight);
            foreach ((double x, double y) in c.Skylights)
                Assert.Contains(f.OpeningsNear(x, y, 0.01), o => o.CaveId == c.Id && o.Skylight);
        }
        Assert.Equal(w.Ruins.Count, Enumerable.Range(0, world.Shape.N).Sum(i => f.RuinsIn(i).Count));
        Assert.Equal(w.Wrecks.Count, Enumerable.Range(0, world.Shape.N).Sum(i => f.WrecksIn(i).Count));
    }

    // ---------- MapKnowledge ----------

    [Fact]
    public void TheTrail_ListsACellAgainOnlyAfterLeavingIt()
    {
        var k = new MapKnowledge(10, 10);
        Assert.True(k.Walk(5, 0));
        Assert.False(k.Walk(5, 10));
        Assert.True(k.Walk(6, 20));
        Assert.True(k.Walk(5, 30));
        Assert.Equal(new[] { 5, 6, 5 }, k.Trail.Select(s => s.Cell).ToArray());
        Assert.Equal(0, k.Trail[0].Tick);
    }

    private static MapKnowledge Sample(int nx, int seenFrom, int seenTo, int trailFrom, long tick)
    {
        var k = new MapKnowledge(nx, nx);
        for (int c = seenFrom; c <= seenTo; c++)
            k.See(c);
        for (int c = trailFrom; c < trailFrom + 3; c++)
            k.Walk(c, tick + c);
        return k;
    }

    [Fact]
    public void Merge_TakesTheUnion_KeepsTheOlderTrailApart_AndMergingTwiceChangesNothing()
    {
        MapKnowledge mine = Sample(10, 0, 9, 0, 1000);
        mine.Find(new FoundFeature(FeatureKind.Ruin, 1, FoundHow.Seen, 1000));
        mine.EnterSite(2);
        mine.Note(new MapNote(MapNoteKind.Note, 10, 20, "Good water here", 1000));
        MapKnowledge theirs = Sample(10, 5, 14, 20, 0);
        theirs.Find(new FoundFeature(FeatureKind.Ruin, 1, FoundHow.Entered, 5));
        theirs.Find(new FoundFeature(FeatureKind.Cave, 3, FoundHow.Entered, 7));
        theirs.Chart(4, 2);
        theirs.EnterSite(7);
        theirs.EnterRoom(11);
        theirs.Note(new MapNote(MapNoteKind.Echo, 30, 40, "You died here", 9));

        mine.Merge(theirs);
        Assert.Equal(15, mine.SeenCount);
        Assert.True(Enumerable.Range(0, 15).All(mine.HasSeen));
        Assert.Equal(FoundHow.Seen, mine.Feature(FeatureKind.Ruin, 1)?.How); // what this survivor found stays theirs
        Assert.Equal(FoundHow.Entered, mine.Feature(FeatureKind.Cave, 3)?.How);
        Assert.True(mine.HasCharted(4, 2));
        Assert.Equal(new[] { 2, 7 }, mine.Sites.ToArray());
        Assert.Equal(new[] { 11 }, mine.Rooms.ToArray());
        Assert.Equal(2, mine.Notes.Count);
        Assert.Equal(new[] { 0, 1, 2 }, mine.Trail.Select(s => s.Cell).ToArray()); // their trail isn't joined to mine
        IReadOnlyList<TrailStep> older = Assert.Single(mine.OlderTrails);
        Assert.Equal(new[] { 20, 21, 22 }, older.Select(s => s.Cell).ToArray());

        ulong Hash(MapKnowledge k)
        {
            var h = new StateHasher();
            k.AddTo(h);
            return h.Value;
        }
        ulong once = Hash(mine);
        mine.Merge(theirs);
        mine.Merge(mine);
        Assert.Equal(once, Hash(mine));
        Assert.Single(mine.OlderTrails);
        Assert.Throws<ArgumentException>(() => mine.Merge(new MapKnowledge(11, 10)));
    }

    [Fact]
    public void Merge_CarriesTrailsMergedIntoTheOtherMap()
    {
        MapKnowledge first = Sample(10, 0, 0, 30, 0), second = Sample(10, 1, 1, 40, 0), third = Sample(10, 2, 2, 50, 0);
        second.Merge(first); // the second survivor had recovered the first's map
        third.Merge(second);
        Assert.Equal(2, third.OlderTrails.Count);
        Assert.Equal(30, third.OlderTrails[0][0].Cell);
        Assert.Equal(40, third.OlderTrails[1][0].Cell);
        Assert.Equal(3, third.SeenCount);
    }

    // ---------- in the simulation ----------

    [Fact]
    public void Off_ByDefault_AndWhenOff_TheStateHashIsUntouched()
    {
        ulong Run(bool know)
        {
            var (sim, world, index, site) = RoomIndexTests.AtGenetics();
            var (_, outside, way) = RoomIndexTests.DoorIn(sim, world, index, site);
            Actor walker = sim.AddActor("Walker", 'w', outside, Species.Human,
                new ScriptedController(new ActorAction[] { new MoveAction(way), new MoveAction(way), new MoveAction(way) }));
            if (!know)
                Assert.Null(sim.Knowledge(walker));
            else
                sim.UseMapKnowledge(walker, world);
            sim.RunUntil(sim.CurrentTick + 20 * Ticks.PerSecond);
            return sim.ComputeStateHash();
        }
        ulong off = Run(false), offAgain = Run(false), on = Run(true), onAgain = Run(true);
        Assert.Equal(off, offAgain);
        Assert.Equal(on, onAgain);
        Assert.NotEqual(off, on); // in the hash once on
    }

    [Fact]
    public void Walking_FillsInTheMap_TheSiteAndTheRoomsEntered()
    {
        var (sim, world, index, site) = RoomIndexTests.AtGenetics();
        sim.SetTimeOfDay(12);
        var (room, outside, way) = RoomIndexTests.DoorIn(sim, world, index, site);
        Actor walker = sim.AddActor("Walker", 'w', outside, Species.Human,
            new ScriptedController(new ActorAction[] { new MoveAction(way), new MoveAction(way), new MoveAction(way) }));
        var sites = new List<int>();
        sim.SiteFound += (a, s) => sites.Add(s);
        MapKnowledge k = sim.UseMapKnowledge(walker, world);
        Assert.Same(k, sim.Knowledge(walker));
        Assert.True(k.SeenCount >= 1);
        Assert.Single(k.Trail);
        Assert.Contains(site, k.Sites); // standing in the genetics complex
        var (ix, iy) = sim.ToIsland(outside);
        Assert.True(k.HasSeen(Features["SmallNoCliffs"].Value.CellAt(ix + 0.5, iy + 0.5))); // AtGenetics is on SmallNoCliffs

        sim.RunUntil(sim.CurrentTick + 20 * Ticks.PerSecond);
        Assert.Contains(room.Id, k.Rooms);
        Assert.Equal(new[] { site }, sites); // found once, when knowledge began; walking about inside adds nothing
    }

    /// <summary>One position of a scripted walk: where the walker stood after an action, and the clock either side of it.</summary>
    private readonly record struct WalkStep(TilePos At, long Before, long After);

    /// <summary>
    /// A survivor walks 350 m south from the Default island's Court of Unfinished Measures gate (the old control
    /// centre's walk), across open grassland and dry scrub,
    /// with map knowledge on. Every move is recorded. Returns what the simulation knows at the end and the
    /// positions walked (the first is the start, before any move).
    /// </summary>
    private (MapKnowledge Known, List<WalkStep> Steps, Simulation Sim) WalkFromTheCourt(int hour)
    {
        IslandWorld world = TestIslands.World("Default");
        Site control = world.Works.Sites.First(t => t.Kind == "court");
        Assert.Equal('s', control.Gate); // the walk goes out of the gate, away from the buildings
        double gateY = control.Y + control.R;
        var window = new IslandWindow(world, TestIslands.Chunker("Default"), control.X, gateY + 175, 9);
        var sim = new Simulation(TestContent.NewSession(), window.Map, seed: 5);
        sim.SetTimeOfDay(hour);
        sim.UseRooms(RoomIndex.Build(world));
        var drive = new PlayerController();
        TilePos start = window.NearestStandable(window.ToLocal(control.X, gateY + 6));
        Actor walker = sim.AddActor("Walker", 'w', start, Species.Human, drive, Direction.South);
        long t0 = sim.CurrentTick;
        MapKnowledge k = sim.UseMapKnowledge(walker, world, Features["Default"].Value);
        sim.RunUntilInputNeeded();
        var steps = new List<WalkStep> { new(walker.Position, t0, sim.CurrentTick) };

        TilePos goal = window.NearestStandable(window.ToLocal(control.X, gateY + 356));
        IReadOnlyList<Direction>? path = Pathfinder.FindPath(sim, walker, goal, tolerance: 3, maxNodes: 300_000);
        Assert.NotNull(path);
        Assert.True(path.Count >= 300, $"a {path.Count}-move path");
        foreach (Direction move in path)
        {
            long before = sim.CurrentTick;
            drive.Pending = new MoveAction(move);
            sim.RunUntilInputNeeded();
            steps.Add(new WalkStep(walker.Position, before, sim.CurrentTick));
        }
        return (k, steps, sim);
    }

    /// <summary>
    /// The simulation's side of map knowledge: after every action the survivor looks from where they now
    /// stand, with the range for that spot and the time of day, and the map they end with is exactly what
    /// MapSight gives for the same spots looked at in the same order. (MapSight itself is checked against
    /// the island lab above.) By day on open ground they see far more than by night.
    /// </summary>
    [Fact]
    public void TheSimulationKeepsTheMap_JustAsMapSightDoes_ByDayAndByNight()
    {
        IslandWorld world = TestIslands.World("Default");
        FeatureIndex f = Features["Default"].Value;
        var seen = new Dictionary<int, int>();
        foreach (int hour in new[] { 12, 0 })
        {
            var (k, steps, sim) = WalkFromTheCourt(hour);
            bool day = hour == 12;
            Assert.Equal(day, sim.AmbientLightPercent >= Simulation.DaylightPercent);

            // the same looks, made by hand on an empty map
            var replay = Empty(world);
            var stepOfTrail = new List<int>();
            var rooms = new HashSet<int>();
            int roomWas = sim.RoomAt(steps[0].At)?.Id ?? -1;
            for (int i = 0; i < steps.Count; i++)
            {
                var (ix, iy) = sim.ToIsland(steps[i].At);
                double x = ix + 0.5, y = iy + 0.5;
                int trailWas = replay.Trail.Count;
                MapSight.See(replay, world, f, x, y, MapSight.Range(world, x, y, day, sim.Map.IsIndoor(steps[i].At)), steps[i].After);
                if (replay.Trail.Count > trailWas)
                    stepOfTrail.Add(i);
                int roomNow = sim.RoomAt(steps[i].At)?.Id ?? -1;
                if (i > 0 && roomNow != roomWas && roomNow >= 0)
                    rooms.Add(roomNow);
                roomWas = roomNow;
            }

            Assert.Equal(replay.SeenCells(), k.SeenCells());
            Assert.Equal(replay.Trail.Select(p => p.Cell), k.Trail.Select(p => p.Cell));
            Assert.Equal(replay.Features.Select(q => (q.Kind, q.Id, q.How)).OrderBy(q => q), k.Features.Select(q => (q.Kind, q.Id, q.How)).OrderBy(q => q));
            Assert.Equal(replay.Sites.OrderBy(n => n), k.Sites.OrderBy(n => n));
            Assert.Equal(rooms.OrderBy(n => n), k.Rooms.OrderBy(n => n));

            // each trail step is stamped during the move that first stood in its cell
            for (int t = 0; t < k.Trail.Count; t++)
            {
                WalkStep at = steps[stepOfTrail[t]];
                Assert.InRange(k.Trail[t].Tick, at.Before, at.After);
            }

            // the walk went somewhere, one cell at a time, and everywhere it went was seen
            Assert.True(k.Trail.Count >= 3, $"{k.Trail.Count} trail steps");
            for (int t = 1; t < k.Trail.Count; t++)
            {
                int a = k.Trail[t - 1].Cell, b = k.Trail[t].Cell;
                Assert.True(Math.Max(Math.Abs(a % k.Nx - b % k.Nx), Math.Abs(a / k.Nx - b / k.Nx)) <= 1, $"trail jumps from {a} to {b}");
            }
            Assert.All(k.Trail, p => Assert.True(k.HasSeen(p.Cell)));
            seen[hour] = k.SeenCount;
            _output.WriteLine($"{(day ? "noon" : "midnight")}: {steps.Count - 1} moves, {k.Trail.Count} trail steps, {k.SeenCount} cells seen");
        }
        Assert.True(seen[12] > seen[0], $"seen {seen[12]} cells by day, {seen[0]} by night");
    }
}
