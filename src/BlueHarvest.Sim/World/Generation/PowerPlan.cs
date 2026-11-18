using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Power;

namespace BlueHarvest.Sim.World.Generation;

/// <summary>
/// Builds the island's power network for one run from what the generator already placed, without
/// changing a single tile: the power lines between facilities become feeders, the rooms each
/// programme names ("Backup generators", "Switch room", "UPS and batteries", "Battery room", control
/// rooms) get their equipment, the yard fuel tanks feed the generators, some roofs carry solar
/// panels, and every room and compound corner gets a lamp. A buried tie cable, normally open, joins
/// the nuclear plant's network to the geothermal plant's.
///
/// Ten years on, the run's seed decides what state it was all left in: breakers open or tripped,
/// lines down, generators ready, broken or seized, tanks part full, batteries flat. Then two rules
/// make it playable: every facility has at least two ways back to power (the grid and something of
/// its own, majors two of their own), and a few are still powered when the run starts.
/// </summary>
public static class PowerPlan
{
    /// <summary>What each kind of facility draws with everything on, kW.</summary>
    private static readonly Dictionary<string, int> LoadKw = new()
    {
        ["genetics"] = 120, ["control"] = 60, ["nuclear"] = 80, ["geothermal"] = 40, ["lab"] = 25, ["depot"] = 15,
        ["docks"] = 20, ["airstrip"] = 20, ["comms"] = 8, ["weather"] = 30, ["orgone"] = 15, ["raptors"] = 30,
        ["ley"] = 3, ["watch"] = 3,
    };

    /// <summary>Percent chance a kind of facility has solar panels on a roof: likelier the more remote.</summary>
    private static readonly Dictionary<string, int> SolarChance = new()
    {
        ["ley"] = 95, ["watch"] = 95, ["comms"] = 85, ["weather"] = 70, ["orgone"] = 70, ["raptors"] = 60, ["lab"] = 55,
        ["control"] = 45, ["airstrip"] = 40, ["docks"] = 40, ["depot"] = 40, ["genetics"] = 35,
    };

    /// <summary>Rooms where a generator stands, by name.</summary>
    private static readonly string[] GeneratorRooms = ["Backup generators", "Generator", "Generator room"];

    /// <summary>Where facilities without a generator room keep a standby or emergency diesel.</summary>
    private static readonly Dictionary<string, string> StandbyRoom = new()
    {
        ["docks"] = "Workshop", ["airstrip"] = "Workshop", ["depot"] = "Office", ["nuclear"] = "Cooling-water pumps", ["geothermal"] = "Workshop",
    };

    /// <summary>Where the switch panel goes, best first; failing these, the compound's first room.</summary>
    private static readonly string[] PanelRooms = ["Switch room", "Main control room", "Control room", "Network operations", "Array control", "Radio room", "Dispatch", "Operations office"];

    private static readonly string[] BatteryRooms = ["UPS and batteries", "Battery room"];

    // Which rooms have security or motorised doors, and at what keycard level, is in RoomRules.

    /// <summary>Kinds whose generator matters most: they get two ways back to power of their own.</summary>
    private static readonly HashSet<string> Majors = ["genetics", "control", "nuclear", "geothermal", "lab", "depot", "docks", "airstrip"];

    private sealed record RoomInfo(string Building, string Name, int X0, int Y0, int X1, int Y1)
    {
        public Footprint Footprint => new(X0, Y0, X1, Y1);
    }

