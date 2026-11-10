namespace BlueHarvest.Sim.World.Generation;

/// <summary>How a room is furnished.</summary>
public enum Furnish : byte { None, Rack, Console, Furniture, Bunks, Tanks, Lift }

public readonly record struct RoomSpec(string Name, int Weight, Furnish Furnish = Furnish.None);

public sealed record BuildingSpec(string Name, int W, int H, RoomSpec[] Rooms);

public enum YardKind : byte { Helipad, Tanks, Tower, Parking, Dish, Dome, Ring, Switchyard, Antennas, Towers, Marker, Pen }

/// <summary>An outdoor feature of a compound. A and B are its numbers (count, size or radius, by kind).</summary>
public sealed record YardSpec(YardKind Kind, string Label = "", int A = 0, int B = 0);

public sealed record FacilityProgram(BuildingSpec[] Buildings, YardSpec[] Yard);

/// <summary>A building or yard feature placed in a compound, relative to the compound's centre.</summary>
public sealed record LayoutItem(int X, int Y, int W, int H, BuildingSpec? Building, YardSpec? Yard);

/// <summary>A programme shelf-packed into a square compound; R is its radius.</summary>
public sealed record CompoundLayout(int R, LayoutItem[] Items);

/// <summary>
/// What each kind of facility is built from, and how a programme becomes buildings, rooms,
/// furniture and yard features. Tables match the island lab's PROGRAMS.
/// </summary>
public static class FacilityPrograms
{
    private static RoomSpec Rm(string name, int w, Furnish f = Furnish.None) => new(name, w, f);

    private static RoomSpec[] Bunks(int n, string prefix = "Bunk room") =>
        Enumerable.Range(1, n).Select(k => new RoomSpec(prefix + " " + k, 1, Furnish.Bunks)).ToArray();

    private static RoomSpec[] Join(RoomSpec[] a, params RoomSpec[] b) => a.Concat(b).ToArray();

    private const Furnish Rack = Furnish.Rack, Con = Furnish.Console, Fur = Furnish.Furniture, Bnk = Furnish.Bunks, Tnk = Furnish.Tanks;

