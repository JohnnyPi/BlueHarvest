using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;

namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// The island's loot for one run, built before any actor exists (see Simulation.UseItems), without
/// changing a tile: each room the facility programmes name gets what such a room would hold, rolled
/// row by row from a table, on its free floor (inside the walls, off the furniture, clear of the
/// doors). Fixtures (lockers, desks, fridges, cabinets) stand against the walls and what's rolled for
/// "inside" goes into the last one placed in the room. Locks and keycards belong to their facility:
/// their tag is its name, and a card opens locks of its level and below. The monorail key lies in
/// its objective's room. The same world and run seed always give the same items, ids and all.
/// </summary>
public static class ItemPlan
{
    /// <summary>
    /// One row of the loot table: in rooms with these names (a name ending '*' matches as a prefix),
    /// at facilities of these kinds (any if null), this percent of the time, these items. A spec
    /// starting '>' goes inside the room's last container (on the floor if it has none).
    /// </summary>
    private sealed record Loot(string[] Rooms, int Percent, string Spec, string[]? Sites = null)
    {
        public bool Inside { get; } = Spec.StartsWith('>');

        public IReadOnlyList<ItemSpec> Items { get; } = ItemCatalog.ParseSpec(Spec.TrimStart('>'));

        public bool Matches(string room, string siteKind) =>
            (Sites is null || Sites.Contains(siteKind))
            && Rooms.Any(r => r.EndsWith('*') ? room.StartsWith(r[..^1], StringComparison.Ordinal) : r == room);
    }

    private static readonly string[] Offices =
        ["Offices", "Lab offices", "Office", "Shift office", "Loading office", "Customs", "Harbourmaster's office", "Operations office", "Keeper's office", "Director's office", "Briefing room"];
    private static readonly string[] Lobbies = ["Lobby", "Gatehouse", "Check-in", "Departure lounge", "Elevator lobby", "Cabin hall"];
    private static readonly string[] Security = ["Security office", "Security"];
    private static readonly string[] Armouries = ["Armoury"];
    private static readonly string[] Workshops = ["Workshop", "Parts store", "Vehicle bays", "Hangar floor", "Boat bays", "Store", "Cargo hall", "Cargo room"];
    private static readonly string[] Plant =
        ["Backup generators", "Generator", "Generator room", "Switch room", "Turbine hall", "Feedwater pumps", "Cooling-water pumps", "Steam separators", "Cooling plant"];
    private static readonly string[] Kitchens = ["Kitchen", "Galley"];
    private static readonly string[] Eating = ["Kitchen", "Galley", "Canteen", "Dining room", "Pantry", "Cold store"];
    private static readonly string[] Sleeping = ["Bunk room*", "Quarters", "Bunks", "Common room"];
    private static readonly string[] Washrooms = ["Washrooms", "Washroom", "Showers"];
    private static readonly string[] Clinics = ["Veterinary room", "Health physics", "Decontamination"];
    private static readonly string[] Labs =
        ["Sequencing lab", "Embryology lab", "Clean room", "Microscopy", "Fertilisation lab", "Wet lab", "Genetics lab", "Laboratory", "Specimen store"];
    private static readonly string[] DataRooms = ["Server hall", "Data centre", "Network operations"];
    private static readonly string[] Records = ["Records"];
    private static readonly string[] Control =
        ["Control room", "Main control room", "Radio room", "Tower cab", "Array control", "Radar room", "Observation room", "Instrument room", "Lookout", "Dispatch"];
    private static readonly string[] Lookouts = ["Lookout", "Tower cab", "Observation room"];
    private static readonly string[] Animals = ["Feed store", "Nursery pens", "Animal holding", "Incubation hall"];