    /// <summary>The island's power network as this run finds it.</summary>
    public static PowerGrid Build(IslandWorld world, ulong runSeed)
    {
        IslandWorks works = world.Works;
        ulong seed = new StateHasher().Add((ulong)world.Params.Seed).Add("power").Add(runSeed).Value;
        var grid = new PowerGrid(seed);
        int Roll(string what, string who) => (int)(new StateHasher().Add(seed).Add(what).Add(who).Value % 100);

        // ---- the facilities ----
        var byName = new Dictionary<string, PowerSite>();
        foreach (Site t in works.Sites)
        {
            if (byName.ContainsKey(t.Name))
                continue;
            int load = LoadKw.TryGetValue(t.Kind, out int kw) ? kw : 10;
            byName[t.Name] = grid.AddSite(t.Name, t.Kind, (int)Math.Round(t.X), (int)Math.Round(t.Y), t.R, load);
        }

        // ---- their rooms and yards, read back from the compounds' primitives ----
        var rooms = new Dictionary<int, List<RoomInfo>>();
        var fuelYards = new Dictionary<int, (Footprint Box, int Tanks)>();
        var buildings = new List<(Prim Building, int SiteId)>();
        foreach (Prim p in works.Prims.InOrder(works.CompoundPrims))
        {
            string[] parts = works.Labels[p.Label].Split(" · ");
            if (!byName.TryGetValue(parts[0], out PowerSite? site))
                continue;
            if (p.Kind == PrimKind.Building && p.Rooms is not null)
            {
                buildings.Add((p, site.Id));
                if (!rooms.TryGetValue(site.Id, out List<RoomInfo>? list))
                    rooms[site.Id] = list = new List<RoomInfo>();
                foreach (Room r in p.Rooms)
                {
                    string[] rp = works.Labels[r.Label].Split(" · ");
                    list.Add(new RoomInfo(rp.Length > 1 ? rp[1] : "", rp.Length > 2 ? rp[2] : rp[^1], r.X0, r.Y0, r.X1, r.Y1));
                }
            }
            else if (p.Kind == PrimKind.Circle && p.Cover == Cover.Tank && parts.Length > 1 && parts[1] == "Fuel tanks")
            {
                // a tank is a circle of radius 3; the box is the tanks plus a step round them, where you stand to work the valve
                var box = Footprint.Around((int)Math.Round(p.X), (int)Math.Round(p.Y), (int)Math.Ceiling(p.R) + 1);
                fuelYards[site.Id] = fuelYards.TryGetValue(site.Id, out var had)
                    ? (new Footprint(Math.Min(had.Box.X0, box.X0), Math.Min(had.Box.Y0, box.Y0), Math.Max(had.Box.X1, box.X1), Math.Max(had.Box.Y1, box.Y1)), had.Tanks + 1)
                    : (box, 1);
            }
        }

        // ---- equipment, facility by facility ----
        foreach (PowerSite site in grid.Sites)
        {
            List<RoomInfo> mine = rooms.TryGetValue(site.Id, out List<RoomInfo>? found) ? found : new List<RoomInfo>();
            RoomInfo? Named(params string[] names)
            {
                foreach (string n in names)
                    if (mine.Find(r => r.Name == n) is { } hit) return hit;
                return null;
            }
            // somewhere to stand even if a compound somehow has no rooms: a patch at its centre
            Footprint centre = Footprint.Around(site.X, site.Y, 2);

            RoomInfo? panelRoom = Named(PanelRooms) ?? (mine.Count > 0 ? mine[0] : null);
            grid.AddDevice(new Panel(grid.NextDeviceId, site.Id, "the switch panel", panelRoom?.Footprint ?? centre));

            if (site.Kind is "nuclear" or "geothermal")
            {
                RoomInfo? control = Named(site.Kind == "nuclear" ? "Main control room" : "Control room") ?? panelRoom;
                grid.AddDevice(new Plant(grid.NextDeviceId, site.Id, site.Kind == "nuclear" ? "the reactor controls" : "the turbine controls",
                    control?.Footprint ?? centre, nuclear: site.Kind == "nuclear"));
            }

            RoomInfo? genRoom = Named(GeneratorRooms) ?? (StandbyRoom.TryGetValue(site.Kind, out string? standby) ? Named(standby) : null);
            if (genRoom is not null)
            {
                int tankLitres = site.Kind switch { "genetics" => 400, "nuclear" => 600, "geothermal" => 300, "control" => 250, _ => 120 };
                grid.AddDevice(new Generator(grid.NextDeviceId, site.Id, "the backup generator", genRoom.Footprint,
                    kw: Math.Max(5, site.LoadKw * 12 / 10), tankCentilitres: tankLitres * 100));
                if (fuelYards.TryGetValue(site.Id, out var yard))
                    grid.AddDevice(new FuelStore(grid.NextDeviceId, site.Id, "the fuel tanks", yard.Box, capacityLitres: yard.Tanks * 6000));
            }

            RoomInfo? batteryRoom = Named(BatteryRooms);
            bool solar = SolarChance.TryGetValue(site.Kind, out int chance) && Roll("solar", site.Name) < chance;
            if (batteryRoom is not null || solar)
            {
                RoomInfo? room = batteryRoom ?? panelRoom;
                int wh = batteryRoom is not null ? (site.Kind == "genetics" ? 200_000 : 60_000) : site.LoadKw * 8000;
                grid.AddDevice(new BatteryBank(grid.NextDeviceId, site.Id, "the battery bank", room?.Footprint ?? centre, wh));
                if (solar)
                {
                    // small stations can run on their panels by day; big ones only top up their batteries
                    int share = site.LoadKw <= 10 ? 150 + Roll("solar-size", site.Name) : 30 + Roll("solar-size", site.Name) / 2;
                    grid.AddDevice(new SolarArray(grid.NextDeviceId, site.Id, "the solar charge controller", room?.Footprint ?? centre,
                        peakW: site.LoadKw * 1000 * share / 100));
                }
            }

            // lamps: a grid of ceiling lights in every room, a red emergency light in every other room,
            // floodlights at the compound's corners (most on their own panel and battery)
            for (int k = 0; k < mine.Count; k++)
                AddRoomLamps(grid, site.Id, mine[k], emergency: k % 2 == 0, inCompound: true);
            int c = Math.Max(4, site.Radius - 6);
            bool standalone = Roll("flood-solar", site.Name) < 65;
            int corner = 0;
            foreach ((int dx, int dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                PowerLamp flood = grid.AddLamp(site.Id, site.X + dx * c, site.Y + dy * c, heightDm: 60, radiusTiles: 14, intensityPercent: 75, LampKind.Flood);
                string floodKey = $"{site.Name}#{corner++}";
                if (standalone)
                    PowerGrid.SetStandalone(flood, panelW: 150, capacityWh: 1500, chargeWh: 300 + Roll("flood-charge", floodKey) * 12,
                        broken: Roll("flood-broken", floodKey) < 20);
            }
        }

        // ---- the lines: the generated power tree, the fences, and the tie between the two plants' networks ----
        var lines = new List<(Feeder Feeder, PowerLine Line)>();
        foreach (PowerLine l in works.Power)
        {
            if (!byName.TryGetValue(l.From, out PowerSite? from))
                continue;
            Feeder f = byName.TryGetValue(l.To, out PowerSite? to)
                ? grid.AddFeeder(from.Id, to.Id, to.Name)
                : grid.AddFeeder(from.Id, -1, l.To);
            lines.Add((f, l));
        }
        AddTie(grid);
        AddStations(grid, works, byName);
        AddDoors(grid, buildings, works.Labels);
        foreach (Paddock q in works.Paddocks)
        {
            Feeder? fence = grid.Feeders.FirstOrDefault(f => f.ToFence && f.ToName == q.Name);
            if (fence is null)
                continue;
            grid.AddFenceLine(fence.Id, q.Name, q.Poly.Select(v => (int)Math.Round(v)).ToArray());
        }

        // ---- how it was all left ----
        foreach ((Feeder f, PowerLine l) in lines)
        {
            string who = $"{l.From}>{l.To}";
            int b = Roll("breaker", who);
            bool down = !f.ToFence && Roll("down", who) < 20;
            PowerGrid.SetBreaker(f, b < 50 ? BreakerState.Closed : b < 80 ? BreakerState.Open : BreakerState.Tripped, down);
            if (down)
            {
                int mx = (int)Math.Round((l.X1 + l.X2) / 2), my = (int)Math.Round((l.Y1 + l.Y2) / 2);
                grid.AddDevice(new LineFault(grid.NextDeviceId, $"the downed line to the {f.ToName}", Footprint.Around(mx, my, 3), f.Id));
            }
        }
        foreach (PowerSite site in grid.Sites)
        {
            string who = site.Name;
            PowerGrid.SetBoard(site, on: Roll("board", who) < 55, autoBackup: Roll("auto", who) < 50);
            if (grid.PlantOf(site) is { } plant)
                PowerGrid.SetPlant(plant, plant.Nuclear ? PlantState.Shutdown : Roll("plant", who) < 35 ? PlantState.Running : PlantState.Tripped);
            if (grid.GeneratorOf(site) is { } gen)
            {
                int g = Roll("generator", who), fuel = Roll("day-tank", who);
                PowerGrid.SetGenerator(gen, g < 45 ? GeneratorState.Off : g < 85 ? GeneratorState.NeedsRepair : GeneratorState.Seized,
                    fuel < 30 ? 0 : gen.TankCentilitres * (fuel - 30) / 200);
            }
            if (grid.FuelOf(site) is { } tanks)
            {
                int stored = Roll("tanks", who);
                PowerGrid.SetFuel(tanks, stored < 25 ? 0 : tanks.CapacityLitres * (stored - 25) / 190, valveOpen: Roll("valve", who) < 40);
            }
            if (grid.BatteryOf(site) is { } battery)
                PowerGrid.SetBattery(battery, battery.CapacityWh * (Roll("charge", who) % 16) / 100, inverterOn: Roll("inverter", who) < 60);
            if (grid.SolarOf(site) is { } panels)
                PowerGrid.SetSolar(panels, 40 + Roll("condition", who) * 60 / 100, connected: Roll("connected", who) < 75);
        }

        EnsureWaysBack(grid, Roll);
        EnsureSomeStillRunning(grid, Roll);
        return grid;
    }

    /// <summary>
    /// Ceiling lights inside a room's walls, about one per 12 m each way, and (if asked) one dim red
    /// emergency light by the room's corner.
    /// </summary>
    private static void AddRoomLamps(PowerGrid grid, int siteId, RoomInfo r, bool emergency, bool inCompound)
    {
        int w = r.X1 - r.X0 - 1, h = r.Y1 - r.Y0 - 1;
        if (w < 1 || h < 1)
            return;
        int nx = Math.Max(1, (w + 6) / 12), ny = Math.Max(1, (h + 6) / 12);
        int radius = Math.Clamp(Math.Max(w / nx, h / ny) * 6 / 10 + 2, 4, 10);
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                grid.AddLamp(siteId, r.X0 + 1 + (2 * i + 1) * w / (2 * nx), r.Y0 + 1 + (2 * j + 1) * h / (2 * ny),
                    heightDm: 25, radiusTiles: radius, intensityPercent: 70, LampKind.Room, inCompound);
        if (emergency)
            grid.AddLamp(siteId, r.X0 + 1, r.Y0 + 1, heightDm: 22, radiusTiles: 5, intensityPercent: 30, LampKind.Emergency, inCompound);
    }

    /// <summary>
    /// The monorail stations' elevators and the cable cars' cabin halls, with their stations' lights.
    /// A monorail station runs on the power of the facility it serves; a cable car on its winch end's.
    /// </summary>
    private static void AddStations(PowerGrid grid, IslandWorks works, Dictionary<string, PowerSite> byName)
    {
        // where each stop is along its line, and what powers it, by "line · Station at …"
        var stops = new Dictionary<string, (TransportKind Kind, PowerSite Power, int AlongM, int ShaftM)>();
        if (works.Monorail is { } mono)
        {
            double along = 0;
            for (int k = 0; k < mono.Stations.Count; k++)
            {
                MonorailStation st = mono.Stations[k];
                if (k > 0)
                    along += Math.Sqrt((double)(st.X - mono.Stations[k - 1].X) * (st.X - mono.Stations[k - 1].X)
                        + (double)(st.Y - mono.Stations[k - 1].Y) * (st.Y - mono.Stations[k - 1].Y));
                if (byName.TryGetValue(st.Power, out PowerSite? power))
                    stops[$"{mono.Name} · Station at {st.Name}"] = (TransportKind.Monorail, power, (int)Math.Round(along), (int)Math.Round(Math.Max(0, st.Shaft)));
            }
        }
        foreach (Lift lift in works.Lifts)
        {
            if (!byName.TryGetValue(lift.From, out PowerSite? winch))
                continue;
            stops[$"{lift.Name} · Station at {lift.From}"] = (TransportKind.CableCar, winch, 0, 0);
            stops[$"{lift.Name} · Station at {lift.To}"] = (TransportKind.CableCar, winch, (int)Math.Round(lift.Km * 1000), 0);
        }

        foreach (Prim p in works.Prims.InOrder())
        {
            if (p.Kind != PrimKind.Building || p.Rooms is null || !stops.TryGetValue(works.Labels[p.Label], out var stop))
                continue;
            string[] parts = works.Labels[p.Label].Split(" · ");
            string line = parts[0], name = parts.Length > 1 ? parts[1] : parts[0];
            var stationRooms = p.Rooms.Select(r =>
            {
                string[] rp = works.Labels[r.Label].Split(" · ");
                return new RoomInfo(name, rp[^1], r.X0, r.Y0, r.X1, r.Y1);
            }).ToList();
            for (int k = 0; k < stationRooms.Count; k++)
                AddRoomLamps(grid, stop.Power.Id, stationRooms[k], emergency: k == 0, inCompound: false);
            RoomInfo? board = stationRooms.Find(r => r.Name == (stop.Kind == TransportKind.Monorail ? "Elevator" : "Cabin hall"));
            if (board is not null)
                grid.AddDevice(new Transport(grid.NextDeviceId, stop.Power.Id,
                    stop.Kind == TransportKind.Monorail ? "the elevator" : "the cable car", board.Footprint,
                    stop.Kind, line, name, stop.AlongM, stop.ShaftM));
            AddDoorsOf(grid, p, stop.Power.Id, stationRooms);
        }
    }

    /// <summary>The powered doors of the compounds' buildings.</summary>
    private static void AddDoors(PowerGrid grid, List<(Prim Building, int SiteId)> buildings, List<string> labels)
    {
        foreach ((Prim b, int siteId) in buildings)
        {
            if (b.Rooms is null)
                continue;
            var named = b.Rooms.Select(r => new RoomInfo("", labels[r.Label].Split(" · ")[^1], r.X0, r.Y0, r.X1, r.Y1)).ToList();
            AddDoorsOf(grid, b, siteId, named);
        }
    }

    /// <summary>
    /// A door is a security door if it opens onto a secure room, motorised if it opens onto an elevator,
    /// an airlock, a vehicle bay, a hangar floor, a boat bay or a cargo hall.
    /// </summary>
    private static void AddDoorsOf(PowerGrid grid, Prim building, int siteId, List<RoomInfo> rooms)
    {
        if (building.Doors is null)
            return;
        foreach ((int x, int y) in building.Doors)
        {
            var touching = rooms.Where(t => x >= t.X0 && x <= t.X1 && y >= t.Y0 && y <= t.Y1).Select(t => t.Name).ToList();
            string? secure = touching.FirstOrDefault(RoomRules.SecureRooms.Contains);
            if (secure is not null)
            {
                grid.AddDoor(siteId, x, y, DoorKind.SecurityLock, secure, RoomRules.Level3Rooms.Contains(secure) ? 3 : 2);
                continue;
            }
            string? motor = touching.FirstOrDefault(RoomRules.MotorRooms.Contains);
            if (motor is not null)
                grid.AddDoor(siteId, x, y, DoorKind.Motor, motor);
        }
    }

    /// <summary>
    /// The buried tie cable: between the closest pair of facilities on the two plants' networks, its
    /// breaker open. Closing it lets either plant feed the other's side.
    /// </summary>
    private static void AddTie(PowerGrid grid)
    {
        var plants = grid.Sites.Where(s => s.Kind is "nuclear" or "geothermal").ToList();
        if (plants.Count < 2)
            return;
        int[] network = new int[grid.Sites.Count];
        Array.Fill(network, -1);
        for (int k = 0; k < 2; k++)
        {
            var queue = new Queue<int>();
            queue.Enqueue(plants[k].Id);
            network[plants[k].Id] = k;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (Feeder f in grid.Feeders)
                {
                    if (f.ToFence) continue;
                    int next = f.From == at ? f.To : f.To == at ? f.From : -1;
                    if (next < 0 || network[next] >= 0) continue;
                    network[next] = k;
                    queue.Enqueue(next);
                }
            }
        }
        PowerSite? a = null, b = null;
        long best = long.MaxValue;
        foreach (PowerSite s in grid.Sites)
        {
            if (network[s.Id] != 0) continue;
            foreach (PowerSite t in grid.Sites)
            {
                if (network[t.Id] != 1) continue;
                long d = (long)(s.X - t.X) * (s.X - t.X) + (long)(s.Y - t.Y) * (s.Y - t.Y);
                if (d < best) { best = d; a = s; b = t; }
            }
        }
        if (a is null || b is null)
            return;
        PowerGrid.SetBreaker(grid.AddFeeder(a.Id, b.Id, b.Name, underground: true), BreakerState.Open);
    }