    public static readonly IReadOnlyDictionary<string, FacilityProgram> Programs = new Dictionary<string, FacilityProgram>
    {
        ["genetics"] = new(
        [
            new("Main laboratory", 64, 30, [Rm("Sequencing lab", 3, Con), Rm("Embryology lab", 3, Fur), Rm("Clean room", 2, Fur), Rm("Microscopy", 2, Con), Rm("Fertilisation lab", 2, Fur), Rm("Decontamination", 1), Rm("Lab offices", 2, Fur), Rm("Washrooms", 1)]),
            new("Server farm", 44, 28, [Rm("Server hall", 6, Rack), Rm("Cooling plant", 2, Tnk), Rm("UPS and batteries", 2, Rack), Rm("Network operations", 2, Con)]),
            new("Hatchery", 38, 24, [Rm("Incubation hall", 4, Rack), Rm("Nursery pens", 3), Rm("Veterinary room", 1, Fur), Rm("Feed store", 1, Rack)]),
            new("Deep freeze", 26, 20, [Rm("Airlock", 1), Rm("Cryo vault", 4, Rack), Rm("Liquid nitrogen store", 2, Tnk)]),
            new("Dormitory", 46, 17, Join(Bunks(8), Rm("Showers", 1), Rm("Washrooms", 1), Rm("Laundry", 1, Rack), Rm("Common room", 2, Fur))),
            new("Mess hall", 28, 20, [Rm("Dining room", 4, Fur), Rm("Kitchen", 2, Con), Rm("Cold store", 1, Rack), Rm("Pantry", 1, Rack)]),
            new("Administration and security", 28, 16, [Rm("Lobby", 2), Rm("Security office", 1, Con), Rm("Armoury", 1, Rack), Rm("Director's office", 1, Fur), Rm("Records", 1, Rack)]),
            new("Generator house", 18, 12, [Rm("Backup generators", 3, Tnk), Rm("Switch room", 1, Con)]),
            new("Vehicle garage", 28, 16, [Rm("Vehicle bays", 4), Rm("Workshop", 1, Fur)]),
        ],
        [new(YardKind.Helipad), new(YardKind.Tanks, "Fuel tanks", 3), new(YardKind.Tower, "Water tower"), new(YardKind.Parking, "", 30, 16)]),
        ["control"] = new(
        [
            new("Operations centre", 50, 28, [Rm("Control room", 5, Con), Rm("Data centre", 2, Rack), Rm("Briefing room", 2, Fur), Rm("Radio room", 1, Con), Rm("Offices", 2, Fur), Rm("Washrooms", 1), Rm("Canteen", 2, Fur)]),
            new("Staff quarters", 34, 15, Join(Bunks(5), Rm("Showers", 1), Rm("Washrooms", 1), Rm("Kitchen", 2, Con))),
            new("Generator house", 16, 12, [Rm("Backup generators", 3, Tnk), Rm("Switch room", 1, Con)]),
            new("Motor pool", 26, 14, [Rm("Vehicle bays", 4), Rm("Parts store", 1, Rack)]),
        ],
        [new(YardKind.Tower, "Radio mast"), new(YardKind.Helipad), new(YardKind.Parking, "", 26, 14)]),
        ["docks"] = new(
        [
            new("Warehouse", 40, 24, [Rm("Cargo hall", 5, Rack), Rm("Cold store", 1, Rack), Rm("Loading office", 1, Fur)]),
            new("Harbour office", 20, 12, [Rm("Harbourmaster's office", 1, Con), Rm("Radio room", 1, Con), Rm("Customs", 1, Fur), Rm("Washrooms", 1)]),
            new("Boathouse", 24, 14, [Rm("Boat bays", 3), Rm("Workshop", 1, Fur)]),
        ],
        [new(YardKind.Tanks, "Fuel tanks", 3), new(YardKind.Tower, "Dock crane"), new(YardKind.Parking, "", 20, 12)]),
        ["airstrip"] = new(
        [
            new("Hangar", 38, 30, [Rm("Hangar floor", 6), Rm("Workshop", 1, Fur)]),
            new("Terminal", 30, 16, [Rm("Departure lounge", 3, Fur), Rm("Check-in", 1, Fur), Rm("Operations office", 1, Con), Rm("Cargo room", 2, Rack), Rm("Washrooms", 1)]),
            new("Control tower", 9, 9, [Rm("Tower cab", 1, Con)]),
        ],
        [new(YardKind.Tanks, "Fuel tanks", 2), new(YardKind.Tower, "Windsock"), new(YardKind.Parking, "", 18, 10)]),
        ["comms"] = new(
        [
            new("Equipment hut", 16, 11, [Rm("Radio room", 2, Con), Rm("Battery room", 1, Rack), Rm("Bunk room", 1, Bnk)]),
            new("Generator shed", 9, 7, [Rm("Generator", 1, Tnk)]),
        ],
        [new(YardKind.Tower, "Lattice mast"), new(YardKind.Dish, "Satellite dish")]),
        ["nuclear"] = new(
        [
            new("Turbine hall", 64, 24, [Rm("Turbine hall", 6, Tnk), Rm("Feedwater pumps", 1, Tnk)]),
            new("Control building", 32, 18, [Rm("Main control room", 3, Con), Rm("Shift office", 1, Fur), Rm("Health physics", 1, Rack), Rm("Kitchen", 1, Con), Rm("Washrooms", 1), Rm("Security", 1, Con)]),
            new("Spent-fuel store", 22, 14, [Rm("Fuel pool", 2, Tnk), Rm("Cask store", 1, Rack)]),
            new("Pump house", 14, 10, [Rm("Cooling-water pumps", 1, Tnk)]),
            new("Guard post", 8, 6, [Rm("Gatehouse", 1, Con)]),
        ],
        [new(YardKind.Dome, "Reactor containment", 22), new(YardKind.Switchyard)]),
        ["geothermal"] = new(
        [
            new("Power house", 42, 20, [Rm("Turbine hall", 4, Tnk), Rm("Control room", 1, Con), Rm("Workshop", 1, Fur)]),
            new("Separator station", 14, 10, [Rm("Steam separators", 1, Tnk)]),
        ],
        [new(YardKind.Ring, "Cooling tower", 9), new(YardKind.Ring, "Cooling tower", 9), new(YardKind.Tanks, "Wellheads", 4)]),
        ["weather"] = new(
        [
            new("Control bunker", 28, 16, [Rm("Array control", 3, Con), Rm("Radar room", 1, Con), Rm("Generator room", 1, Tnk), Rm("Bunk room", 1, Bnk), Rm("Galley", 1, Fur), Rm("Washroom", 1)]),
        ],
        [new(YardKind.Antennas, "Antenna field"), new(YardKind.Dome, "Radar dome", 8)]),
        ["orgone"] = new(
        [
            new("Accumulator hall", 34, 20, [Rm("Accumulator chamber", 4, Rack), Rm("Observation room", 1, Con)]),
            new("Laboratory", 26, 16, [Rm("Laboratory", 2, Fur), Rm("Records", 1, Rack), Rm("Quarters", 1, Bnk), Rm("Washroom", 1)]),
        ],
        [new(YardKind.Towers, "Cloudbuster array", 3)]),
        ["lab"] = new(
        [
            new("Laboratory", 38, 20, [Rm("Wet lab", 2, Fur), Rm("Genetics lab", 2, Con), Rm("Specimen store", 1, Rack), Rm("Animal holding", 2, Rack), Rm("Offices", 1, Fur), Rm("Washrooms", 1)]),
            new("Bunkhouse", 24, 13, Join(Bunks(3), Rm("Kitchen", 1, Con), Rm("Washrooms", 1))),
            new("Generator shed", 10, 8, [Rm("Generator", 1, Tnk)]),
        ],
        [new(YardKind.Tanks, "Water tanks", 2)]),
        ["depot"] = new(
        [
            new("Warehouse A", 36, 22, [Rm("Store", 5, Rack), Rm("Office", 1, Fur)]),
            new("Warehouse B", 30, 20, [Rm("Store", 5, Rack)]),
            new("Depot office", 14, 10, [Rm("Dispatch", 1, Con), Rm("Washroom", 1)]),
        ],
        [new(YardKind.Tanks, "Fuel tanks", 4), new(YardKind.Parking, "", 30, 16)]),
        ["ley"] = new(
        [new("Survey hut", 16, 10, [Rm("Map room", 2, Fur), Rm("Instrument room", 1, Con), Rm("Bunks", 1, Bnk)])],
        [new(YardKind.Tower, "Instrument mast"), new(YardKind.Marker, "Survey marker")]),
        ["watch"] = new(
        [new("Bunk hut", 12, 8, [Rm("Bunk room", 1, Bnk), Rm("Radio room", 1, Con)]), new("Watchtower", 6, 6, [Rm("Lookout", 1, Con)])],
        [new(YardKind.Tower, "Searchlight")]),
        ["raptors"] = new(
        [
            new("Observation bunker", 22, 10, [Rm("Observation room", 3, Con), Rm("Airlock", 1)]),
            new("Keeper station", 22, 12, [Rm("Keeper's office", 2, Con), Rm("Armoury", 1, Rack), Rm("Washroom", 1)]),
            new("Holding block", 34, 10, [Rm("Holding cell 1", 1), Rm("Holding cell 2", 1), Rm("Holding cell 3", 1), Rm("Holding cell 4", 1), Rm("Squeeze chute", 1)]),
        ],
        [new(YardKind.Pen, "Raptor enclosure", 120, 90), new(YardKind.Tower, "Feeding gantry")]),
    };