    /// <summary>The loot table, first guesses to tune. Rows are rolled in order, room by room.</summary>
    private static readonly Loot[] Table =
    [
        // offices: a desk, and what people leave in desks
        new(Offices, 85, "desk"),
        new(Offices, 40, ">notebook"),
        new(Offices, 35, ">keycard level=1"),
        new(Offices, 15, ">security-log"),
        new(Offices, 15, ">painkillers"),
        new(Offices, 20, ">chocolate"),
        new(Offices, 15, ">flashlight"),
        new(["Director's office"], 50, ">keycard level=3"),
        new(["Director's office"], 60, ">research-file"),

        // front desks
        new(Lobbies, 50, "desk"),
        new(Lobbies, 60, ">keycard level=1"),
        new(Lobbies, 40, "fire-extinguisher"),

        // security: the duty desk, then the locked gun locker
        new(Security, 90, "desk"),
        new(Security, 60, ">keycard level=2"),
        new(Security, 70, ">security-log"),
        new(Security, 40, ">radio"),
        new(Security, 50, ">flashlight"),
        new(Security, 60, "locker locked level=2"),
        new(Security, 40, ">pistol"),
        new(Security, 40, ">magazine-9mm x2"),
        new(Security, 40, ">rounds-9mm x20"),
        new(Security, 30, ">taser"),

        // armouries: always a locker, locked at level 3
        new(Armouries, 100, "locker locked level=3"),
        new(Armouries, 50, ">shotgun"),
        new(Armouries, 60, ">shells-12 x12"),
        new(Armouries, 50, ">pistol"),
        new(Armouries, 50, ">magazine-9mm x2"),
        new(Armouries, 60, ">rounds-9mm x30"),
        new(Armouries, 30, ">rifle"),
        new(Armouries, 40, ">rounds-308 x10"),
        new(Armouries, 50, ">tactical-vest"),
        new(Armouries, 30, ">taser"),
        new(Armouries, 80, ">tranq-rifle", ["raptors"]),
        new(Armouries, 80, ">tranq-dart x6", ["raptors"]),

        // workshops and stores
        new(Workshops, 80, "cabinet"),
        new(Workshops, 50, ">tool-kit"),
        new(Workshops, 35, ">spare-parts"),
        new(Workshops, 50, ">duct-tape"),
        new(Workshops, 40, ">fuse x4"),
        new(Workshops, 30, ">multitool"),
        new(Workshops, 30, ">wire-cutters"),
        new(Workshops, 40, ">work-gloves"),
        new(Workshops, 20, ">bolt-cutters"),
        new(Workshops, 30, ">wire-spool"),
        new(Workshops, 40, "crate"),
        new(Workshops, 50, ">scrap-metal x2"),
        new(Workshops, 30, ">circuit-board x2"),
        new(Workshops, 25, "crowbar"),
        new(Workshops, 40, "jerry-can"),
        new(Workshops, 30, "lantern"),
        new(Workshops, 20, "stick x2"),
        new(Workshops, 25, "hard-hat"),

        // plant rooms
        new(Plant, 50, "jerry-can"),
        new(Plant, 25, "spare-parts"),
        new(Plant, 60, "fire-extinguisher"),
        new(Plant, 40, "cabinet"),
        new(Plant, 50, ">fuse x2"),
        new(Plant, 40, ">hard-hat"),
        new(Plant, 30, ">work-gloves"),
        new(Plant, 20, ">tool-kit"),

        // kitchens: the fridge, then the cupboards
        new(Kitchens, 80, "fridge"),
        new(Kitchens, 50, ">canned-food x2"),
        new(Kitchens, 40, ">soda x2"),
        new(Kitchens, 30, ">canned-peaches x2"),
        new(Eating, 60, "cabinet"),
        new(Eating, 50, ">noodles x3"),
        new(Eating, 50, ">crackers x2"),
        new(Eating, 30, ">dried-fruit"),
        new(Eating, 20, ">mre x2"),
        new(Eating, 40, "lighter"),
        new(Eating, 30, "matches"),
        new(Eating, 50, "fire-extinguisher"),
        new(Eating, 20, "thermos"),
        new(Eating, 30, "rag x3"),

        // where people slept: a locker each room
        new(Sleeping, 80, "locker"),
        new(Sleeping, 30, ">poncho"),
        new(Sleeping, 20, ">boots"),
        new(Sleeping, 30, ">energy-bar x2"),
        new(Sleeping, 25, ">flashlight"),
        new(Sleeping, 20, ">daypack"),
        new(Sleeping, 20, ">glow-stick x3"),
        new(Sleeping, 30, ">chocolate"),
        new(Sleeping, 25, ">soda"),
        new(Sleeping, 20, ">lighter"),
        new(Sleeping, 20, ">notebook"),
        new(Sleeping, 20, ">jerky x3"),
        new(Sleeping, 15, ">canteen"),
        new(Sleeping, 15, ">belt-pouch"),

        new(Washrooms, 70, "medicine-cabinet"),
        new(Washrooms, 40, ">bandage x2"),
        new(Washrooms, 30, ">painkillers"),
        new(Washrooms, 25, ">antiseptic"),

        // clinics
        new(Clinics, 90, "medicine-cabinet"),
        new(Clinics, 50, ">first-aid-kit"),
        new(Clinics, 40, ">suture-kit"),
        new(Clinics, 40, ">antibiotics"),
        new(Clinics, 50, ">antiseptic"),
        new(Clinics, 30, ">epipen"),
        new(Clinics, 30, ">burn-gel"),
        new(Clinics, 60, ">bandage x4"),
        new(Clinics, 50, "cabinet"),
        new(Clinics, 30, ">splint"),
        new(Clinics, 30, ">tourniquet x2"),
        new(Clinics, 30, ">water-tablets"),
        new(["Veterinary room"], 30, ">tranq-dart x2"),

        // labs: their files are locked away
        new(Labs, 60, "cabinet locked level=2"),
        new(Labs, 50, ">research-file"),
        new(Labs, 40, ">data-drive"),
        new(Labs, 50, "desk"),
        new(Labs, 40, ">notebook"),
        new(Labs, 20, ">battery-aa x2"),
        new(Labs, 40, "fire-extinguisher"),
        new(Labs, 20, "burn-gel"),

        new(DataRooms, 40, "data-drive"),
        new(DataRooms, 40, "circuit-board x2"),
        new(Records, 70, "cabinet"),
        new(Records, 60, ">research-file"),
        new(Records, 40, ">security-log"),
        new(Records, 30, ">data-drive"),

        // control rooms and lookouts
        new(Control, 70, "desk"),
        new(Control, 40, ">radio"),
        new(Control, 30, ">flashlight"),
        new(Control, 30, ">battery-aa x2"),
        new(Control, 30, ">keycard level=2"),
        new(Control, 30, "binoculars"),
        new(Lookouts, 70, "binoculars"),

        new(["Map room"], 80, "desk"),
        new(["Map room"], 60, ">compass"),
        new(["Map room"], 50, ">notebook"),

        new(["Laundry"], 60, "cabinet"),
        new(["Laundry"], 40, ">poncho"),
        new(["Laundry"], 30, ">work-gloves"),
        new(["Laundry"], 60, ">rag x5"),
        new(["Laundry"], 20, ">boots"),

        new(Animals, 30, "rag x3"),
        new(Animals, 40, "stick x2"),
        new(Animals, 15, "tranq-dart x2"),

        new(["Cryo vault"], 20, "data-drive"),

        // park maps (step 25b-2): common, but each charts only a tenth of its facility's area (the area a
        // station stands in), so charting an area takes several. A front office per kind of facility,
        // a second room per kind, and the transit stations
        new(["Lobby", "Security office"], 70, ">park-map", ["genetics"]),
        new(["Offices"], 70, ">park-map", ["court", "outstation", "orchard"]),
        new(["Harbourmaster's office"], 70, ">park-map", ["docks"]),
        new(["Operations office"], 70, ">park-map", ["airstrip"]),
        new(["Control room"], 70, ">park-map", ["geothermal"]),
        new(["Array control"], 70, ">park-map", ["weather"]),
        new(["Observation room"], 70, ">park-map", ["orgone"]),
        new(["Office"], 70, ">park-map", ["depot", "yard"]),
        new(["Map room"], 70, ">park-map", ["survey"]),
        new(["Radio room"], 70, ">park-map", ["comms"]),
        new(["Keeper's office"], 70, ">park-map", ["raptors"]),
        new(["Recording booths"], 70, ">park-map", ["choir"]),
        new(["Chart room"], 70, ">park-map", ["drowned"]),
        new(["Lab offices", "Director's office"], 50, ">park-map", ["genetics"]),
        new(["Map room", "Radio room"], 50, ">park-map", ["court"]),
        new(["Customs", "Loading office"], 50, ">park-map", ["docks"]),
        new(["Check-in", "Departure lounge"], 50, ">park-map", ["airstrip"]),
        new(["Workshop"], 50, ">park-map", ["geothermal"]),
        new(["Radar room"], 50, ">park-map", ["weather"]),
        new(["Laboratory"], 50, ">park-map", ["orgone"]),
        new(["Wet lab"], 50, ">park-map", ["outstation"]),
        new(["Dispatch"], 50, ">park-map", ["depot"]),
        new(["Instrument room"], 50, ">park-map", ["survey", "drowned"]),
        new(["Bunk room"], 50, ">park-map", ["comms"]),
        new(["Observation room"], 50, ">park-map", ["raptors"]),
        new(["Lookout"], 50, ">park-map", ["watch"]),
        new(["Potting room"], 50, ">park-map", ["orchard"]),
        new(["Controller room"], 50, ">park-map", ["yard"]),
        new(["Elevator lobby", "Cabin hall"], 80, ">park-map", ["transit"]),

        // keycards outside the doors they open (step 25b-2): every facility with secure rooms has a card
        // for its top level somewhere that isn't itself behind one of those doors
        new(["Offices"], 40, ">keycard level=2", ["outstation", "court"]),
        new(["Lab offices"], 35, ">keycard level=2", ["genetics"]),
        new(["Observation room"], 40, ">keycard level=3", ["orgone"]),
        new(["Keeper's office"], 40, ">keycard level=3", ["raptors"]),
        new(["Chart room"], 40, ">keycard level=3", ["drowned"]),
    ];

