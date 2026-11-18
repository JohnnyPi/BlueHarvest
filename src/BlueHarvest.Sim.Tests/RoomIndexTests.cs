using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// Step 25a-1: the room index. Every named room of every building, its place and its rules, checked
/// against the island lab (tools/island-lab/golden-rooms.js), against the power plan's doors, and in
/// the simulation (RoomAt, and the event when an actor walks into another room).
/// </summary>
public class RoomIndexTests
{
    /// <param name="Rooms">How many rooms the index holds.</param>
    /// <param name="Secure">Rooms behind security doors (keycard level 2 or 3).</param>
    /// <param name="Level3">Of those, the level 3 ones.</param>
    /// <param name="Motorised">Rooms with motorised doors.</param>
    /// <param name="RowsHash">Every row, as golden-rooms.js hashes them.</param>
    /// <param name="Places">Rooms per place, "place=count;…", in the order places first appear.</param>
    public sealed record Expected(int Rooms, int Secure, int Level3, int Motorised, ulong RowsHash, string Places);

    // node tools/island-lab/golden-rooms.js
    private static readonly Dictionary<string, Expected> Golden = new()
    {
        ["Default"] = new(203, 27, 8, 13, 0xD19E3CC56900645AUL,
            "Underground monorail station=8;Pali skyway station=4;Stegosaurus paddock=2;Triceratops paddock=2;Tyrannosaur paddock=2;Apatosaurus paddock=2;Cordon watch post, north-east=3;Cordon watch post, north=3;Cordon watch post, east=3;Ley-line survey station, north=3;Ley-line survey station, south=3;Ley-line survey station, south-east=3;Supply depot, north-west=5;Supply depot, north-east=5;Research lab, south-east=12;Research lab, north-west=12;Orgone research station=6;Weather control array=6;Geothermal plant=4;Nuclear plant=12;Velociraptor pen=10;Genetics complex=44;Control centre=19;Comms relay=4;Airstrip=8;Docks, south-east=9;Docks, south-west=9"),
        ["Seed12"] = new(203, 27, 8, 13, 0x9712A794BD3F8BF0UL,
            "Underground monorail station=8;Pali skyway station=4;Stegosaurus paddock=2;Triceratops paddock=2;Tyrannosaur paddock=2;Apatosaurus paddock=2;Cordon watch post, south-east=3;Cordon watch post, north-east=3;Cordon watch post, north-west=3;Ley-line survey station, west=3;Ley-line survey station, south=3;Ley-line survey station, east=3;Supply depot, south=5;Supply depot, east=5;Research lab, south=12;Research lab, south-east=12;Orgone research station=6;Weather control array=6;Geothermal plant=4;Nuclear plant=12;Velociraptor pen=10;Genetics complex=44;Control centre=19;Comms relay=4;Airstrip=8;Docks, west=9;Docks, south-east=9"),
        ["SmallNoCliffs"] = new(207, 27, 8, 13, 0x2615EB2541B14876UL,
            "Underground monorail station=8;Summit lift station=4;Pali skyway station=4;Stegosaurus paddock=2;Triceratops paddock=2;Tyrannosaur paddock=2;Apatosaurus paddock=2;Cordon watch post, east 2=3;Cordon watch post, east=3;Cordon watch post, south=3;Ley-line survey station, west=3;Ley-line survey station, north-west=3;Ley-line survey station, south-west=3;Supply depot, south-east=5;Supply depot, west=5;Research lab, south-east=12;Research lab, north-west=12;Orgone research station=6;Weather control array=6;Geothermal plant=4;Nuclear plant=12;Velociraptor pen=10;Genetics complex=44;Control centre=19;Comms relay=4;Airstrip=8;Docks, east=9;Docks, south-west=9"),
        ["CoolWet"] = new(203, 27, 8, 13, 0x4DEA960CF108C3CBUL,
            "Underground monorail station=8;Pali skyway station=4;Stegosaurus paddock=2;Triceratops paddock=2;Tyrannosaur paddock=2;Apatosaurus paddock=2;Cordon watch post, east 2=3;Cordon watch post, east=3;Cordon watch post, south=3;Ley-line survey station, west=3;Ley-line survey station, north=3;Ley-line survey station, east=3;Supply depot, north-west 2=5;Supply depot, north-west=5;Research lab, north-west 2=12;Research lab, north-west=12;Orgone research station=6;Weather control array=6;Geothermal plant=4;Nuclear plant=12;Velociraptor pen=10;Genetics complex=44;Control centre=19;Comms relay=4;Airstrip=8;Docks, north-west=9;Docks, south-east=9"),
    };

