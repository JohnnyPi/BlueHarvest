using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Power;

/// <summary>
/// The island's power network: facilities, the lines between them, and every facility's own
/// equipment. It is island-wide (it doesn't move with the map window) and is stepped once a game
/// minute (<see cref="Step"/>): plants come up to speed, the grid reaches whatever the closed
/// breakers and standing lines connect to a running plant, generators burn fuel, solar follows the
/// sun and the cloud, batteries charge and run down, and storms trip breakers.
///
/// A facility has power when its main switch is on and one of these can carry its load: the grid,
/// its backup generator, its solar panels, or its battery bank (topped up by the solar). Generators
/// and batteries don't feed the grid back; only plants do. Everything is whole numbers, and every
/// roll is a hash of the seed, the thing and the minute, so the network replays exactly.
/// </summary>
public sealed class PowerGrid
{
    /// <summary>How much fuel a generator burns: 0.3 L per kWh, in centilitres per kW per game minute (×100).</summary>
    public const int BurnCentilitresPerKwMinuteX100 = 50;

    /// <summary>How fast the yard tanks top up a day tank while the valve is open, centilitres per game minute.</summary>
    public const int TransferCentilitresPerMinute = 2000;

    /// <summary>A storm trips an overhead line's breaker one game minute in this many.</summary>
    public const int StormTripOdds = 400;

    /// <summary>Batteries charge at a sixth of their capacity an hour from the grid or a generator.</summary>
    public const int ChargeHours = 6;

    // how long working each thing takes, in ticks
    public const int SwitchTicks = 300, ValveTicks = 500, StartTicks = 1000, RepairTicks = 6000, LineRepairTicks = 9000;

    private readonly List<PowerSite> _sites = new();
    private readonly List<PowerDevice> _devices = new();
    private readonly List<Feeder> _feeders = new();
    private readonly List<PowerLamp> _lamps = new();
    private readonly ulong _seed;

    public PowerGrid(ulong seed) => _seed = seed;

    public IReadOnlyList<PowerSite> Sites => _sites;
    public IReadOnlyList<PowerDevice> Devices => _devices;
    public IReadOnlyList<Feeder> Feeders => _feeders;
    public IReadOnlyList<PowerLamp> Lamps => _lamps;

    // ---------------- building the network (PowerPlan does this for the island) ----------------

    public PowerSite AddSite(string name, string kind, int x, int y, int radius, int loadKw)
    {
        var site = new PowerSite(_sites.Count, name, kind, x, y, radius, loadKw);
        _sites.Add(site);
        return site;
    }

    public Feeder AddFeeder(int from, int to, string toName, bool underground = false)
    {
        var feeder = new Feeder(_feeders.Count, from, to, toName, underground);
        _feeders.Add(feeder);
        return feeder;
    }

    /// <summary>Adds a device made with the next free id (see <see cref="NextDeviceId"/>) and links it to its site.</summary>
    public T AddDevice<T>(T device) where T : PowerDevice
    {
        if (device.Id != _devices.Count)
            throw new ArgumentException($"Device ids go in order: expected {_devices.Count}, got {device.Id}.");
        _devices.Add(device);
        if (device.SiteId >= 0)
        {
            PowerSite site = _sites[device.SiteId];
            switch (device)
            {
                case Panel: site.PanelId = device.Id; break;
                case Generator: site.GeneratorId = device.Id; break;
                case FuelStore: site.FuelId = device.Id; break;
                case BatteryBank: site.BatteryId = device.Id; break;
                case SolarArray: site.SolarId = device.Id; break;
                case Plant: site.PlantId = device.Id; break;
            }
        }
        if (device is LineFault fault)
            _feeders[fault.FeederId].FaultDevice = device.Id;
        return device;
    }

    public int NextDeviceId => _devices.Count;

    public PowerLamp AddLamp(int siteId, int x, int y, int heightDm, int radiusTiles, int intensityPercent)
    {
        var lamp = new PowerLamp(_lamps.Count, siteId, x, y, heightDm, radiusTiles, intensityPercent);
        _lamps.Add(lamp);
        return lamp;
    }