    /// <summary>A building of this many rooms or more keeps a fire extinguisher by its first room's door, most of the time.</summary>
    private const int ExtinguisherRooms = 4, ExtinguisherPercent = 70;

    /// <summary>The run's loot as a ledger of items lying on the island, ready for Simulation.UseItems.</summary>
    public static ItemLedger Build(IslandWorld world, ulong runSeed)
    {
        IslandWorks works = world.Works;
        ulong seed = new StateHasher().Add((ulong)world.Params.Seed).Add("loot").Add(runSeed).Value;
        var ledger = new ItemLedger();
        var siteKinds = new Dictionary<string, string>();
        var siteIndex = new Dictionary<string, int>();
        for (int i = 0; i < works.Sites.Count; i++)
        {
            siteKinds.TryAdd(works.Sites[i].Name, works.Sites[i].Kind);
            siteIndex.TryAdd(works.Sites[i].Name, i);
        }
        FacilityAreas areas = world.Areas();
        var mapsIn = new Dictionary<int, int>(); // park maps placed so far in each area

        // the furniture of each room, by the room's label
        var furniture = new Dictionary<int, List<Prim>>();
        foreach (Prim p in works.Prims.InOrder())
        {
            if (p.Kind != PrimKind.Furniture)
                continue;
            if (!furniture.TryGetValue(p.Label, out var list))
                furniture[p.Label] = list = new List<Prim>();
            list.Add(p);
        }
        Objective? key = works.Objectives.FirstOrDefault(o => o.Key == "monorail-key");

        foreach (Prim building in works.Prims.InOrder())
        {
            if (building.Kind != PrimKind.Building || building.Rooms is not { Count: > 0 } rooms)
                continue;
            var doors = building.Doors ?? new List<(int X, int Y)>();
            for (int index = 0; index < rooms.Count; index++)
            {
                Room room = rooms[index];
                string label = works.Labels[room.Label];
                string[] parts = label.Split(" · ");
                string site = parts[0], name = parts[^1];
                string kind = siteKinds.TryGetValue(site, out string? k) ? k : "transit";
                var floor = new Floor(FreeTiles(room, furniture.TryGetValue(room.Label, out var stuff) ? stuff : null, doors), seed, label);
                if (floor.IsEmpty)
                    continue;
                // the area a park map found here charts: its facility's, or the one a station stands in
                int area = siteIndex.TryGetValue(site, out int si) ? si : areas.AreaAt((room.X0 + room.X1) / 2.0, (room.Y0 + room.Y1) / 2.0);

                Item? container = null;
                for (int row = 0; row < Table.Length; row++)
                {
                    Loot loot = Table[row];
                    if (!loot.Matches(name, kind) || Roll(seed, label, row) >= loot.Percent)
                        continue;
                    foreach (ItemSpec spec in loot.Items)
                    {
                        foreach (Item item in Make(ledger, spec, site, seed, label, row))
                        {
                            if (item.Kind == ItemKind.ParkMap && area >= 0 && areas.SheetOrder(area) is { Count: > 0 } order)
                            {
                                int nth = mapsIn.GetValueOrDefault(area);
                                mapsIn[area] = nth + 1;
                                item.Tag = ParkMaps.Tag(works.Sites[area], order[nth % order.Count]);
                            }
                            if (loot.Inside && container is not null && container.RefusesToHold(item) is null)
                            {
                                container.Insert(item);
                                continue;
                            }
                            var (x, y) = floor.Take(againstWall: item.Def.Is(ItemCategory.Fixture) || item.IsContainer && item.Def.Size == ItemSize.TwoHanded);
                            ledger.Lay(item, x, y, onDeck: false);
                            if (item.Def.Holds is { Only: null } && !loot.Inside)
                                container = item;
                        }
                    }
                }

                if (index == 0 && rooms.Count >= ExtinguisherRooms && Roll(seed, label, -1) < ExtinguisherPercent)
                {
                    var (x, y) = floor.Take(againstWall: true);
                    ledger.Lay(ledger.Create(ItemKind.FireExtinguisher), x, y, onDeck: false);
                }

                if (key is not null && key.Site == site && label == $"{key.Site} · {key.Room}")
                {
                    var (x, y) = floor.Take(againstWall: false);
                    ledger.Lay(ledger.Create(ItemKind.Keycard, tag: "monorail", level: 1), x, y, onDeck: false);
                    key = null; // one key
                }
            }
        }
        return ledger;
    }

