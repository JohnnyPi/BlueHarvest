using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>A tile on fire: how hot it is burning and how much it has left to burn.</summary>
public sealed class Fire
{
    internal Fire(TilePos position, int intensity, int fuelSteps, int maxIntensity)
    {
        Position = position;
        Intensity = intensity;
        FuelSteps = fuelSteps;
        MaxIntensity = maxIntensity;
    }

    public TilePos Position { get; internal set; }

    /// <summary>How hot it is burning, 1–100.</summary>
    public int Intensity { get; internal set; }

    /// <summary>Fire steps of fuel left; at 0 it dies down.</summary>
    public int FuelSteps { get; internal set; }

    public int MaxIntensity { get; }

    /// <summary>Fire steps it has burnt so far: a fire put out at once leaves the ground as it was.</summary>
    public int StepsBurnt { get; internal set; }
}

/// <summary>
/// Fire, in action time. A fire step every 2 s: burning tiles grow while they have fuel and die down
/// after, rain damps them, and each passes heat to its neighbours (more downwind and uphill), which
/// catch if heat plus how burnable they are passes the threshold, give or take a seeded roll. A
/// burnt-out tile becomes what its material leaves (burnt ground, mud, rubble). Fires give light and
/// smoke, burn whoever stands in them, and frighten animals. Sources: flares, lightning, sparking
/// fences, burning vehicles and the yard fuel tanks.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>How many of the hottest fires give light: enough to light a burning hillside, few enough to keep the light grid quick.</summary>
    public const int FireLightCap = 40;

    /// <summary>Standing in fire burns you at most this often.</summary>
    public const int BurnRepeatTicks = 300;

    /// <summary>How close animals let a fire come: a big body keeps well back, a small one less so; people step out of it.</summary>
    public const int FireFearLarge = 10, FireFearSmall = 5, FireFearHuman = 2;

    /// <summary>Yard fuel tanks heated by a fire for this many steps (30 s) go up.</summary>
    public const int TankBurstSteps = 15;

    /// <summary>The roar of tanks going up, and the crackle of a fire at its hottest.</summary>
    public const int TankBurstLoudness = 70, FireLoudness = 40;

    private readonly Dictionary<TilePos, Fire> _fires = new();
    private readonly Dictionary<TilePos, long> _fuel = new();
    private readonly List<(TilePos Tile, long OffTick)> _groundFlares = new();
    private readonly HashSet<int> _burntVehicles = new();
    private readonly Dictionary<int, long> _lastBurnTick = new();
    private readonly Dictionary<int, int> _tankHeat = new();
    private readonly List<int> _fireLights = new();
    private long _fireTick;
    private bool _smokeOnMap;
    private long _recentRainHour = -1;
    private int _recentRainPercent;

    /// <summary>The tiles burning now.</summary>
    public IReadOnlyCollection<Fire> Fires => _fires.Values;

    /// <summary>Where flares lie burning on the ground now (thrown or held to something).</summary>
    public IEnumerable<TilePos> GroundFlares => _groundFlares.Where(f => f.OffTick > CurrentTick).Select(f => f.Tile);

    /// <summary>How many tiles have burnt out so far.</summary>
    public int TilesBurnt { get; private set; }

    /// <summary>A fire took hold from a source (not by spreading): where, and from what.</summary>
    public event Action<TilePos, string>? FireStarted;

    /// <summary>The last fire on the map went out.</summary>
    public event Action? FiresOut;

    /// <summary>Someone was burnt.</summary>
    public event Action<Actor, Severity>? Burned;

    /// <summary>Someone held a flare to a tile: whether it caught.</summary>
    public event Action<Actor, TilePos, bool>? Kindled;

    /// <summary>A facility's fuel tanks went up.</summary>
    public event Action<TilePos>? TanksBurst;

    /// <summary>Lightning hit the ground on the map.</summary>
    public event Action<TilePos>? LightningHitGround;

    /// <summary>The fire on a tile, if it is burning.</summary>
    public Fire? FireAt(TilePos p) => _fires.TryGetValue(p, out Fire? fire) ? fire : null;

    /// <summary>Whether fuel is spilled on a tile.</summary>
    public bool FuelAt(TilePos p) => _fuel.TryGetValue(p, out long until) && until > CurrentTick;

    /// <summary>
    /// How much rain there has been lately, 0–100: the rain now, or the last six game hours' if that
    /// was wetter (the latest hour weighs most). Without a weather source, only the rain now.
    /// </summary>
    public int RecentRainPercent
    {
        get
        {
            int now = Weather.Settings.RainPercent;
            if (WeatherSource is not { } source)
                return now;
            long hour = GameMinute(CurrentTick) / 60;
            if (hour != _recentRainHour)
            {
                _recentRainHour = hour;
                var (x, y) = WeatherPlace;
                long sum = 0, weights = 0;
                for (int back = 1; back <= 6; back++)
                {
                    long h = hour - back;
                    int weight = 7 - back;
                    if (h >= 0)
                        sum += (long)weight * source.At(x, y, h * 60 + 30).RainPercent;
                    weights += weight;
                }
                _recentRainPercent = (int)(sum / weights);
            }
            return Math.Max(now, _recentRainPercent);
        }
    }

    /// <summary>How wet a tile is, 0–100: its climate, recent rain, and water beside it. Indoors is dry.</summary>
    public int WetnessAt(TilePos p)
    {
        if (Map.IsIndoor(p))
            return 0;
        int wet = Map.Damp[p] / 2 + RecentRainPercent;
        foreach (Direction d in Directions.Cardinal)
        {
            TilePos n = p.Offset(d);
            if (Map.InBounds(n) && Cover[n] is CoverType.ShallowWater or CoverType.DeepWater)
            {
                wet += FireRules.WaterSideDamp;
                break;
            }
        }
        return Math.Min(100, wet);
    }

    /// <summary>
    /// How readily a tile would catch now, 0–100: its material's burnable value, less what it has
    /// soaked up; at least <see cref="FireRules.FuelBurnable"/> with fuel on it. 0 if it's burning already.
    /// </summary>
    public int BurnableAt(TilePos p)
    {
        if (_fires.ContainsKey(p))
            return 0;
        CoverType cover = Cover[p];
        if (!FireRules.CanBurn(cover))
            return 0;
        int burnable = cover == CoverType.Vehicle && (VehicleAt(p) is not { } v || _burntVehicles.Contains(v.Id))
            ? 0
            : FireRules.Of(cover).Burnable;
        if (FuelAt(p))
            return Math.Max(FireRules.FuelBurnable, burnable);
        if (burnable == 0)
            return 0;
        return Math.Max(0, burnable - WetnessAt(p) * FireRules.Of(cover).SoakPercent / 100);
    }

    /// <summary>Spills fuel on a tile: anything there will burn hot for a while, until it evaporates.</summary>
    public void SpillFuel(TilePos p)
    {
        if (Map.InBounds(p) && FireRules.CanBurn(Cover[p]))
            _fuel[p] = CurrentTick + FireRules.FuelLastsTicks;
    }

    /// <summary>
    /// Heat of <paramref name="heat"/> on a tile: it catches if heat plus how burnable it is passes the
    /// threshold, give or take a roll seeded by the place and the tick. <paramref name="cause"/> names
    /// the source (for the log); null when a fire spreads.
    /// </summary>
    public bool TryIgnite(TilePos p, int heat, string? cause) => TryIgnite(p, heat, cause, CurrentTick);

    private bool TryIgnite(TilePos p, int heat, string? cause, long tick)
    {
        if (!Map.InBounds(p) || _fires.ContainsKey(p))
            return false;
        int burnable = BurnableAt(p);
        if (burnable <= 0)
            return false;
        int roll = Rng.Fork($"ignite:{p.X + WindowOrigin.X}:{p.Y + WindowOrigin.Y}:{tick}").NextInt(2 * FireRules.IgnitionRoll + 1)
            - FireRules.IgnitionRoll;
        if (heat + burnable < FireRules.IgnitionThreshold + roll)
            return false;
        Kindle(p, cause);
        return true;
    }

    /// <summary>Sets a tile burning.</summary>
    private void Kindle(TilePos p, string? cause)
    {
        CoverType cover = Cover[p];
        Material m = FireRules.Of(cover);
        bool fuelled = FuelAt(p);
        // a vehicle's tank goes with it: over the parts not burning yet (what's alight already has it)
        if (cover == CoverType.Vehicle && VehicleAt(p) is { } vehicle)
        {
            foreach (TilePos t in vehicle.Tiles)
                if (Map.InBounds(t) && t != p && !_fires.ContainsKey(t))
                    _fuel[t] = CurrentTick + FireRules.FuelLastsTicks;
        }
        var fire = new Fire(p,
            fuelled ? 100 : FireRules.StartIntensity,
            (fuelled && m.Burnable == 0 ? 0 : m.FuelSteps) + (fuelled ? FireRules.FuelSteps : 0),
            fuelled ? 100 : m.MaxIntensity);
        _fires[p] = fire;
        _fuel.Remove(p);
        if (cause is not null)
            FireStarted?.Invoke(p, cause);
    }

    /// <summary>
    /// Lights a fire with a flare held to a tile next to you (or your own): it uses up a flare, which
    /// then burns there like a thrown one.
    /// </summary>
    private int Ignite(Actor actor, Direction? direction)
    {
        if (!actor.Inventory.Has(ItemKind.Flare))
            return Bump(actor, "no flares left");
        TilePos tile = direction is { } d ? actor.Position.Offset(d) : actor.Position;
        if (!Map.InBounds(tile) || Map.CoverAt(tile).BlocksMovement && Cover[tile] != CoverType.Equipment && Cover[tile] != CoverType.Support)
            return Bump(actor, "nothing there to set alight");
        actor.Inventory.Use(ItemKind.Flare);
        if (direction is { } facing)
            actor.Facing = facing;
        actor.LastMovedTick = CurrentTick;
        _lights.Add(LightSource.Flare(tile));
        _lightExpiry.Add((_lights.Count - 1, CurrentTick + GearRules.FlareBurnTicks));
        _groundFlares.Add((tile, CurrentTick + GearRules.FlareBurnTicks));
        _lightGrid = null;
        EmitNoise(tile, GearRules.FlareLoudness, NoiseKinds.Flare);
        bool caught = TryIgnite(tile, FireRules.HeldFlareHeat, actor.Name);
        Kindled?.Invoke(actor, tile, caught);
        return GearRules.ThrowTicks;
    }

    /// <summary>A flare lying burning on the ground: it may light what it's on, every fire step.</summary>
    private void FlareLanded(TilePos tile)
    {
        _groundFlares.Add((tile, CurrentTick + GearRules.FlareBurnTicks));
        TryIgnite(tile, FireRules.FlareHeat, "a flare");
    }

    /// <summary>A close strike (thunder within 1.5 s) hits the ground on the map one time in three.</summary>
    private void StrikeGround(LightningStrike strike)
    {
        if (strike.ThunderDelayTicks > 150)
            return;
        ulong roll = Rng.Fork($"ground-strike:{strike.Tick}").NextULong();
        if (roll % 3 != 0)
            return;
        var tile = new TilePos((int)((roll >> 8) % (ulong)Map.Width), (int)((roll >> 32) % (ulong)Map.Height));
        LightningHitGround?.Invoke(tile);
        TryIgnite(tile, FireRules.LightningHeat, "lightning", strike.Tick);
    }

    /// <summary>Runs every fire step that time has passed since the last call.</summary>
    private void AdvanceFire()
    {
        if (CurrentTick <= _fireTick)
            return;
        long from = _fireTick;
        _fireTick = CurrentTick;
        for (long b = (Math.Max(0, from) / FireRules.StepTicks + 1) * FireRules.StepTicks; b <= CurrentTick; b += FireRules.StepTicks)
        {
            if (_fires.Count == 0 && _groundFlares.Count == 0 && _fuel.Count == 0 && !_smokeOnMap && _sparks.Count == 0)
                return; // nothing burning or about to
            FireStep(b);
        }
    }

    /// <summary>One fire step at <paramref name="tick"/>.</summary>
    private void FireStep(long tick)
    {
        int rain = Weather.Settings.RainPercent;

        // spilled fuel evaporates, faster in rain
        if (_fuel.Count > 0)
        {
            foreach (TilePos p in _fuel.Keys.ToList())
            {
                long until = _fuel[p] - (long)FireRules.StepTicks * rain / 25;
                if (until <= tick)
                    _fuel.Remove(p);
                else
                    _fuel[p] = until;
            }
        }

        // sources: flares on the ground, live sparks on a sagging fence
        for (int i = _groundFlares.Count - 1; i >= 0; i--)
        {
            if (_groundFlares[i].OffTick <= tick)
                _groundFlares.RemoveAt(i);
            else
                TryIgnite(_groundFlares[i].Tile, FireRules.FlareHeat, "a flare", tick);
        }
        foreach (SparkingFence sparks in _sparks)
        {
            if (!sparks.Live)
                continue;
            int k = Rng.Fork($"spark:{sparks.Position.X + WindowOrigin.X}:{sparks.Position.Y + WindowOrigin.Y}:{tick}").NextInt(8);
            TryIgnite(sparks.Position.Offset(Directions.All[k]), FireRules.SparkHeat, "sparks from the fence", tick);
        }

        bool hadFires = _fires.Count > 0;
        if (hadFires)
            BurnStep(tick, rain);
        TanksStep();
        BurnActors(tick);
        UpdateSmoke();
        UpdateFireLights();
        if (hadFires && _fires.Count == 0)
            FiresOut?.Invoke();
    }

    /// <summary>The fires grow or die down, pass heat on, and burn out.</summary>
    private void BurnStep(long tick, int rain)
    {
        var (wx, wy) = Weather.Downwind;
        int wind = Weather.Settings.WindSpeed;
        var heat = new Dictionary<TilePos, (int Max, int Count)>();
        var burntOut = new List<Fire>();
        foreach (Fire fire in _fires.Values.OrderBy(f => f.Position.Y).ThenBy(f => f.Position.X).ToList())
        {
            TilePos p = fire.Position;
            if (!FireRules.CanBurn(Cover[p]))
            {
                // the water came over it
                _fires.Remove(p);
                continue;
            }
            if (fire.FuelSteps > 0)
            {
                fire.Intensity = Math.Min(fire.MaxIntensity, fire.Intensity + FireRules.GrowthPerStep);
                fire.FuelSteps--;
                fire.StepsBurnt++;
            }
            else
            {
                fire.Intensity -= FireRules.DeclinePerStep;
            }
            fire.Intensity -= rain / 4;
            if (fire.Intensity <= 0)
            {
                burntOut.Add(fire);
                continue;
            }

            // heat for the neighbours: half its intensity, more downwind (2⅓ times at 10 m/s, none straight upwind) and uphill
            foreach (Direction d in Directions.All)
            {
                TilePos n = p.Offset(d);
                if (!Map.InBounds(n) || _fires.ContainsKey(n))
                    continue;
                int h = fire.Intensity * FireRules.HeatPercent / 100;
                if (d.IsDiagonal())
                    h = h * 7 / 10;
                if (wind > 0)
                {
                    var (dx, dy) = d.Delta();
                    int along = (dx * wx + dy * wy) * 1000 / (d.IsDiagonal() ? 1414 : 1000); // −1000 … 1000: how much it's downwind
                    h += h * along * Math.Min(wind, 15) / 7500;
                }
                int rise = Math.Clamp(Map.Heights[n] - Map.Heights[p], -10, 20);
                h += h * rise * 3 / 100;
                if (h <= 0)
                    continue;
                heat[n] = heat.TryGetValue(n, out var had) ? (Math.Max(had.Max, h), had.Count + 1) : (h, 1);
            }
        }

        foreach (var (n, (max, count)) in heat.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
            TryIgnite(n, Math.Min(100, max + FireRules.ExtraSourceHeat * (count - 1)), null, tick);

        foreach (Fire fire in burntOut)
            BurnOut(fire);

        // the crackle and roar of it, from where it burns hottest
        Fire? hottest = null;
        foreach (Fire fire in _fires.Values)
            if (hottest is null || fire.Intensity > hottest.Intensity
                || fire.Intensity == hottest.Intensity && (fire.Position.Y, fire.Position.X).CompareTo((hottest.Position.Y, hottest.Position.X)) < 0)
                hottest = fire;
        if (hottest is not null)
            EmitNoise(hottest.Position, Math.Min(FireLoudness, 15 + hottest.Intensity / 4 + _fires.Count / 10), NoiseKinds.Fire);
    }

    /// <summary>A fire has burnt itself out: the ground is left as its material leaves it.</summary>
    private void BurnOut(Fire fire)
    {
        TilePos p = fire.Position;
        _fires.Remove(p);
        _fuel.Remove(p); // any fuel that got onto it while it burned has burnt with it
        if (fire.StepsBurnt < 2)
            return; // put out before it took hold
        CoverType cover = Cover[p];
        if (cover == CoverType.Vehicle)
        {
            if (VehicleAt(p) is { } vehicle && _burntVehicles.Add(vehicle.Id))
                vehicle.Integrity = 0;
            return;
        }
        if (FireRules.Of(cover).BurnsInto is not { } into || FireRules.Of(cover).Burnable == 0)
            return;
        Cover[p] = into;
        CoverChanges++;
        TilesBurnt++;
        _lightGrid = null;
        if (cover == CoverType.Support && PlatformAt(p) is { Collapsed: false } platform)
            CheckCollapse(platform, null);
    }

    /// <summary>A facility's yard fuel tanks heated long enough by a fire go up, spraying burning fuel around them.</summary>
    private void TanksStep()
    {
        if (Power is null || _fires.Count == 0)
            return;
        foreach (FuelStore store in Power.Devices.OfType<FuelStore>())
        {
            if (store.Litres <= 0)
                continue;
            TilePos a = FromIsland(store.Footprint.X0 - 1, store.Footprint.Y0 - 1), b = FromIsland(store.Footprint.X1 + 1, store.Footprint.Y1 + 1);
            bool heated = false;
            foreach (Fire fire in _fires.Values)
            {
                TilePos f = fire.Position;
                if (fire.Intensity >= 50 && f.X >= a.X && f.X <= b.X && f.Y >= a.Y && f.Y <= b.Y)
                {
                    heated = true;
                    break;
                }
            }
            if (!heated)
            {
                _tankHeat.Remove(store.Id);
                continue;
            }
            int steps = _tankHeat.TryGetValue(store.Id, out int s) ? s + 1 : 1;
            _tankHeat[store.Id] = steps;
            if (steps < TankBurstSteps)
                continue;
            _tankHeat.Remove(store.Id);
            PowerGrid.SetFuel(store, 0, store.ValveOpen);
            var centre = FromIsland((store.Footprint.X0 + store.Footprint.X1) / 2, (store.Footprint.Y0 + store.Footprint.Y1) / 2);
            for (int y = a.Y - 1; y <= b.Y + 1; y++)
            {
                for (int x = a.X - 1; x <= b.X + 1; x++)
                {
                    var t = new TilePos(x, y);
                    if (!Map.InBounds(t) || !FireRules.CanBurn(Cover[t]) || _fires.ContainsKey(t))
                        continue;
                    _fuel[t] = CurrentTick + FireRules.FuelLastsTicks;
                    Kindle(t, null);
                }
            }
            if (Map.InBounds(centre))
            {
                EmitNoise(centre, TankBurstLoudness, NoiseKinds.TanksBursting);
                TanksBurst?.Invoke(centre);
            }
        }
    }

    /// <summary>Anyone standing in fire is burnt: badly in a hot one. Not more often than <see cref="BurnRepeatTicks"/>.</summary>
    private void BurnActors(long tick)
    {
        if (_fires.Count == 0)
            return;
        foreach (Actor actor in _actors)
        {
            if (actor.IsGone || actor.OnDeck)
                continue;
            int hottest = 0;
            foreach (TilePos t in CoreTiles(actor.Body))
                if (_fires.TryGetValue(t, out Fire? fire))
                    hottest = Math.Max(hottest, fire.Intensity);
            if (hottest == 0)
                continue;
            if (_lastBurnTick.TryGetValue(actor.Id, out long last) && tick - last < BurnRepeatTicks)
                continue;
            _lastBurnTick[actor.Id] = tick;
            Severity severity = hottest >= 70 ? Severity.Red : Severity.Yellow;
            Injure(actor, RandomSection(Array.Empty<BodySection>()), severity, bleeding: false, "burns");
            Burned?.Invoke(actor, severity);
            if (!actor.IsDead)
                CryOut(actor);
        }
    }

    /// <summary>Smoke from every fire, carried downwind in a widening plume (around the fire in still air).</summary>
    private void UpdateSmoke()
    {
        bool had = _smokeOnMap;
        if (had)
            Map.Smoke.Clear();
        _smokeOnMap = _fires.Count > 0;
        if (!_smokeOnMap)
        {
            if (had)
                _lightGrid = null; // the air has cleared
            return;
        }
        var (wx, wy) = Weather.Downwind;
        int wind = Weather.Settings.WindSpeed;
        foreach (Fire fire in _fires.Values)
        {
            int length = wind == 0 ? 1 : 2 + fire.Intensity / 20 + wind / 3;
            int strength = Math.Min(70, fire.Intensity * 6 / 10);
            for (int k = 0; k <= length; k++)
            {
                int cx = fire.Position.X + (int)Math.Round(wx * k / 1000.0), cy = fire.Position.Y + (int)Math.Round(wy * k / 1000.0);
                int amount = strength * (length + 1 - k) / (length + 1);
                int spread = 1 + k / 3;
                for (int y = cy - spread; y <= cy + spread; y++)
                {
                    for (int x = cx - spread; x <= cx + spread; x++)
                    {
                        var t = new TilePos(x, y);
                        if (!Map.InBounds(t))
                            continue;
                        int here = amount - 10 * Math.Max(Math.Abs(x - cx), Math.Abs(y - cy));
                        if (here > Map.Smoke[t])
                            Map.Smoke[t] = here;
                    }
                }
            }
        }
        _lightGrid = null;
    }

    /// <summary>The hottest fires light their surroundings, red-orange; the light pool's other lights go out.</summary>
    private void UpdateFireLights()
    {
        if (_fires.Count == 0 && _fireLights.Count == 0)
            return;
        var lit = _fires.Values.OrderByDescending(f => f.Intensity).ThenBy(f => f.Position.Y).ThenBy(f => f.Position.X)
            .Take(FireLightCap).ToList();
        bool changed = false;
        for (int i = 0; i < Math.Max(lit.Count, _fireLights.Count); i++)
        {
            if (i < lit.Count)
            {
                Fire f = lit[i];
                var light = new LightSource(f.Position, HeightDm: 15, RadiusTiles: 3 + f.Intensity / 20,
                    IntensityPercent: 30 + f.Intensity / 2, On: true, Red: true);
                if (i < _fireLights.Count)
                {
                    if (_lights[_fireLights[i]] == light)
                        continue;
                    _lights[_fireLights[i]] = light;
                }
                else
                {
                    _lights.Add(light);
                    _fireLights.Add(_lights.Count - 1);
                }
                changed = true;
            }
            else if (_lights[_fireLights[i]].On)
            {
                _lights[_fireLights[i]] = _lights[_fireLights[i]] with { On = false };
                changed = true;
            }
        }
        if (changed)
            _lightGrid = null;
    }

    /// <summary>Fires, spilled fuel and burning flares move with the map; those that scroll off are lost.</summary>
    private void ShiftFire(int dx, int dy)
    {
        if (_fires.Count > 0)
        {
            var moved = _fires.Values.ToList();
            _fires.Clear();
            foreach (Fire fire in moved)
            {
                var to = new TilePos(fire.Position.X - dx, fire.Position.Y - dy);
                if (!Map.InBounds(to))
                    continue;
                fire.Position = to;
                _fires[to] = fire;
            }
        }
        if (_fuel.Count > 0)
        {
            var spilled = _fuel.ToList();
            _fuel.Clear();
            foreach (var (p, until) in spilled)
            {
                var to = new TilePos(p.X - dx, p.Y - dy);
                if (Map.InBounds(to))
                    _fuel[to] = until;
            }
        }
        for (int i = _groundFlares.Count - 1; i >= 0; i--)
        {
            var to = new TilePos(_groundFlares[i].Tile.X - dx, _groundFlares[i].Tile.Y - dy);
            if (Map.InBounds(to))
                _groundFlares[i] = (to, _groundFlares[i].OffTick);
            else
                _groundFlares.RemoveAt(i);
        }
        UpdateSmoke();
        UpdateFireLights();
    }

    /// <summary>
    /// A step away from the nearest fire within <paramref name="radius"/> (one burning hot enough to
    /// notice), or null if there's none near or nowhere to go. Brains use it: animals give fire room.
    /// </summary>
    public ActorAction? FleeFire(Actor actor, int radius)
    {
        if (_fires.Count == 0)
            return null;
        TilePos at = actor.Position;
        Fire? nearest = null;
        int best = int.MaxValue;
        foreach (Fire fire in _fires.Values)
        {
            if (fire.Intensity < 20)
                continue;
            int d = Math.Max(Math.Abs(fire.Position.X - at.X), Math.Abs(fire.Position.Y - at.Y));
            if (d > radius || d > best || d == best && nearest is not null
                && (fire.Position.Y, fire.Position.X).CompareTo((nearest.Position.Y, nearest.Position.X)) > 0)
                continue;
            best = d;
            nearest = fire;
        }
        if (nearest is null)
            return null;
        TilePos from = nearest.Position;
        Direction? way = null;
        long farthest = (long)(at.X - from.X) * (at.X - from.X) + (long)(at.Y - from.Y) * (at.Y - from.Y);
        foreach (Direction d in Directions.All)
        {
            TilePos to = at.Offset(d);
            long away = (long)(to.X - from.X) * (to.X - from.X) + (long)(to.Y - from.Y) * (to.Y - from.Y);
            if (away <= farthest || _fires.ContainsKey(to) || MoveBlockReason(actor, d) is not null)
                continue;
            farthest = away;
            way = d;
        }
        return way is { } go ? new MoveAction(go) : null;
    }
}