    public static IEnumerable<object[]> CaseNames => Golden.Keys.Select(k => new object[] { k });

    private static readonly Dictionary<string, Lazy<RoomIndex>> Indexes =
        TestIslands.Cases.Keys.ToDictionary(k => k, k => new Lazy<RoomIndex>(() => RoomIndex.Build(TestIslands.World(k))));

    private static RoomIndex Index(string island) => Indexes[island].Value;

    private static string PlaceCounts(RoomIndex index)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>();
        foreach (IndexedRoom r in index.Rooms)
        {
            if (!counts.ContainsKey(r.Place)) { order.Add(r.Place); counts[r.Place] = 0; }
            counts[r.Place]++;
        }
        return string.Join(";", order.Select(p => $"{p}={counts[p]}"));
    }

    // ---------- against the island lab ----------

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void RoomsPerPlace_MatchTheIslandLab(string island)
    {
        RoomIndex index = Index(island);
        Expected e = Golden[island];
        string got = PlaceCounts(index);
        if (got != e.Places)
        {
            Dictionary<string, string> Parse(string s) => s.Split(';').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
            Dictionary<string, string> want = Parse(e.Places), have = Parse(got);
            var differ = want.Keys.Union(have.Keys)
                .Where(p => !want.TryGetValue(p, out string? a) || !have.TryGetValue(p, out string? b) || a != b)
                .Select(p => $"{p}: lab {(want.TryGetValue(p, out string? a) ? a : "none")}, index {(have.TryGetValue(p, out string? b) ? b : "none")}");
            Assert.Fail($"Rooms per place differ from the island lab ({string.Join("; ", differ)}). If the counts agree, the places are in a different order.");
        }
        Assert.Equal(e.Rooms, index.Count);
        Assert.Equal(e.Secure, index.Rooms.Count(r => r.KeycardLevel > 0));
        Assert.Equal(e.Level3, index.Rooms.Count(r => r.KeycardLevel == 3));
        Assert.Equal(e.Motorised, index.Rooms.Count(r => r.Motorised));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryRow_MatchesTheIslandLab(string island)
    {
        RoomIndex index = Index(island);
        var h = new GenHasher().I(index.Count);
        foreach (IndexedRoom r in index.Rooms)
            h.I(r.Id).S(r.Name).S(r.Building).S(r.Place).I(r.SiteIndex).S(r.Kind)
                .I(r.X0).I(r.Y0).I(r.X1).I(r.Y1).I(r.KeycardLevel).I(r.Motorised ? 1 : 0).I(r.Label);
        Assert.Equal(Golden[island].RowsHash, h.Value);
    }

    // ---------- the index itself ----------

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryRoomOfEveryBuilding_IsIndexedOnce_InOrder(string island)
    {
        IslandWorks works = TestIslands.Works(island);
        RoomIndex index = Index(island);
        var labels = works.Prims.InOrder().Where(p => p.Kind == PrimKind.Building && p.Rooms is not null)
            .SelectMany(p => p.Rooms!).Select(r => r.Label).ToList();
        Assert.Equal(labels, index.Rooms.Select(r => r.Label).ToList());
        Assert.Equal(labels.Count, labels.Distinct().Count());
        for (int i = 0; i < index.Count; i++)
        {
            IndexedRoom r = index.Rooms[i];
            Assert.Equal(i, r.Id);
            Assert.Equal(works.Labels[r.Label].Split(" · ")[^1], r.Name);
            Assert.Equal(RoomRules.KeycardLevel(r.Name), r.KeycardLevel);
            if (r.SiteIndex >= 0)
            {
                Assert.Equal(works.Sites[r.SiteIndex].Name, r.Place);
                Assert.Equal(works.Sites[r.SiteIndex].Kind, r.Kind);
                Assert.Contains(r, index.OfSite(r.SiteIndex));
            }
            else
            {
                Assert.True(r.Kind is "paddock" or "transit", $"{r.Place} · {r.Name} has kind {r.Kind}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EachRoomsMiddle_LooksUpToItself(string island)
    {
        RoomIndex index = Index(island);
        foreach (IndexedRoom r in index.Rooms)
        {
            int cx = (r.X0 + r.X1) / 2, cy = (r.Y0 + r.Y1) / 2;
            Assert.True(index.At(cx, cy)?.Id == r.Id, $"the middle of {RoomIndex.Describe(r)} (room {r.Id}) looks up to {index.At(cx, cy)?.Id.ToString() ?? "nothing"}");
            Assert.Contains(r, index.In(cx - 1, cy - 1, cx + 1, cy + 1));
        }
        Assert.Null(index.At(-5, -5));
        Assert.Empty(index.In(-50, -50, -10, -10));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryPowerDoor_TouchesTheIndexedRoomItWasMadeFor(string island)
    {
        RoomIndex index = Index(island);
        PowerGrid grid = PowerPlan.Build(TestIslands.World(island), runSeed: 1);
        Assert.NotEmpty(grid.Doors);
        foreach (PowerDoor door in grid.Doors)
        {
            IReadOnlyList<IndexedRoom> touching = index.In(door.X, door.Y, door.X, door.Y);
            string what = $"the {door.Kind} door to {door.Room} at ({door.X}, {door.Y}); it touches {string.Join(", ", touching.Select(RoomIndex.Describe))}";
            if (door.Kind == DoorKind.SecurityLock)
                Assert.True(touching.Any(r => r.Name == door.Room && r.KeycardLevel == door.Level), what);
            else
                Assert.True(touching.Any(r => r.Name == door.Room && r.Motorised), what);
        }
        // and every security door was made for a secure room's door
        Assert.All(grid.Doors.Where(d => d.Kind == DoorKind.SecurityLock), d => Assert.True(RoomRules.SecureRooms.Contains(d.Room), d.Room));
    }

    [Fact]
    public void BuildingTwice_GivesTheSameRows()
    {
        IslandWorld world = TestIslands.World("SmallNoCliffs");
        RoomIndex a = RoomIndex.Build(world), b = RoomIndex.Build(world);
        Assert.Equal(a.Rooms, b.Rooms);
    }

    [Fact]
    public void Describe_NamesThePlaceAndTheRoom_NotTheBuilding()
    {
        RoomIndex index = Index("SmallNoCliffs");
        IndexedRoom hall = index.Rooms.First(r => r.Kind == "genetics" && r.Name == "Server hall");
        Assert.Equal("Server farm", hall.Building);
        Assert.Equal("Genetics complex · Server hall", RoomIndex.Describe(hall));
        Assert.Equal(2, hall.KeycardLevel);
        IndexedRoom vault = index.Rooms.First(r => r.Kind == "genetics" && r.Name == "Cryo vault");
        Assert.Equal(3, vault.KeycardLevel);
        IndexedRoom lift = index.Rooms.First(r => r.Kind == "transit" && r.Name == "Elevator");
        Assert.Equal("Underground monorail station · Elevator", RoomIndex.Describe(lift));
        Assert.StartsWith("Station at ", lift.Building);
        Assert.True(lift.Motorised);
        Assert.Equal(-1, lift.SiteIndex);
        IndexedRoom gallery = index.Rooms.First(r => r.Kind == "paddock");
        Assert.Equal("Viewing station", gallery.Building);
        Assert.Equal(0, gallery.KeycardLevel);
        Assert.True(index.Rooms.First(r => r.Name == "Airlock").IsPassage);
        Assert.False(hall.IsPassage);
    }

    // ---------- in the simulation ----------

    private const string Island = "SmallNoCliffs";

    private static (Simulation Sim, IslandWorld World, RoomIndex Index, int Site) AtGenetics(bool rooms = true)
    {
        IslandWorld world = TestIslands.World(Island);
        Site genetics = world.Works.Sites.First(t => t.Kind == "genetics");
        var window = new IslandWindow(world, TestIslands.Chunker(Island), genetics.X, genetics.Y, 6);
        var sim = new Simulation(window.Map, seed: 5);
        sim.SetTimeOfDay(12);
        sim.UsePower(PowerPlan.Build(world, runSeed: 1), window.OriginX, window.OriginY);
        RoomIndex index = Index(Island);
        if (rooms)
            sim.UseRooms(index);
        return (sim, world, index, world.Works.Sites.IndexOf(genetics));
    }

    [Fact]
    public void WithoutRooms_NothingIsFound()
    {
        var (sim, _, index, site) = AtGenetics(rooms: false);
        IndexedRoom hall = index.OfSite(site).First(r => r.Name == "Server hall");
        Assert.Null(sim.RoomAt(sim.FromIsland((hall.X0 + hall.X1) / 2, (hall.Y0 + hall.Y1) / 2)));
        Assert.Empty(sim.RoomsOf(site));
        Assert.Null(sim.Rooms);
    }

    [Fact]
    public void RoomAt_FindsTheSameRoom_AfterTheWindowShifts()
    {
        var (sim, _, index, site) = AtGenetics();
        Assert.Same(index, sim.Rooms);
        Assert.Equal(index.OfSite(site), sim.RoomsOf(site));
        IndexedRoom hall = index.OfSite(site).First(r => r.Name == "Server hall");
        int cx = (hall.X0 + hall.X1) / 2, cy = (hall.Y0 + hall.Y1) / 2;
        TilePos before = sim.FromIsland(cx, cy);
        Assert.True(sim.Map.InBounds(before));
        Assert.Same(hall, sim.RoomAt(before));

        sim.ShiftWindow(5, -3);
        TilePos after = sim.FromIsland(cx, cy);
        Assert.Equal(new TilePos(before.X - 5, before.Y + 3), after);
        Assert.Same(hall, sim.RoomAt(after));
    }

    /// <summary>
    /// An ordinary room of the facility with a door you can walk through from outside every room:
    /// the tile outside the door, and the way in.
    /// </summary>
    private static (IndexedRoom Room, TilePos Outside, Direction In) DoorIn(Simulation sim, IslandWorld world, RoomIndex index, int site)
    {
        foreach (Prim b in world.Works.Prims.InOrder())
        {
            if (b.Kind != PrimKind.Building || b.Doors is null)
                continue;
            foreach ((int x, int y) in b.Doors)
            {
                if (index.At(x, y) is not { } r || r.SiteIndex != site || r.KeycardLevel > 0 || r.Motorised)
                    continue;
                (int dx, int dy, Direction way) = y == r.Y1 ? (0, 1, Direction.North) : y == r.Y0 ? (0, -1, Direction.South)
                    : x == r.X0 ? (-1, 0, Direction.East) : x == r.X1 ? (1, 0, Direction.West) : (0, 0, Direction.North);
                if (dx == 0 && dy == 0)
                    continue;
                TilePos outside = sim.FromIsland(x + dx, y + dy), door = sim.FromIsland(x, y), inside = sim.FromIsland(x - dx, y - dy);
                if (!sim.Map.InBounds(outside) || !sim.Map.InBounds(inside) || !sim.IsClosedDoor(door))
                    continue;
                if (index.At(x + dx, y + dy) is not null || index.At(x - dx, y - dy)?.Id != r.Id)
                    continue;
                if (sim.Map.CoverAt(outside).BlocksMovement || sim.Map.CoverAt(inside).BlocksMovement || sim.PowerDoorAt(door) is not null)
                    continue;
                if (Math.Abs(sim.Map.Heights[outside] - sim.Map.Heights[door]) > 3 || Math.Abs(sim.Map.Heights[inside] - sim.Map.Heights[door]) > 3)
                    continue;
                return (r, outside, way);
            }
        }
        throw new InvalidOperationException("No ordinary door found into a room of the genetics complex.");
    }

    private static Direction Back(Direction d) => (Direction)(((int)d + 4) % 8);

    [Fact]
    public void WalkingIntoARoom_RaisesTheEventOnce_AndLeavingIt_Once()
    {
        var (sim, world, index, site) = AtGenetics();
        var (room, outside, way) = DoorIn(sim, world, index, site);
        Assert.Null(sim.RoomAt(outside));
        var script = new ActorAction[]
        {
            new MoveAction(way),       // opens the door
            new MoveAction(way),       // onto the door: in the room
            new MoveAction(way),       // inside
            new MoveAction(Back(way)), // back onto the door: still in the room
            new MoveAction(Back(way)), // out again
        };
        Actor walker = sim.AddActor("Walker", 'w', outside, Species.Human, new ScriptedController(script));
        var seen = new List<(Actor Who, IndexedRoom? Room)>();
        sim.ActorEnteredRoom += (a, r) => seen.Add((a, r));

        sim.RunUntil(sim.CurrentTick + 20 * Ticks.PerSecond);

        Assert.Equal(outside, walker.Position);
        Assert.Equal(2, seen.Count);
        Assert.Same(walker, seen[0].Who);
        Assert.Same(room, seen[0].Room);
        Assert.Null(seen[1].Room);
    }

    [Fact]
    public void Rooms_DontChangeTheStateHash()
    {
        ulong Run(bool rooms)
        {
            var (sim, world, index, site) = AtGenetics(rooms);
            var (_, outside, way) = DoorIn(sim, world, index, site);
            var script = new ActorAction[] { new MoveAction(way), new MoveAction(way), new MoveAction(way), new MoveAction(Back(way)) };
            sim.AddActor("Walker", 'w', outside, Species.Human, new ScriptedController(script));
            sim.RunUntil(sim.CurrentTick + 20 * Ticks.PerSecond);
            return sim.ComputeStateHash();
        }
        Assert.Equal(Run(rooms: false), Run(rooms: true));
    }
}