    /// <summary>The viewing station at each paddock gate.</summary>
    public static readonly BuildingSpec ViewingStation = new("Viewing station", 16, 9, [Rm("Viewing gallery", 3, Fur), Rm("Keeper's room", 1, Con)]);

    private static (int W, int H) YardSize(YardSpec y) => y.Kind switch
    {
        YardKind.Helipad => (24, 24),
        YardKind.Tanks => (y.A * 8, 8),
        YardKind.Tower => (6, 6),
        YardKind.Parking => (y.A, y.B),
        YardKind.Dish => (14, 14),
        YardKind.Dome or YardKind.Ring => (y.A * 2 + 4, y.A * 2 + 4),
        YardKind.Switchyard => (40, 28),
        YardKind.Antennas => (72, 56),
        YardKind.Towers => (y.A * 10, 10),
        YardKind.Marker => (4, 4),
        YardKind.Pen => (y.A, y.B),
        _ => (8, 8),
    };

    private static readonly Dictionary<string, CompoundLayout> LayoutCache = new();
    private static readonly object CacheLock = new();

    /// <summary>Shelf-packs a programme into a square compound centred on 0,0.</summary>
    public static CompoundLayout Layout(string kind)
    {
        lock (CacheLock)
        {
            if (LayoutCache.TryGetValue(kind, out CompoundLayout? cached)) return cached;
            FacilityProgram prog = Programs[kind];
            var raw = new List<(int W, int H, BuildingSpec? B, YardSpec? Y)>();
            foreach (BuildingSpec b in prog.Buildings) raw.Add((b.W, b.H, b, null));
            foreach (YardSpec y in prog.Yard) { (int w, int h) = YardSize(y); raw.Add((w, h, null, y)); }
            // tallest first, then widest; stable
            var items = raw.OrderByDescending(it => it.H).ThenByDescending(it => it.W).ToList();
            const int gap = 6;
            int total = 0;
            foreach (var it in items) total += (it.W + gap) * (it.H + gap);
            int rowW = Math.Max(items.Max(i => i.W), (int)Math.Ceiling(Math.Sqrt(total * 1.3)));
            int x = 0, y0 = 0, rowH = 0, maxW = 0;
            var pos = new (int X, int Y)[items.Count];
            for (int k = 0; k < items.Count; k++)
            {
                var it = items[k];
                if (x > 0 && x + it.W > rowW) { y0 += rowH + gap; x = 0; rowH = 0; }
                pos[k] = (x, y0);
                x += it.W + gap; rowH = Math.Max(rowH, it.H); maxW = Math.Max(maxW, x - gap);
            }
            int hh = y0 + rowH;
            var placed = new LayoutItem[items.Count];
            for (int k = 0; k < items.Count; k++)
            {
                var it = items[k];
                placed[k] = new LayoutItem((int)GenMath.Round(pos[k].X - maxW / 2.0), (int)GenMath.Round(pos[k].Y - hh / 2.0), it.W, it.H, it.B, it.Y);
            }
            var layout = new CompoundLayout((int)Math.Ceiling(Math.Max(maxW, hh) / 2.0 + 10), placed);
            LayoutCache[kind] = layout;
            return layout;
        }
    }

