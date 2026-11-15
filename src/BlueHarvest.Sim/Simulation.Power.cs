using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// The island's power network inside the simulation: stepped every game minute after the weather,
/// its lamps placed in the map window, its security doors locked and its paddock fences live while
/// their power lasts, its running generators heard, its motorised doors and elevators worked, and its
/// equipment operated through <see cref="OperateAction"/>.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>A running generator in the window is heard every this many ticks.</summary>
    public const int GeneratorNoiseTicks = 300;

    /// <summary>How loud a running generator is at its own tile: a steady roar that carries.</summary>
    public const int GeneratorLoudness = 38;

    /// <summary>Touching a live fence: the crack it makes, and how long it throws you off.</summary>
    public const int ShockLoudness = 40, ShockTicks = 300;

    /// <summary>A motorised door: quiet with power; without, forcing it takes 20 s and can be heard a long way.</summary>
    public const int MotorDoorLoudness = 12, ForcedDoorLoudness = 35, ForcedDoorTicks = 2000;

    /// <summary>The reason a large body gives when a live fence stops it.</summary>
    public const string LiveFenceReason = "a live fence";

    /// <summary>The map tiles of live paddock fence in the window.</summary>
    private readonly HashSet<TilePos> _liveFence = new();
    private ulong _powerSignature;
    private (int ActorId, long Tick) _lastShock = (-1, -1);

    /// <summary>Facility lamp id → index into the simulation's lights, for lamps that have been in the window.</summary>
    private readonly Dictionary<int, int> _lampLights = new();
    private long _machineryTick = -1;

    /// <summary>The island's power network; null on maps without one (the testbed).</summary>
    public PowerGrid? Power { get; private set; }

    /// <summary>Something on the network changed by itself, or right after a switch was thrown.</summary>
    public event Action<PowerEvent>? PowerChanged;

    /// <summary>Someone worked a piece of equipment: who, which, and what came of it.</summary>
    public event Action<Actor, PowerDevice, PowerResult>? PowerOperated;

    /// <summary>Someone (or something) touched a live fence at this tile.</summary>
    public event Action<Actor, TilePos>? Shocked;

    /// <summary>Someone forced a motorised door that had no power.</summary>
    public event Action<Actor, TilePos>? DoorForced;

    /// <summary>Someone got out of an elevator or a cable car at the end of a ride.</summary>
    public event Action<Actor>? RideEnded;

    /// <summary>
    /// Puts the island's power network on this map: from now on it steps every game minute, its
    /// facilities' lamps light the map window while they have power, and its equipment can be worked.
    /// </summary>
    /// <param name="islandX">Where the map's tile (0, 0) is now, in island metres (east).</param>
    /// <param name="islandY">The same, south.</param>
    public void UsePower(PowerGrid grid, long islandX, long islandY)
    {
        Power = grid;
        SetIslandOrigin(islandX, islandY);
        foreach (PowerEvent e in grid.Settle(SunPercent(CurrentTick), PowerWeather(GameMinute(CurrentTick))))
            PowerChanged?.Invoke(e);
        SyncPowerWorld();
    }

    /// <summary>The window's tiles of live paddock fence (for drawing).</summary>
    public IReadOnlyCollection<TilePos> LiveFenceTiles => _liveFence;

    /// <summary>
    /// Whether a map tile is paddock fence with power in it: it holds against anything, and shocks
    /// whatever touches it. A sagging section is shorted out, and a broken one is just wire on the ground.
    /// </summary>
    public bool IsLiveFence(TilePos tile) =>
        _liveFence.Count > 0 && _liveFence.Contains(tile) && Cover[tile] == CoverType.Fence;

    /// <summary>The powered door at a map tile, if any.</summary>
    public PowerDoor? PowerDoorAt(TilePos tile)
    {
        if (Power is null)
            return null;
        var (x, y) = ToIsland(tile);
        return Power.DoorAt(x, y);
    }

    /// <summary>A map tile's place in island metres.</summary>
    public (long X, long Y) ToIsland(TilePos tile) => (IslandOrigin.X + tile.X, IslandOrigin.Y + tile.Y);

    /// <summary>The map tile at a place in island metres (it may be outside the map).</summary>
    public TilePos FromIsland(long x, long y) => new((int)(x - IslandOrigin.X), (int)(y - IslandOrigin.Y));

    /// <summary>The devices an actor can work from where it stands: on the ground, inside or next to them.</summary>
    public IEnumerable<PowerDevice> DevicesInReach(Actor actor)
    {
        if (Power is null || actor.IsGone || actor.IsLarge || actor.OnDeck)
            yield break;
        var (x, y) = ToIsland(actor.Position);
        foreach (PowerDevice device in Power.Devices)
        {
            if (device.Footprint.Contains(x, y))
                yield return device;
        }
    }

    /// <summary>The devices whose footprint covers a map tile: for tooltips.</summary>
    public IEnumerable<PowerDevice> DevicesAt(TilePos tile)
    {
        if (Power is null)
            yield break;
        var (x, y) = ToIsland(tile);
        foreach (PowerDevice device in Power.Devices)
        {
            if (device.Footprint.Contains(x, y))
                yield return device;
        }
    }

    /// <summary>The facility whose compound a map tile is in, if any.</summary>
    public PowerSite? SiteAt(TilePos tile)
    {
        if (Power is null)
            return null;
        var (x, y) = ToIsland(tile);
        foreach (PowerSite site in Power.Sites)
        {
            if (site.Contains(x, y))
                return site;
        }
        return null;
    }

    private int SunPercent(long tick) => Clock is { } clock ? Sky.SunPercent(clock.MinuteOfDay(tick)) : 100;

    /// <summary>The weather the network sees at a place: the source's there, or the map's own when it's set by hand.</summary>
    private Func<int, int, WeatherSettings> PowerWeather(long minute) =>
        FollowingWeatherSource
            ? (x, y) => WeatherSource!.At(x, y, minute).Clamped()
            : (_, _) => Weather.Settings;

    /// <summary>One game minute on the network, at a minute boundary.</summary>
    private void StepPower(long tick)
    {
        List<PowerEvent> events = Power!.Step(GameMinute(tick), SunPercent(tick), PowerWeather(GameMinute(tick)));
        // floodlights come on at dusk with no event, so the lamps are checked every minute (cheaply)
        SyncPowerLights();
        if (events.Count > 0 || PowerSignature() != _powerSignature)
        {
            SyncPowerDoors();
            SyncPowerFences();
        }
        foreach (PowerEvent e in events)
            PowerChanged?.Invoke(e);
    }

    /// <summary>Lamps, security doors and live fences in the window, all brought in line with the network.</summary>
    private void SyncPowerWorld()
    {
        SyncPowerLights();
        SyncPowerDoors();
        SyncPowerFences();
    }

    /// <summary>What the doors and fences depend on: which sites have power, their locks, and which fence lines are live.</summary>
    private ulong PowerSignature()
    {
        var hasher = new StateHasher();
        foreach (PowerSite site in Power!.Sites)
            hasher.Add(site.Powered ? 1 : 0).Add(site.LocksEngaged ? 1 : 0);
        foreach (FenceLine fence in Power.FenceLines)
            hasher.Add(Power.FenceLive(Power.Feeders[fence.FeederId]) ? 1 : 0);
        return hasher.Value;
    }

    /// <summary>
    /// Security doors in the window lock while their site has power and its locks are engaged, and
    /// unlock (closed) when either goes. A door with someone standing in it waits for the next change.
    /// </summary>
    private void SyncPowerDoors()
    {
        if (Power is null)
            return;
        foreach (PowerDoor door in Power.Doors)
        {
            if (door.Kind != DoorKind.SecurityLock)
                continue;
            TilePos tile = FromIsland(door.X, door.Y);
            if (!Map.InBounds(tile) || Cover[tile] != CoverType.Door)
                continue;
            bool locked = Power.DoorLocked(door);
            DoorState state = Map.Structures.DoorAt(tile);
            if (locked && state != DoorState.Locked && ActorAt(tile) is null)
                SetDoor(tile, DoorState.Locked);
            else if (!locked && state == DoorState.Locked)
                SetDoor(tile, DoorState.Closed);
        }
    }

    /// <summary>Marks the window's tiles of paddock fence that are live: those along a live fence line.</summary>
    private void SyncPowerFences()
    {
        _liveFence.Clear();
        if (Power is null)
            return;
        _powerSignature = PowerSignature();
        var (ox, oy) = IslandOrigin;
        long right = ox + Map.Width, bottom = oy + Map.Height;
        foreach (FenceLine fence in Power.FenceLines)
        {
            if (!Power.FenceLive(Power.Feeders[fence.FeederId]))
                continue;
            int[] p = fence.Poly;
            int n = p.Length / 2;
            for (int k = 0; k < n; k++)
            {
                int x0 = p[2 * k], y0 = p[2 * k + 1], x1 = p[(2 * k + 2) % p.Length], y1 = p[(2 * k + 3) % p.Length];
                // skip stretches that can't touch the window
                if (Math.Max(x0, x1) < ox - 1 || Math.Min(x0, x1) > right || Math.Max(y0, y1) < oy - 1 || Math.Min(y0, y1) > bottom)
                    continue;
                int steps = Math.Max(1, 2 * Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
                for (int i = 0; i <= steps; i++)
                {
                    long x = x0 + (long)(x1 - x0) * i / steps, y = y0 + (long)(y1 - y0) * i / steps;
                    // the drawn fence can sit a tile either side of the surveyed line
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            TilePos tile = FromIsland(x + dx, y + dy);
                            if (Map.InBounds(tile) && Cover[tile] == CoverType.Fence)
                                _liveFence.Add(tile);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Touching a live fence: a crack, a jolt, and for a person a hurt arm. Returns the time it costs.</summary>
    private int Shock(Actor actor, TilePos fence)
    {
        // one jolt per touch: a bite across three fence tiles is still one shock
        if (_lastShock == (actor.Id, CurrentTick))
            return ShockTicks;
        _lastShock = (actor.Id, CurrentTick);
        EmitNoise(fence, ShockLoudness, NoiseKinds.ElectricCrack);
        if (!actor.IsLarge)
            Injure(actor, CurrentTick % 2 == 0 ? BodySection.RightArm : BodySection.LeftArm, Severity.Yellow, bleeding: false, "an electric fence");
        Shocked?.Invoke(actor, fence);
        return ShockTicks;
    }

    /// <summary>
    /// Opens or shuts a door. A motorised door slides quietly with power; without it has to be forced,
    /// slowly and loudly. Anything else swings as before.
    /// </summary>
    private int WorkDoor(Actor actor, TilePos tile, DoorState state)
    {
        if (PowerDoorAt(tile) is not { Kind: DoorKind.Motor } door)
            return SwingDoor(actor, tile, state);
        SetDoor(tile, state);
        actor.LastMovedTick = CurrentTick;
        if (Power!.DoorPowered(door))
        {
            EmitNoise(tile, MotorDoorLoudness, NoiseKinds.DoorSliding, actor.Id);
            return DoorTicks;
        }
        EmitNoise(tile, ForcedDoorLoudness, NoiseKinds.DoorForced, actor.Id);
        DoorForced?.Invoke(actor, tile);
        return ForcedDoorTicks;
    }

    /// <summary>
    /// Ends a ride: puts the actor at its destination. If the destination is on the map it steps out
    /// at once; otherwise it waits there, parked, until the map is moved over it (the front end
    /// recentres the window on the returned tile and shifts). Returns the destination's map tile.
    /// </summary>
    public TilePos CompleteRide(Actor actor)
    {
        if (actor.PendingRide is not { } to)
            return actor.Position;
        actor.PendingRide = null;
        TilePos tile = FromIsland(to.X, to.Y);
        if (actor.IsGone)
            return tile;
        if (Map.InBounds(tile) && ActorAt(tile) is null)
        {
            Vacate(actor);
            Place(actor, tile, actor.Facing, onDeck: false);
            RideEnded?.Invoke(actor);
            return tile;
        }
        // off the map (or someone is standing there): wait at the far end until the window gets there
        Vacate(actor);
        actor.LeftBehind = true;
        actor.ParkedAt = new TilePos(tile.X + WindowOrigin.X, tile.Y + WindowOrigin.Y);
        actor.ParkedLightOn = actor.CarriedLight is { } held && _lights[held].On;
        if (actor.CarriedLight is { } carried && _lights[carried].On)
            SetLightOn(carried, false);
        RideEnded?.Invoke(actor);
        return tile;
    }

    /// <summary>
    /// Places every facility lamp that is inside the map window and switches it on or off with its
    /// facility. Lamps that have left the window were already switched off by the shift.
    /// </summary>
    private void SyncPowerLights()
    {
        if (Power is null)
            return;
        bool changed = false;
        foreach (PowerLamp lamp in Power.Lamps)
        {
            TilePos tile = FromIsland(lamp.X, lamp.Y);
            bool inside = Map.InBounds(tile);
            bool on = inside && lamp.Lit;
            if (_lampLights.TryGetValue(lamp.Id, out int index))
            {
                LightSource light = _lights[index];
                LightSource wanted = inside ? light with { Position = tile, On = on } : light with { On = false };
                if (wanted == light)
                    continue;
                _lights[index] = wanted;
                changed = true;
            }
            else if (inside)
            {
                _lights.Add(new LightSource(tile, lamp.HeightDm, lamp.RadiusTiles, lamp.IntensityPercent, On: on, Red: lamp.Kind == LampKind.Emergency));
                _lampLights[lamp.Id] = _lights.Count - 1;
                changed = true;
            }
        }
        if (changed)
            _lightGrid = null;
    }

    /// <summary>Running generators inside the window make themselves heard every few seconds.</summary>
    private void AdvanceMachinery()
    {
        if (Power is null || CurrentTick <= _machineryTick)
            return;
        long from = _machineryTick;
        _machineryTick = CurrentTick;
        if (from >= 0 && CurrentTick / GeneratorNoiseTicks == from / GeneratorNoiseTicks)
            return;
        foreach (PowerDevice device in Power.Devices)
        {
            if (device is not Generator { State: GeneratorState.Running })
                continue;
            var (cx, cy) = device.Footprint.Centre;
            TilePos tile = FromIsland(cx, cy);
            if (Map.InBounds(tile))
                EmitNoise(tile, GeneratorLoudness, NoiseKinds.Generator);
        }
    }

    /// <summary>Works a device: the actor must be inside or next to it, on the ground. Repairs roll against its electronics skill.</summary>
    private int Operate(Actor actor, int deviceId, PowerOp op, int arg)
    {
        if (Power is null || deviceId < 0 || deviceId >= Power.Devices.Count)
            return BumpTicks;
        PowerDevice device = Power.Devices[deviceId];
        if (!DevicesInReach(actor).Contains(device))
        {
            PowerOperated?.Invoke(actor, device, new PowerResult(false, $"You need to be at {device.Name} to work it.", 0));
            return BumpTicks;
        }
        int roll = Rng.Fork($"power:{deviceId}:{CurrentTick}").NextInt(100);
        PowerResult result = Power.Operate(deviceId, op, arg, actor.Skills.Electronics, roll);
        if (result.Loudness > 0)
            EmitNoise(actor.Position, result.Loudness, result.NoiseKind, actor.Id);
        if (result.Done && op == PowerOp.Ride && Power.Devices[arg] is Transport to)
        {
            // get out just inside the far room's corner once the ride's time has passed (see CompleteRide)
            actor.PendingRide = (to.Footprint.X0 + 1, to.Footprint.Y0 + 1);
        }
        PowerOperated?.Invoke(actor, device, result);
        // the lights, doors and fences answer the switch at once, not at the next minute
        List<PowerEvent> events = Power.Settle(SunPercent(CurrentTick), PowerWeather(GameMinute(CurrentTick)));
        SyncPowerWorld();
        foreach (PowerEvent e in events)
            PowerChanged?.Invoke(e);
        return Math.Max(BumpTicks, result.Ticks);
    }
}
