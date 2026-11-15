using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// The island's power network inside the simulation: stepped every game minute after the weather,
/// its lamps placed in the map window (on while their facility has power), its running generators
/// heard, and its equipment worked through <see cref="OperateAction"/>.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>A running generator in the window is heard every this many ticks.</summary>
    public const int GeneratorNoiseTicks = 300;

    /// <summary>How loud a running generator is at its own tile: a steady roar that carries.</summary>
    public const int GeneratorLoudness = 38;

    /// <summary>Facility lamp id → index into the simulation's lights, for lamps that have been in the window.</summary>
    private readonly Dictionary<int, int> _lampLights = new();
    private long _machineryTick = -1;

    /// <summary>The island's power network; null on maps without one (the testbed).</summary>
    public PowerGrid? Power { get; private set; }

    /// <summary>Something on the network changed by itself, or right after a switch was thrown.</summary>
    public event Action<PowerEvent>? PowerChanged;

    /// <summary>Someone worked a piece of equipment: who, which, and what came of it.</summary>
    public event Action<Actor, PowerDevice, PowerResult>? PowerOperated;

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
        SyncPowerLights();
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
        if (events.Count == 0)
            return;
        SyncPowerLights();
        foreach (PowerEvent e in events)
            PowerChanged?.Invoke(e);
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
            bool on = inside && Power.Sites[lamp.SiteId].Powered;
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
                _lights.Add(new LightSource(tile, lamp.HeightDm, lamp.RadiusTiles, lamp.IntensityPercent, On: on));
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
        PowerOperated?.Invoke(actor, device, result);
        // the lights answer the switch at once, not at the next minute
        List<PowerEvent> events = Power.Settle(SunPercent(CurrentTick), PowerWeather(GameMinute(CurrentTick)));
        SyncPowerLights();
        foreach (PowerEvent e in events)
            PowerChanged?.Invoke(e);
        return Math.Max(BumpTicks, result.Ticks);
    }
}