    // setters for the starting state, so generation and tests can set things up
    public static void SetBoard(PowerSite site, bool on, bool autoBackup) { site.BoardOn = on; site.AutoBackup = autoBackup; }
    public static void SetBreaker(Feeder feeder, BreakerState state, bool down = false) { feeder.Breaker = state; feeder.Down = down; }
    public static void SetGenerator(Generator g, GeneratorState state, int fuelCentilitres) { g.State = state; g.FuelCentilitres = Math.Clamp(fuelCentilitres, 0, g.TankCentilitres); }
    public static void SetFuel(FuelStore f, int litres, bool valveOpen) { f.Litres = Math.Clamp(litres, 0, f.CapacityLitres); f.ValveOpen = valveOpen; }
    public static void SetBattery(BatteryBank b, int chargeWh, bool inverterOn) { b.ChargeWh = Math.Clamp(chargeWh, 0, b.CapacityWh); b.InverterOn = inverterOn; }
    public static void SetSolar(SolarArray s, int conditionPercent, bool connected) { s.ConditionPercent = Math.Clamp(conditionPercent, 0, 100); s.Connected = connected; }
    public static void SetPlant(Plant p, PlantState state) { p.State = state; p.StartMinutesLeft = state == PlantState.Starting ? p.StartMinutes : 0; }

    public T Device<T>(int? id) where T : PowerDevice => (T)_devices[id ?? throw new ArgumentNullException(nameof(id))];

    public Generator? GeneratorOf(PowerSite site) => site.GeneratorId is { } id ? (Generator)_devices[id] : null;
    public FuelStore? FuelOf(PowerSite site) => site.FuelId is { } id ? (FuelStore)_devices[id] : null;
    public BatteryBank? BatteryOf(PowerSite site) => site.BatteryId is { } id ? (BatteryBank)_devices[id] : null;
    public SolarArray? SolarOf(PowerSite site) => site.SolarId is { } id ? (SolarArray)_devices[id] : null;
    public Plant? PlantOf(PowerSite site) => site.PlantId is { } id ? (Plant)_devices[id] : null;

    /// <summary>The lines whose breakers are at this site: the ones leaving it.</summary>
    public IEnumerable<Feeder> FeedersFrom(int siteId) => _feeders.Where(f => f.From == siteId);

    /// <summary>The lines arriving at this site from elsewhere.</summary>
    public IEnumerable<Feeder> FeedersTo(int siteId) => _feeders.Where(f => f.To == siteId);

    /// <summary>Whether a paddock fence line has power: its site's switchyard is live, and the line closed and standing.</summary>
    public bool FenceLive(Feeder fence) => fence.ToFence && fence.Carries && _sites[fence.From].BusLive;

    // ---------------- time passing ----------------

    /// <summary>
    /// Works out who has power now, without any time passing: after a switch is thrown, and at the
    /// start. Returns what changed since the last time.
    /// </summary>
    /// <param name="sunPercent">Daylight now, 0–100 (Sky.SunPercent).</param>
    /// <param name="weatherAt">The weather at a place (metres) now.</param>
    public List<PowerEvent> Settle(int sunPercent, Func<int, int, WeatherSettings> weatherAt)
    {
        var events = new List<PowerEvent>();
        Evaluate(sunPercent, weatherAt, events, consume: false, minute: 0);
        return events;
    }