    /// <summary>A roll of 0–99 for one row of one room.</summary>
    private static int Roll(ulong seed, string label, int row) =>
        (int)(new StateHasher().Add(seed).Add(label).Add(row).Value % 100);

    /// <summary>
    /// The items one spec makes for a room: locks and keycards tagged with the facility; fills (water,
    /// fuel, a lighter's uses, batteries' charge) somewhere between a fifth and full; guns and
    /// magazines empty unless the spec says.
    /// </summary>
    private static IEnumerable<Item> Make(ItemLedger ledger, ItemSpec spec, string site, ulong seed, string label, int row)
    {
        var def = ItemCatalog.Of(spec.Kind);
        int copies = def.Stacks ? 1 : spec.Count;
        for (int i = 0; i < copies; i++)
        {
            bool keyed = def.Is(ItemCategory.Key) || spec.Locked;
            var item = ledger.Create(spec.Kind, def.Stacks ? spec.Count : spec.Amount, keyed ? spec.Tag ?? site : spec.Tag, spec.Level,
                fitted: !spec.Empty);
            item.Locked = spec.Locked && def.Holds is { Closes: true };
            int n = 0;
            foreach (Item part in item.SelfAndNested())
            {
                var partDef = part.Def;
                if (spec.Amount is null && !partDef.Stacks && partDef.Unit is not null && partDef.Fills is null && partDef.StartAmount > 0)
                {
                    int percent = 20 + (int)(new StateHasher().Add(seed).Add(label).Add(row).Add(i).Add(n++).Add("fill").Value % 81);
                    part.Amount = Math.Max(1, partDef.MaxAmount * percent / 100);
                }
            }
            yield return item;
        }
    }