    /// <summary>Builds a facility's programme at a position: buildings with rooms and furniture, and the yard.</summary>
    public static void BuildCompound(double siteX, double siteY, string siteName, string kind, List<string> labels, PrimList prims)
    {
        CompoundLayout layout = Layout(kind);
        int cx = (int)GenMath.Round(siteX), cy = (int)GenMath.Round(siteY);
        foreach (LayoutItem it in layout.Items)
        {
            int x0 = cx + it.X, y0 = cy + it.Y, x1 = x0 + it.W - 1, y1 = y0 + it.H - 1;
            if (it.Building != null) Building(it.Building, x0, y0, x1, y1, siteName, labels, prims);
            else YardFeature(it.Yard!, x0, y0, x1, y1, siteName, labels, prims);
        }
    }

    private static int Lab(List<string> labels, string name)
    {
        labels.Add(name);
        return labels.Count - 1;
    }

    /// <summary>
    /// A building with rooms either side of a corridor along its long axis, doors onto the corridor and
    /// entrances at both ends; small buildings are a single room.
    /// </summary>
    public static void Building(BuildingSpec b, int x0, int y0, int x1, int y1, string site, List<string> labels, PrimList prims)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        bool horiz = w >= h;
        // work in (u along the long side, v across), then map back
        int uLen = horiz ? w : h, vLen = horiz ? h : w;
        (int X, int Y) ToXY(int u, int v) => horiz ? (x0 + u, y0 + v) : (x0 + v, y0 + u);
        var rooms = new List<Room>();
        var doors = new List<(int X, int Y)>();
        int label = Lab(labels, site + " · " + b.Name);
        RoomSpec[] specs = b.Rooms;
        if (specs.Length == 1 || vLen < 7)
        {
            RoomSpec r0 = specs[0];
            int rl = Lab(labels, site + " · " + b.Name + " · " + r0.Name);
            rooms.Add(new Room(x0, y0, x1, y1, rl));
            doors.Add(ToXY(uLen / 2, vLen - 1));
            Furnishing(r0.Furnish, x0, y0, x1, y1, rl, prims);
        }
        else
        {
            bool two = vLen >= 11;
            int c0 = two ? vLen / 2 - 1 : vLen - 3, c1 = c0 + 1;
            var sides = two ? new[] { new List<RoomSpec>(), new List<RoomSpec>() } : new[] { new List<RoomSpec>() };
            int wA = 0, wB = 0;
            foreach (RoomSpec r in specs)
            {
                if (!two || wA <= wB) { sides[0].Add(r); wA += r.Weight; }
                else { sides[1].Add(r); wB += r.Weight; }
            }
            for (int si = 0; si < sides.Length; si++)
            {
                List<RoomSpec> list = sides[si];
                if (list.Count == 0) continue;
                int v0 = si == 0 ? 0 : c1 + 1, v1 = si == 0 ? c0 - 1 : vLen - 1, tot = list.Sum(r => r.Weight);
                int u = 0;
                for (int k = 0; k < list.Count; k++)
                {
                    RoomSpec r = list[k];
                    int uEnd = k == list.Count - 1 ? uLen - 1 : Math.Max(u + 3, (int)GenMath.Round(u + (uLen - 1) * r.Weight / (double)tot));
                    (int ax, int ay) = ToXY(u, v0);
                    (int bx, int by) = ToXY(uEnd, v1);
                    int rx0 = Math.Min(ax, bx), ry0 = Math.Min(ay, by), rx1 = Math.Max(ax, bx), ry1 = Math.Max(ay, by);
                    int rl = Lab(labels, site + " · " + b.Name + " · " + r.Name);
                    rooms.Add(new Room(rx0, ry0, rx1, ry1, rl));
                    doors.Add(ToXY((u + uEnd) / 2, si == 0 ? v1 : v0));
                    Furnishing(r.Furnish, rx0, ry0, rx1, ry1, rl, prims);
                    u = uEnd;
                }
            }
            doors.Add(ToXY(0, c0));
            doors.Add(ToXY(uLen - 1, c0));
        }
        prims.AddFront(new Prim { Kind = PrimKind.Building, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Label = label, Rooms = rooms, Doors = doors });
    }

    private static void Furnishing(Furnish kind, int x0, int y0, int x1, int y1, int label, PrimList prims)
    {
        int ix0 = x0 + 2, iy0 = y0 + 2, ix1 = x1 - 2, iy1 = y1 - 2;
        if (kind == Furnish.None || ix1 < ix0 || iy1 < iy0) return;
        bool horiz = (x1 - x0) >= (y1 - y0);
        void Add(int a, int b, int c, int d, Cover cover) =>
            prims.Add(new Prim { Kind = PrimKind.Furniture, X0 = a, Y0 = b, X1 = c, Y1 = d, Cover = cover, Label = label });
        switch (kind)
        {
            case Furnish.Rack: // rows with aisles between
                if (horiz) { for (int y = iy0; y <= iy1; y += 2) Add(ix0, y, ix1, y, Cover.Rack); }
                else { for (int x = ix0; x <= ix1; x += 2) Add(x, iy0, x, iy1, Cover.Rack); }
                break;
            case Furnish.Console: // a bank of consoles along the far wall, desks in the middle
                if (horiz)
                {
                    Add(ix0, iy0, ix1, iy0, Cover.Console);
                    if (iy1 - iy0 >= 3) { int my = FloorDiv2(iy0 + iy1) + 1; Add(ix0 + 1, my, ix1 - 1, my, Cover.Furniture); }
                }
                else
                {
                    Add(ix0, iy0, ix0, iy1, Cover.Console);
                    if (ix1 - ix0 >= 3) { int mx = FloorDiv2(ix0 + ix1) + 1; Add(mx, iy0 + 1, mx, iy1 - 1, Cover.Furniture); }
                }
                break;
            case Furnish.Furniture: // tables on a grid
                for (int y = iy0; y + 1 <= iy1; y += 3)
                    for (int x = ix0; x + 1 <= ix1; x += 4) Add(x, y, x + 1, y, Cover.Furniture);
                break;
            case Furnish.Bunks: // bunks along both long walls
                if (horiz)
                {
                    for (int x = ix0 - 1; x <= ix1 + 1; x += 2)
                    {
                        Add(x, iy0 - 1, x, iy0 - 1, Cover.Furniture);
                        if (iy1 > iy0) Add(x, iy1 + 1, x, iy1 + 1, Cover.Furniture);
                    }
                }
                else
                {
                    for (int y = iy0 - 1; y <= iy1 + 1; y += 2)
                    {
                        Add(ix0 - 1, y, ix0 - 1, y, Cover.Furniture);
                        if (ix1 > ix0) Add(ix1 + 1, y, ix1 + 1, y, Cover.Furniture);
                    }
                }
                break;
            case Furnish.Lift: // the elevator car fills the room
                Add(x0 + 1, y0 + 1, x1 - 1, y1 - 1, Cover.Elevator);
                break;
            case Furnish.Tanks: // tanks and machinery blocks
                for (int y = iy0; y + 1 <= iy1; y += 4)
                    for (int x = ix0; x + 1 <= ix1; x += 4) Add(x, y, x + 1, y + 1, Cover.Tank);
                break;
        }
    }

    /// <summary>Math.floor(v / 2) for ints of either sign (C# division truncates toward zero).</summary>
    private static int FloorDiv2(int v) => v >> 1;

    private static void YardFeature(YardSpec y, int x0, int y0, int x1, int y1, string site, List<string> labels, PrimList prims)
    {
        double cx = (x0 + x1 + 1) / 2.0, cy = (y0 + y1 + 1) / 2.0;
        switch (y.Kind)
        {
            case YardKind.Helipad:
                prims.Add(new Prim { Kind = PrimKind.Circle, X = cx, Y = cy, R = 10, Cover = Cover.Helipad, Label = Lab(labels, site + " · Helipad") });
                break;
            case YardKind.Tanks:
            {
                int label = Lab(labels, site + " · " + y.Label);
                for (int k = 0; k < y.A; k++) prims.Add(new Prim { Kind = PrimKind.Circle, X = x0 + 4 + k * 8, Y = cy, R = 3, Cover = Cover.Tank, Label = label });
                break;
            }
            case YardKind.Tower:
                prims.Add(new Prim { Kind = PrimKind.Tower, X = cx, Y = cy, Label = Lab(labels, site + " · " + y.Label) });
                break;
            case YardKind.Parking:
                prims.Add(new Prim { Kind = PrimKind.Rect, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Cover = Cover.Road, Label = Lab(labels, site + " · Car park") });
                break;
            case YardKind.Dish:
                prims.Add(new Prim { Kind = PrimKind.Circle, X = cx, Y = cy, R = 6, Cover = Cover.Console, Ring = true, Fill = Cover.Floor, Label = Lab(labels, site + " · " + y.Label) });
                break;
            case YardKind.Dome:
                prims.Add(new Prim { Kind = PrimKind.Circle, X = cx, Y = cy, R = y.A, Cover = Cover.Wall, Ring = true, Fill = Cover.Floor, Door = true, Label = Lab(labels, site + " · " + y.Label) });
                break;
            case YardKind.Ring:
                prims.Add(new Prim { Kind = PrimKind.Circle, X = cx, Y = cy, R = y.A, Cover = Cover.Wall, Ring = true, Fill = Cover.Floor, Label = Lab(labels, site + " · " + y.Label) });
                break;
            case YardKind.Switchyard:
            {
                int label = Lab(labels, site + " · Switchyard");
                prims.Add(new Prim { Kind = PrimKind.Rect, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Cover = Cover.Yard, Label = label, Fence = true });
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 2; b++) prims.Add(new Prim { Kind = PrimKind.Tower, X = x0 + 8 + a * 12, Y = y0 + 8 + b * 12, Label = label });
                break;
            }
            case YardKind.Antennas:
            {
                int label = Lab(labels, site + " · " + y.Label);
                for (int a = 0; a < 5; a++)
                    for (int b = 0; b < 4; b++) prims.Add(new Prim { Kind = PrimKind.Tower, X = x0 + 6 + a * 15, Y = y0 + 6 + b * 14, Label = label });
                break;
            }
            case YardKind.Towers:
            {
                int label = Lab(labels, site + " · " + y.Label);
                for (int k = 0; k < y.A; k++) prims.Add(new Prim { Kind = PrimKind.Tower, X = x0 + 5 + k * 10, Y = cy, Label = label });
                break;
            }
            case YardKind.Marker:
                prims.Add(new Prim { Kind = PrimKind.Circle, X = cx, Y = cy, R = 1.6, Cover = Cover.Ruin, Label = Lab(labels, site + " · " + y.Label) });
                break;
            case YardKind.Pen:
                prims.Add(new Prim { Kind = PrimKind.Pen, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Label = Lab(labels, site + " · " + y.Label) });
                break;
        }
    }
}