    /// <summary>One game minute: plants, storms, fuel, batteries and the sun, then who has power. Returns what changed.</summary>
    public List<PowerEvent> Step(long minute, int sunPercent, Func<int, int, WeatherSettings> weatherAt)
    {
        var events = new List<PowerEvent>();

        // plants coming up to speed (they need their own site live to keep going)
        foreach (PowerSite site in _sites)
        {
            if (PlantOf(site) is not { State: PlantState.Starting } plant)
                continue;
            if (!site.BusLive)
            {
                plant.State = plant.Nuclear ? PlantState.Shutdown : PlantState.Tripped;
                plant.StartMinutesLeft = 0;
                events.Add(new PowerEvent(PowerEventKind.PlantStopped, site.Id, $"The {site.Name}'s start-up fails: it lost its own power."));
                continue;
            }
            if (--plant.StartMinutesLeft > 0)
                continue;
            plant.State = PlantState.Running;
            events.Add(new PowerEvent(PowerEventKind.PlantRunning, site.Id, $"The {site.Name} is online: it feeds the grid again."));
        }

        // storms trip overhead lines
        foreach (Feeder feeder in _feeders)
        {
            if (feeder.Underground || feeder.Breaker != BreakerState.Closed || feeder.Down)
                continue;
            PowerSite from = _sites[feeder.From];
            if (!weatherAt(from.X, from.Y).Storm)
                continue;
            if (Roll("trip", feeder.Id, minute) % StormTripOdds != 0)
                continue;
            feeder.Breaker = BreakerState.Tripped;
            events.Add(new PowerEvent(PowerEventKind.BreakerTripped, feeder.From,
                $"A breaker trips at the {from.Name}: the line to the {feeder.ToName} is off."));
        }

        Evaluate(sunPercent, weatherAt, events, consume: true, minute);
        return events;
    }