    /// <summary>
    /// A room's free floor: inside its walls, off its furniture, and not just inside a door (so
    /// nothing stands in a doorway). Island metres, row by row.
    /// </summary>
    private static List<(int X, int Y)> FreeTiles(Room room, List<Prim>? furniture, List<(int X, int Y)> doors)
    {
        var free = new List<(int X, int Y)>();
        for (int y = room.Y0 + 1; y <= room.Y1 - 1; y++)
        {
            for (int x = room.X0 + 1; x <= room.X1 - 1; x++)
            {
                if (furniture is not null && furniture.Any(f => x >= f.X0 && x <= f.X1 && y >= f.Y0 && y <= f.Y1))
                    continue;
                if (doors.Any(d => Math.Abs(d.X - x) + Math.Abs(d.Y - y) <= 1))
                    continue;
                free.Add((x, y));
            }
        }
        return free;
    }

    /// <summary>A room's free floor, handed out a tile at a time, by the seed: things against the walls first if asked.</summary>
    private sealed class Floor
    {
        private readonly List<(int X, int Y)> _all;
        private readonly List<(int X, int Y)> _left;
        private readonly ulong _seed;
        private readonly string _label;
        private readonly int _x0, _x1, _y0, _y1;
        private int _taken;

        public Floor(List<(int X, int Y)> tiles, ulong seed, string label)
        {
            _all = tiles;
            _left = new List<(int X, int Y)>(tiles);
            _seed = seed;
            _label = label;
            if (tiles.Count > 0)
            {
                _x0 = tiles.Min(t => t.X);
                _x1 = tiles.Max(t => t.X);
                _y0 = tiles.Min(t => t.Y);
                _y1 = tiles.Max(t => t.Y);
            }
        }

        public bool IsEmpty => _all.Count == 0;

        /// <summary>A free tile (against a wall if asked and there is one), taken so the next thing goes elsewhere; when none are left, any of the room's.</summary>
        public (int X, int Y) Take(bool againstWall)
        {
            var pool = _left.Count > 0 ? _left : _all;
            var choice = againstWall ? pool.Where(t => t.X == _x0 || t.X == _x1 || t.Y == _y0 || t.Y == _y1).ToList() : pool;
            if (choice.Count == 0)
                choice = pool;
            var tile = choice[(int)(new StateHasher().Add(_seed).Add(_label).Add("tile").Add(_taken++).Value % (ulong)choice.Count)];
            _left.Remove(tile);
            return tile;
        }
    }
}