    /// <summary>How many ways back to power a facility has of its own (the grid is always one more).</summary>
    public static int OwnWaysBack(PowerGrid grid, PowerSite site)
    {
        int ways = 0;
        if (grid.GeneratorOf(site) is { } gen && gen.State != GeneratorState.Seized
            && (gen.FuelCentilitres > 0 || grid.FuelOf(site) is { Litres: > 0 }))
            ways++;
        BatteryBank? battery = grid.BatteryOf(site);
        if (grid.SolarOf(site) is { } solar && (battery is not null || solar.PeakW * solar.ConditionPercent / 100 >= site.LoadKw * 1000))
            ways++;
        else if (battery is not null && battery.ChargePercent >= 25)
            ways++;
        return ways;
    }

    /// <summary>
    /// Every facility gets at least one way back of its own besides the grid, and the majors two
    /// where they can: a seized generator becomes one that needs work, a dry one gets some fuel, and
    /// failing that the roof gets solar panels and a battery bank.
    /// </summary>
    private static void EnsureWaysBack(PowerGrid grid, Func<string, string, int> roll)
    {
        foreach (PowerSite site in grid.Sites)
        {
            int wanted = Majors.Contains(site.Kind) ? 2 : 1;
            if (OwnWaysBack(grid, site) >= wanted)
                continue;
            if (grid.GeneratorOf(site) is { } gen)
            {
                if (gen.State == GeneratorState.Seized)
                    PowerGrid.SetGenerator(gen, GeneratorState.NeedsRepair, gen.FuelCentilitres);
                if (gen.FuelCentilitres == 0 && grid.FuelOf(site) is not { Litres: > 0 })
                    PowerGrid.SetGenerator(gen, gen.State, gen.TankCentilitres * (15 + roll("top-up", site.Name) % 16) / 100);
            }
            if (OwnWaysBack(grid, site) >= wanted || grid.SolarOf(site) is not null)
                continue;
            // solar panels on the roof, worked from the battery room or the panel's room
            Footprint where = (grid.BatteryOf(site) ?? (PowerDevice)grid.Device<Panel>(site.PanelId)).Footprint;
            if (grid.BatteryOf(site) is null)
                grid.AddDevice(new BatteryBank(grid.NextDeviceId, site.Id, "the battery bank", where, site.LoadKw * 8000));
            int share = site.LoadKw <= 10 ? 180 : 50;
            SolarArray panels = grid.AddDevice(new SolarArray(grid.NextDeviceId, site.Id, "the solar charge controller", where, site.LoadKw * 1000 * share / 100));
            PowerGrid.SetSolar(panels, 60 + roll("condition", site.Name) * 40 / 100, connected: false);
        }
    }