    /// <summary>
    /// Who has power, and (with <paramref name="consume"/>) a minute of fuel and charge used. The order
    /// is fixed (sites by id) so replays come out the same.
    /// </summary>
    private void Evaluate(int sunPercent, Func<int, int, WeatherSettings> weatherAt, List<PowerEvent> events, bool consume, long minute)
    {
        bool[] energised = Energise();
        foreach (PowerSite site in _sites)
        {
            bool grid = energised[site.Id];
            Generator? gen = GeneratorOf(site);
            FuelStore? fuel = FuelOf(site);
            BatteryBank? battery = BatteryOf(site);
            SolarArray? solar = SolarOf(site);

            // the transfer switch: start the backup when the grid has gone, stop it when it's back
            if (gen is not null && site.AutoBackup)
            {
                if (!grid && gen.State == GeneratorState.Off && gen.FuelCentilitres > 0)
                {
                    gen.State = GeneratorState.Running;
                    gen.AutoStarted = true;
                    events.Add(new PowerEvent(PowerEventKind.GeneratorStarted, site.Id, $"The backup generator at the {site.Name} starts by itself."));
                }
                else if (grid && gen.State == GeneratorState.Running && gen.AutoStarted)
                {
                    gen.State = GeneratorState.Off;
                    gen.AutoStarted = false;
                    events.Add(new PowerEvent(PowerEventKind.GeneratorStopped, site.Id, $"The grid is back at the {site.Name}: its backup generator stops."));
                }
            }

            // solar now
            if (solar is not null)
            {
                int cloud = weatherAt(site.X, site.Y).CloudPercent;
                solar.OutputW = solar.Connected
                    ? (int)((long)solar.PeakW * sunPercent / 100 * solar.ConditionPercent / 100 * (100 - cloud * 7 / 10) / 100)
                    : 0;
            }
            int solarW = solar?.OutputW ?? 0;
            int loadW = site.LoadKw * 1000;
            bool genRunning = gen is { State: GeneratorState.Running };
            bool batteryReady = battery is { InverterOn: true, ChargeWh: > 0 };

            // what carries the load (or would, if the main switch were on)
            PowerSource source = grid ? PowerSource.Grid
                : genRunning ? PowerSource.Generator
                : solarW >= loadW ? PowerSource.Solar
                : batteryReady && battery!.ChargeWh * 60L >= loadW - solarW ? PowerSource.Battery
                : PowerSource.None;

            if (consume)
            {
                // the yard tanks top up the day tank
                if (gen is not null && fuel is { ValveOpen: true, Litres: > 0 } && gen.FuelCentilitres < gen.TankCentilitres)
                {
                    int move = Math.Min(Math.Min(TransferCentilitresPerMinute, gen.TankCentilitres - gen.FuelCentilitres), fuel.Litres * 100);
                    gen.FuelCentilitres += move;
                    fuel.Litres -= (move + 99) / 100;
                }
                // a running generator burns fuel: the load if the site is on, a quarter of its rating idling
                if (gen is { State: GeneratorState.Running })
                {
                    int kw = site.BoardOn && source == PowerSource.Generator ? Math.Max(site.LoadKw, gen.Kw / 4) : gen.Kw / 4;
                    int burn = Math.Max(1, kw * BurnCentilitresPerKwMinuteX100 / 100);
                    gen.FuelCentilitres = Math.Max(0, gen.FuelCentilitres - burn);
                    if (gen.FuelCentilitres == 0)
                    {
                        gen.State = GeneratorState.Off;
                        gen.AutoStarted = false;
                        events.Add(new PowerEvent(PowerEventKind.GeneratorRanDry, site.Id, $"The backup generator at the {site.Name} coughs and dies: its day tank is dry."));
                    }
                }
                // batteries: charged by the grid or a generator, or by spare solar; run down when they carry the load
                if (battery is not null)
                {
                    int drawnW = site.BoardOn ? loadW : 0;
                    if (source is PowerSource.Grid or PowerSource.Generator)
                        battery.ChargeWh = Math.Min(battery.CapacityWh, battery.ChargeWh + battery.CapacityWh / (ChargeHours * 60));
                    else if (solarW > drawnW)
                        battery.ChargeWh = (int)Math.Min(battery.CapacityWh, battery.ChargeWh + (long)(solarW - drawnW) / 60);
                    else if (source == PowerSource.Battery && drawnW > 0)
                    {
                        battery.ChargeWh = (int)Math.Max(0, battery.ChargeWh - (long)(drawnW - solarW + 59) / 60);
                        if (battery.ChargeWh == 0)
                            events.Add(new PowerEvent(PowerEventKind.BatteryFlat, site.Id, $"The battery bank at the {site.Name} is flat."));
                    }
                }
                // after the minute, the generator may have stopped and the battery emptied
                genRunning = gen is { State: GeneratorState.Running };
                batteryReady = battery is { InverterOn: true, ChargeWh: > 0 };
                source = grid ? PowerSource.Grid
                    : genRunning ? PowerSource.Generator
                    : solarW >= loadW ? PowerSource.Solar
                    : batteryReady && battery!.ChargeWh * 60L >= loadW - solarW ? PowerSource.Battery
                    : PowerSource.None;
            }

            bool powered = site.BoardOn && source != PowerSource.None;
            site.Energised = grid;
            site.BusLive = source != PowerSource.None;
            site.Source = source;
            if (powered != site.Powered)
            {
                site.Powered = powered;
                events.Add(powered
                    ? new PowerEvent(PowerEventKind.SiteLit, site.Id, $"The lights come on at the {site.Name}.")
                    : new PowerEvent(PowerEventKind.SiteDark, site.Id, $"The {site.Name} goes dark."));
            }
        }
    }