    /// <summary>
    /// A few facilities still have power when the run starts, so there are lights in the dark and
    /// something to switch off: one keeps its backup generator going on automatic with fuel in the
    /// tank, and one solar station still runs on its panels and batteries.
    /// </summary>
    private static void EnsureSomeStillRunning(PowerGrid grid, Func<string, string, int> roll)
    {
        PowerSite? running = grid.Sites
            .Where(s => grid.PlantOf(s) is null && grid.GeneratorOf(s) is not null)
            .OrderBy(s => roll("still-running", s.Name)).ThenBy(s => s.Id)
            .FirstOrDefault();
        if (running is not null && grid.GeneratorOf(running) is { } gen)
        {
            PowerGrid.SetBoard(running, on: true, autoBackup: true);
            PowerGrid.SetGenerator(gen, GeneratorState.Off, gen.TankCentilitres * 6 / 10);
            if (grid.FuelOf(running) is { } tanks)
                PowerGrid.SetFuel(tanks, Math.Max(tanks.Litres, tanks.CapacityLitres / 5), valveOpen: true);
        }

        PowerSite? solar = grid.Sites
            .Where(s => s != running && grid.SolarOf(s) is not null && grid.BatteryOf(s) is not null)
            .OrderBy(s => roll("still-solar", s.Name)).ThenBy(s => s.Id)
            .FirstOrDefault();
        if (solar is not null)
        {
            PowerGrid.SetBoard(solar, on: true, autoBackup: solar.AutoBackup);
            SolarArray panels = grid.SolarOf(solar)!;
            PowerGrid.SetSolar(panels, Math.Max(80, panels.ConditionPercent), connected: true);
            BatteryBank battery = grid.BatteryOf(solar)!;
            PowerGrid.SetBattery(battery, battery.CapacityWh * 7 / 10, inverterOn: true);
        }
    }
}