    /// <summary>Which sites the grid reaches: everything connected to a running plant through closed breakers and standing lines.</summary>
    private bool[] Energise()
    {
        var on = new bool[_sites.Count];
        var queue = new Queue<int>();
        foreach (PowerSite site in _sites)
        {
            if (PlantOf(site) is { State: PlantState.Running })
            {
                on[site.Id] = true;
                queue.Enqueue(site.Id);
            }
        }
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (Feeder f in _feeders)
            {
                if (!f.Carries || f.ToFence)
                    continue;
                int next = f.From == at ? f.To : f.To == at ? f.From : -1;
                if (next < 0 || on[next])
                    continue;
                on[next] = true;
                queue.Enqueue(next);
            }
        }
        return on;
    }

    // ---------------- working the equipment ----------------

    /// <summary>What can be done at a device now, for the right-click menu.</summary>
    public IEnumerable<PowerChoice> Choices(int deviceId)
    {
        PowerDevice device = _devices[deviceId];
        PowerSite? site = device.SiteId >= 0 ? _sites[device.SiteId] : null;
        switch (device)
        {
            case Panel when site is not null:
                yield return site.BoardOn
                    ? new PowerChoice(PowerOp.SiteOff, -1, $"Switch the {site.Name} off", site.Powered ? "the lights go out" : null)
                    : new PowerChoice(PowerOp.SiteOn, -1, $"Switch the {site.Name} on", site.BusLive ? null : "nothing to run it on yet");
                int here = site.Id;
                foreach (Feeder f in _feeders.Where(t => t.From == here || (t.Underground && t.To == here)))
                {
                    string line = f.Underground ? $"the tie cable to the {(f.From == here ? f.ToName : _sites[f.From].Name)}" : $"the line to the {f.ToName}";
                    yield return f.Breaker == BreakerState.Closed
                        ? new PowerChoice(PowerOp.OpenLine, f.Id, $"Open {line}", f.Down ? "it's down somewhere anyway" : null)
                        : new PowerChoice(PowerOp.CloseLine, f.Id, $"{(f.Breaker == BreakerState.Tripped ? "Reset" : "Close")} {line}", f.Down ? "it's down somewhere" : null);
                }
                if (site.GeneratorId is not null)
                    yield return site.AutoBackup
                        ? new PowerChoice(PowerOp.BackupManual, -1, "Set the backup to manual", "it won't start by itself")
                        : new PowerChoice(PowerOp.BackupAuto, -1, "Set the backup to automatic", "it starts when the grid fails");
                break;
            case Generator g:
                switch (g.State)
                {
                    case GeneratorState.Running:
                        yield return new PowerChoice(PowerOp.Stop, -1, "Stop the generator", $"{Litres(g.FuelCentilitres)} L left");
                        break;
                    case GeneratorState.Off:
                        yield return new PowerChoice(PowerOp.Start, -1, "Start the generator",
                            g.FuelCentilitres > 0 ? $"{Litres(g.FuelCentilitres)} L in the day tank" : "the day tank is dry", g.FuelCentilitres > 0);
                        break;
                    case GeneratorState.NeedsRepair:
                        yield return new PowerChoice(PowerOp.Repair, -1, "Work on the generator", $"{Seconds(RepairTicks)} s, it may not take");
                        break;
                    case GeneratorState.Seized:
                        yield return new PowerChoice(PowerOp.Repair, -1, "The generator is seized solid", "it needs parts", false);
                        break;
                }
                break;
            case FuelStore f:
                yield return f.ValveOpen
                    ? new PowerChoice(PowerOp.CloseValve, -1, "Close the fuel valve", $"{f.Litres} L in the tanks")
                    : new PowerChoice(PowerOp.OpenValve, -1, "Open the fuel valve", f.Litres > 0 ? $"{f.Litres} L in the tanks" : "the tanks are empty");
                break;
            case BatteryBank b:
                yield return b.InverterOn
                    ? new PowerChoice(PowerOp.InverterOff, -1, "Switch the battery inverter off", $"{b.ChargePercent}% charged")
                    : new PowerChoice(PowerOp.InverterOn, -1, "Switch the battery inverter on", $"{b.ChargePercent}% charged");
                break;
            case SolarArray s:
                yield return s.Connected
                    ? new PowerChoice(PowerOp.Disconnect, -1, "Disconnect the solar panels", $"{s.OutputW / 1000.0:0.0} kW now")
                    : new PowerChoice(PowerOp.Connect, -1, "Connect the solar panels", $"{s.ConditionPercent}% of rated output");
                break;
            case Plant p when site is not null:
                if (p.State is PlantState.Running or PlantState.Starting)
                    yield return new PowerChoice(PowerOp.Shutdown, -1, p.Nuclear ? "Shut the reactor down" : "Trip the turbine",
                        p.State == PlantState.Starting ? $"{p.StartMinutesLeft} min to go" : "the grid goes down with it");
                else
                    yield return new PowerChoice(PowerOp.Restart, -1, p.Nuclear ? "Begin the reactor start-up" : "Restart the turbine",
                        site.BusLive ? $"{p.StartMinutes} game min" : "needs power on site to start", site.BusLive);
                break;
            case LineFault lf:
                yield return new PowerChoice(PowerOp.Repair, -1, "Repair the downed line", $"{Seconds(LineRepairTicks)} s, it may not take", _feeders[lf.FeederId].Down);
                break;
        }
    }

    /// <summary>
    /// Works a device. <paramref name="roll"/> (0–99) decides repairs: a better
    /// <paramref name="electronics"/> skill makes them likelier to take.
    /// </summary>
    public PowerResult Operate(int deviceId, PowerOp op, int arg, int electronics, int roll)
    {
        if (deviceId < 0 || deviceId >= _devices.Count)
            return new PowerResult(false, "There's nothing like that here.", 0);
        PowerDevice device = _devices[deviceId];
        PowerSite? site = device.SiteId >= 0 ? _sites[device.SiteId] : null;
        const string Switch = "a heavy switch";
        switch (device, op)
        {
            case (Panel, PowerOp.SiteOn) when site is not null:
                site.BoardOn = true;
                return new PowerResult(true, site.BusLive ? "You throw the main switch: power to the buildings." : "You throw the main switch. Nothing: there's no power to give.", SwitchTicks, 25, Switch);
            case (Panel, PowerOp.SiteOff) when site is not null:
                site.BoardOn = false;
                return new PowerResult(true, "You pull the main switch: the buildings lose power.", SwitchTicks, 25, Switch);
            case (Panel, PowerOp.CloseLine or PowerOp.OpenLine) when site is not null:
            {
                if (arg < 0 || arg >= _feeders.Count || !(_feeders[arg].From == site.Id || (_feeders[arg].Underground && _feeders[arg].To == site.Id)))
                    return new PowerResult(false, "That breaker isn't on this panel.", 0);
                Feeder f = _feeders[arg];
                f.Breaker = op == PowerOp.CloseLine ? BreakerState.Closed : BreakerState.Open;
                string to = f.From == site.Id ? f.ToName : _sites[f.From].Name;
                return new PowerResult(true, op == PowerOp.CloseLine
                    ? $"The breaker for the {to} clunks shut{(f.Down ? ", but that line is down somewhere" : "")}."
                    : $"You open the breaker for the {to}.", SwitchTicks, 25, Switch);
            }
            case (Panel, PowerOp.BackupAuto or PowerOp.BackupManual) when site is not null:
                site.AutoBackup = op == PowerOp.BackupAuto;
                return new PowerResult(true, site.AutoBackup ? "The transfer switch is on automatic: the generator will start if the grid fails." : "The transfer switch is on manual.", SwitchTicks, 10, Switch);
            case (Generator g, PowerOp.Start):
                if (g.State == GeneratorState.Running) return new PowerResult(false, "It's already running.", 0);
                if (g.State == GeneratorState.Seized) return new PowerResult(false, "The generator is seized solid. It needs parts.", SwitchTicks);
                if (g.State == GeneratorState.NeedsRepair) return new PowerResult(false, "It turns over and won't catch. It needs work first.", StartTicks, 45, "an engine turning over");
                if (g.FuelCentilitres == 0) return new PowerResult(false, "It turns over and dies: the day tank is dry.", StartTicks, 45, "an engine turning over");
                g.State = GeneratorState.Running;
                g.AutoStarted = false;
                return new PowerResult(true, "The generator coughs, catches and settles into a roar.", StartTicks, 55, "a generator starting");
            case (Generator g, PowerOp.Stop):
                if (g.State != GeneratorState.Running) return new PowerResult(false, "It isn't running.", 0);
                g.State = GeneratorState.Off;
                g.AutoStarted = false;
                return new PowerResult(true, "The generator winds down and stops.", SwitchTicks, 20, Switch);
            case (Generator g, PowerOp.Repair):
                if (g.State == GeneratorState.Seized) return new PowerResult(false, "Seized solid. Nothing you can do without parts.", SwitchTicks);
                if (g.State != GeneratorState.NeedsRepair) return new PowerResult(false, "There's nothing wrong with it.", 0);
                if (roll < RepairChance(electronics))
                {
                    g.State = GeneratorState.Off;
                    return new PowerResult(true, "You clean the injectors and free the starter. It should run now.", RepairTicks, 30, "clanking tools");
                }
                return new PowerResult(false, "You work on it a long while. It still won't catch; it might another time.", RepairTicks, 30, "clanking tools");
            case (FuelStore f, PowerOp.OpenValve or PowerOp.CloseValve):
                f.ValveOpen = op == PowerOp.OpenValve;
                return new PowerResult(true, f.ValveOpen
                    ? (f.Litres > 0 ? $"You open the valve: fuel runs to the generator ({f.Litres} L in the tanks)." : "You open the valve. The tanks are empty.")
                    : "You close the fuel valve.", ValveTicks, 10, "a squealing valve");
            case (BatteryBank b, PowerOp.InverterOn or PowerOp.InverterOff):
                b.InverterOn = op == PowerOp.InverterOn;
                return new PowerResult(true, b.InverterOn ? $"The inverter hums into life. The batteries are at {b.ChargePercent}%." : "You switch the inverter off.", SwitchTicks, 10, Switch);
            case (SolarArray s, PowerOp.Connect or PowerOp.Disconnect):
                s.Connected = op == PowerOp.Connect;
                return new PowerResult(true, s.Connected ? "You connect the solar panels." : "You disconnect the solar panels.", SwitchTicks, 10, Switch);
            case (Plant p, PowerOp.Restart) when site is not null:
                if (p.State is PlantState.Running or PlantState.Starting) return new PowerResult(false, "It's already running.", 0);
                if (!site.BusLive) return new PowerResult(false, "The start-up needs power here first: get the backup generator running.", SwitchTicks);
                p.State = PlantState.Starting;
                p.StartMinutesLeft = p.StartMinutes;
                return new PowerResult(true, p.Nuclear
                    ? $"You begin the start-up sequence. The reactor will take about {p.StartMinutes} game minutes to come up."
                    : $"The turbine begins to spin up. It will feed the grid in about {p.StartMinutes} game minutes.", StartTicks, 30, "machinery spinning up");
            case (Plant p, PowerOp.Shutdown):
                if (p.State is PlantState.Tripped or PlantState.Shutdown) return new PowerResult(false, "It isn't running.", 0);
                p.State = p.Nuclear ? PlantState.Shutdown : PlantState.Tripped;
                p.StartMinutesLeft = 0;
                return new PowerResult(true, p.Nuclear ? "The control rods drop. The reactor shuts down." : "The turbine trips and spins down.", SwitchTicks, 30, "machinery spinning down");
            case (LineFault lf, PowerOp.Repair):
            {
                Feeder f = _feeders[lf.FeederId];
                if (!f.Down) return new PowerResult(false, "The line is back up already.", 0);
                if (roll < RepairChance(electronics))
                {
                    f.Down = false;
                    return new PowerResult(true, "You splice the broken conductor and rehang it. The line should carry now.", LineRepairTicks, 30, "clanking tools");
                }
                return new PowerResult(false, "You struggle with the broken line and can't get it to hold. Try again.", LineRepairTicks, 30, "clanking tools");
            }
        }
        return new PowerResult(false, $"You can't do that with {device.Name}.", 0);
    }

    /// <summary>Percent chance a repair takes: 45 untrained, 70 with some electronics, 90 for an expert.</summary>
    public static int RepairChance(int electronics) => Math.Min(90, 45 + 25 * Math.Max(0, electronics));

    // ---------------- describing it ----------------

    /// <summary>One line on a device, for the tooltip: "Backup generator: running, 42 L left".</summary>
    public string Describe(int deviceId)
    {
        PowerDevice device = _devices[deviceId];
        switch (device)
        {
            case Panel when device.SiteId >= 0:
            {
                PowerSite s = _sites[device.SiteId];
                return $"Switch panel: site {(s.BoardOn ? "on" : "off")}{(s.GeneratorId is null ? "" : s.AutoBackup ? ", backup automatic" : ", backup manual")}";
            }
            case Generator g:
                return "Backup generator: " + g.State switch
                {
                    GeneratorState.Running => $"running, {Litres(g.FuelCentilitres)} L left",
                    GeneratorState.Off => g.FuelCentilitres > 0 ? $"stopped, {Litres(g.FuelCentilitres)} L in the day tank" : "stopped, day tank dry",
                    GeneratorState.NeedsRepair => "won't start, needs work",
                    _ => "seized",
                };
            case FuelStore f:
                return $"Fuel tanks: {f.Litres} L, valve {(f.ValveOpen ? "open" : "closed")}";
            case BatteryBank b:
                return $"Battery bank: {b.ChargePercent}%, inverter {(b.InverterOn ? "on" : "off")}";
            case SolarArray s:
                return s.Connected ? $"Solar panels: {s.OutputW / 1000.0:0.0} kW now ({s.ConditionPercent}% condition)" : "Solar panels: disconnected";
            case Plant p:
                return (p.Nuclear ? "Reactor: " : "Turbine: ") + p.State switch
                {
                    PlantState.Running => "running",
                    PlantState.Starting => $"starting, {p.StartMinutesLeft} min to go",
                    PlantState.Tripped => "tripped",
                    _ => "in safe shutdown",
                };
            case LineFault lf:
                return _feeders[lf.FeederId].Down ? "A downed power line" : "A repaired power line";
        }
        return device.Name;
    }

    /// <summary>A facility's state in a few words: "lit, on the backup generator (5 h of fuel)".</summary>
    public string DescribeSite(PowerSite site)
    {
        if (!site.BoardOn)
            return site.BusLive ? "switched off (power is there)" : "switched off, no power";
        if (!site.Powered)
            return "dark";
        return site.Source switch
        {
            PowerSource.Grid => "lit, on the grid",
            PowerSource.Generator when GeneratorOf(site) is { } g =>
                $"lit, on the backup generator ({HoursLeft(g.FuelCentilitres, Math.Max(site.LoadKw, g.Kw / 4))} of fuel)",
            PowerSource.Solar => "lit, on solar",
            PowerSource.Battery when BatteryOf(site) is { } b => $"lit, on batteries ({b.ChargePercent}%)",
            _ => "lit",
        };
    }

    private static string HoursLeft(int centilitres, int kw)
    {
        int perMinute = Math.Max(1, kw * BurnCentilitresPerKwMinuteX100 / 100);
        int minutes = centilitres / perMinute;
        return minutes >= 120 ? $"{minutes / 60} h" : $"{minutes} min";
    }

    private static int Litres(int centilitres) => centilitres / 100;

    private static int Seconds(int ticks) => ticks / 100;

    /// <summary>A roll for one thing at one minute, from the seed: the same every replay.</summary>
    private ulong Roll(string stream, int id, long minute) => new StateHasher().Add(_seed).Add(stream).Add(id).Add(minute).Value;

    /// <summary>Fingerprint of everything that changes, for the simulation's state hash.</summary>
    public void AddTo(StateHasher hasher)
    {
        foreach (PowerSite s in _sites)
            hasher.Add(s.BoardOn ? 1 : 0).Add(s.AutoBackup ? 1 : 0).Add(s.Powered ? 1 : 0).Add((int)s.Source);
        foreach (Feeder f in _feeders)
            hasher.Add((int)f.Breaker).Add(f.Down ? 1 : 0);
        foreach (PowerDevice d in _devices)
        {
            switch (d)
            {
                case Generator g: hasher.Add((int)g.State).Add(g.FuelCentilitres).Add(g.AutoStarted ? 1 : 0); break;
                case FuelStore f: hasher.Add(f.Litres).Add(f.ValveOpen ? 1 : 0); break;
                case BatteryBank b: hasher.Add(b.ChargeWh).Add(b.InverterOn ? 1 : 0); break;
                case SolarArray s: hasher.Add(s.Connected ? 1 : 0).Add(s.OutputW); break;
                case Plant p: hasher.Add((int)p.State).Add(p.StartMinutesLeft); break;
            }
        }
    }
}
