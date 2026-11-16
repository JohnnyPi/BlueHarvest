using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

public enum WaterEventKind : byte
{
    HighTide,
    LowTide,
    /// <summary>A river on the map is running high: its fords are deeper.</summary>
    RiverRising,
    /// <summary>A river on the map is in flood: over its banks, its fords closed.</summary>
    RiverInFlood,
    /// <summary>A river on the map is back to its usual level.</summary>
    RiverFalling,
}

public sealed record WaterEvent(WaterEventKind Kind, int River, string Text);

/// <summary>
/// Water that moves in calendar time: the tide covering and uncovering the sea's edge, rivers rising
/// after rain over their catchments, the current they run with and the surf on the coast. Stepped
/// every game minute after the weather; it changes the map's water tiles (in the map window only)
/// and makes running water and breaking surf drown out other sounds nearby.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>A river this many dm above its usual surface is running high, and this many is in flood.</summary>
    public const int RiverHighDm = 3, RiverFloodDm = 8;

    /// <summary>Breaking surf at its biggest takes this much off every sound heard at the water's edge; it fades a point a tile inland.</summary>
    public const int SurfMaskAtFull = 18;

    /// <summary>Running water takes a point off sounds for every 2 dm/s it runs, up to this.</summary>
    public const int RiverMaskMax = 12;

    /// <summary>Water running at this many dm/s or more can carry a small body off its feet.</summary>
    public const int SweepSpeedDm = 16;

    /// <summary>Being carried a tile downstream: the time it costs and the splash it makes.</summary>
    public const int SweptTicks = 150, SplashLoudness = 22;

    private int _seaLevelDm;
    private readonly Dictionary<int, int> _riverRise = new();
    private readonly Dictionary<int, int> _riverSpeed = new();
    private readonly Dictionary<int, int> _riverBand = new();
    private readonly HashSet<int> _windowRivers = new();
    private byte[]? _waterMask;
    private ulong _waterMaskSignature;

    /// <summary>The run's tides, swell and rivers; null without (the testbed, a loaded map).</summary>
    public WaterLevels? Waters { get; private set; }

    /// <summary>How many tiles the tide and the rivers have covered or uncovered so far.</summary>
    public int WaterChanges { get; private set; }

    /// <summary>The sea against its mean level now, dm.</summary>
    public int SeaLevelDm => _seaLevelDm;

    /// <summary>Whether the tide is coming in.</summary>
    public bool TideRising => Waters is { } w && w.TideRising(GameMinute(CurrentTick), MoonPhaseAt(GameMinute(CurrentTick)));

    /// <summary>Whether the map has any of the sea's edge on it, and any river.</summary>
    public bool HasSeaInWindow { get; private set; }
    public bool HasRiverInWindow => _windowRivers.Count > 0;

    /// <summary>The tide, a flood or a fall changed tiles on the map: the front end redraws its water.</summary>
    public event Action? WaterMoved;

    /// <summary>High and low water, and rivers on the map rising, flooding and falling.</summary>
    public event Action<WaterEvent>? WaterChanged;

    /// <summary>Someone was carried a tile downstream by the current.</summary>
    public event Action<Actor, TilePos>? Swept;

    /// <summary>Starts the tides and the rivers running on this map (whose water layer the island filled in).</summary>
    public void UseWater(WaterLevels levels)
    {
        Waters = levels;
        SyncWater();
    }

    /// <summary>How far a river stands above its usual surface now, dm (0 for a river not on the map).</summary>
    public int RiverRiseDm(int river) => _riverRise.TryGetValue(river, out int rise) ? rise : 0;

    /// <summary>The current on a tile now: the river's usual current, faster when it is high.</summary>
    public WaterFlow CurrentAt(TilePos p)
    {
        WaterFlow flow = Map.Flow[p];
        if (flow.IsNone || Cover[p] is not (CoverType.ShallowWater or CoverType.DeepWater))
            return WaterFlow.None;
        WaterTile w = Map.Water[p];
        if (w.Kind != WaterKind.River || !_riverSpeed.TryGetValue(w.River, out int percent) || percent == 100)
            return flow;
        return new WaterFlow((sbyte)Math.Clamp(flow.X * percent / 100, -127, 127), (sbyte)Math.Clamp(flow.Y * percent / 100, -127, 127));
    }

    /// <summary>How big the surf is breaking on a tile of the sea's edge, 0–100; 0 away from the coast and on dry ground.</summary>
    public int SurfPercentAt(TilePos p)
    {
        if (Waters is not { } waters || Map.Water[p].ShoreFacing is not { } facing
            || Cover[p] is not (CoverType.ShallowWater or CoverType.DeepWater))
            return 0;
        return waters.SurfPercent(facing, Weather.Settings, GameMinute(CurrentTick));
    }

    /// <summary>How much running water and breaking surf drown out sounds heard at a tile.</summary>
    public int WaterMaskAt(TilePos p) => _waterMask is { } mask ? mask[p.Y * Map.Width + p.X] : 0;

    private int MoonPhaseAt(long minute) => Sky.MoonPhase(minute, _moonStartPhase);

    /// <summary>One game minute of water: the tide's height, the rivers' rise, and the map brought into line.</summary>
    private void StepWater(long tick)
    {
        WaterLevels waters = Waters!;
        long minute = GameMinute(tick);
        int phase = MoonPhaseAt(minute);
        int turn = waters.TideTurn(minute, phase);
        if (turn != 0 && HasSeaInWindow)
            WaterChanged?.Invoke(turn > 0
                ? new WaterEvent(WaterEventKind.HighTide, -1, "The tide is at its height; it turns and begins to ebb.")
                : new WaterEvent(WaterEventKind.LowTide, -1, "The tide is out as far as it goes: the flats lie bare."));

        bool changed = SetLevels(minute);
        if (changed)
            ApplyWater();
        RefreshWaterMask();
    }

    /// <summary>Brings the map's water into line after it scrolled, or when the water starts.</summary>
    private void SyncWater()
    {
        if (Waters is null)
            return;
        _windowRivers.Clear();
        HasSeaInWindow = false;
        for (int y = 0; y < Map.Height; y++)
        {
            for (int x = 0; x < Map.Width; x++)
            {
                WaterTile w = Map.Water[new TilePos(x, y)];
                if (w.Kind == WaterKind.River)
                    _windowRivers.Add(w.River);
                else if (w.Kind == WaterKind.Sea)
                    HasSeaInWindow = true;
            }
        }
        // rivers that left the map are forgotten; their state is worked out again when they come back
        foreach (int gone in _riverBand.Keys.Where(r => !_windowRivers.Contains(r)).ToList())
            _riverBand.Remove(gone);
        SetLevels(GameMinute(CurrentTick), quiet: true);
        ApplyWater();
        _waterMaskSignature = 0;
        RefreshWaterMask();
    }

    /// <summary>Works out the sea level and the rise of each river on the map; true if any of them changed.</summary>
    private bool SetLevels(long minute, bool quiet = false)
    {
        WaterLevels waters = Waters!;
        bool changed = false;
        int sea = waters.SeaLevelDm(minute, MoonPhaseAt(minute));
        if (sea != _seaLevelDm)
        {
            _seaLevelDm = sea;
            changed = true;
        }
        foreach (int river in _windowRivers.OrderBy(r => r))
        {
            int rise = waters.RiverRiseDm(river, minute, WeatherSource);
            int speed = waters.RiverSpeedPercent(river, minute, WeatherSource);
            if (!_riverRise.TryGetValue(river, out int had) || had != rise)
            {
                _riverRise[river] = rise;
                changed = true;
            }
            _riverSpeed[river] = speed;
            int band = rise >= RiverFloodDm ? 2 : rise >= RiverHighDm ? 1 : 0;
            bool known = _riverBand.TryGetValue(river, out int wasBand);
            _riverBand[river] = band;
            if (quiet || !known || band == wasBand)
                continue;
            WaterChanged?.Invoke(band > wasBand
                ? band == 2
                    ? new WaterEvent(WaterEventKind.RiverInFlood, river, "A river nearby is in flood: brown water over its banks, its fords gone.")
                    : new WaterEvent(WaterEventKind.RiverRising, river, "A river nearby is rising after the rain: its fords run deeper.")
                : new WaterEvent(WaterEventKind.RiverFalling, river, band == 0
                    ? "A river nearby has fallen back to its usual level."
                    : "A river nearby is falling, back inside its banks."));
        }
        return changed;
    }

    /// <summary>
    /// Covers and uncovers the map's water tiles to the levels now: dry ground above the water, shallow
    /// water up to wading depth, deep beyond. A tile someone changed (a vehicle, a wreck) is left alone,
    /// and water doesn't go deep under anyone standing in it: they can still wade out.
    /// </summary>
    private void ApplyWater()
    {
        int changed = 0;
        for (int y = 0; y < Map.Height; y++)
        {
            for (int x = 0; x < Map.Width; x++)
            {
                var p = new TilePos(x, y);
                WaterTile w = Map.Water[p];
                if (w.Kind == WaterKind.None)
                    continue;
                CoverType now = Cover[p];
                if (!w.IsWaterState(now))
                    continue;
                int level = w.Kind == WaterKind.Sea ? _seaLevelDm : RiverRiseDm(w.River);
                int depth = level - w.GroundDm;
                CoverType want = depth <= 0 ? w.DryCover : depth > WaterLevels.WadeDepthDm ? CoverType.DeepWater : CoverType.ShallowWater;
                if (want == CoverType.DeepWater && ActorAt(p) is not null)
                    want = CoverType.ShallowWater;
                if (want == now)
                    continue;
                Cover[p] = want;
                changed++;
            }
        }
        if (changed == 0)
            return;
        WaterChanges += changed;
        WaterMoved?.Invoke();
    }

    /// <summary>
    /// Rebuilds where running water and surf drown out sound, when anything it depends on has changed:
    /// the water's sources, spread inland a point a tile.
    /// </summary>
    private void RefreshWaterMask()
    {
        if (Waters is null)
            return;
        ulong signature = WaterMaskSignature();
        if (signature == _waterMaskSignature && _waterMask is not null)
            return;
        _waterMaskSignature = signature;
        int w = Map.Width, h = Map.Height;
        if (_waterMask is null || _waterMask.Length != w * h)
            _waterMask = new byte[w * h];
        byte[] mask = _waterMask;
        bool any = false;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = new TilePos(x, y);
                int value = 0;
                if (Cover[p] is CoverType.ShallowWater or CoverType.DeepWater)
                {
                    // the surf breaks at the water's edge
                    if (Map.Water[p].ShoreFacing is not null && HasDryNeighbour(p))
                        value = SurfPercentAt(p) * SurfMaskAtFull / 100;
                    int speed = CurrentAt(p).SpeedDm;
                    if (speed > 0)
                        value = Math.Max(value, Math.Min(RiverMaskMax, speed / 2));
                }
                mask[y * w + x] = (byte)value;
                any |= value > 0;
            }
        }
        if (!any)
            return;
        // spread: a point less for every tile away (two passes, each looking back at four neighbours)
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[y * w + x] = (byte)Math.Max(mask[y * w + x], Math.Max(
                    Math.Max(At(x - 1, y), At(x, y - 1)), Math.Max(At(x - 1, y - 1), At(x + 1, y - 1))) - 1);
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
                mask[y * w + x] = (byte)Math.Max(mask[y * w + x], Math.Max(
                    Math.Max(At(x + 1, y), At(x, y + 1)), Math.Max(At(x + 1, y + 1), At(x - 1, y + 1))) - 1);

        int At(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? 0 : mask[y * w + x];
    }

    /// <summary>Whether surf breaks on this tile: the sea's edge, with dry ground beside it.</summary>
    public bool IsSurfEdge(TilePos p) =>
        Map.Water[p].ShoreFacing is not null && Cover[p] is CoverType.ShallowWater or CoverType.DeepWater && HasDryNeighbour(p);

    private bool HasDryNeighbour(TilePos p)
    {
        foreach (Direction d in Directions.Cardinal)
        {
            var n = p.Offset(d);
            if (Map.InBounds(n) && Cover[n] is not (CoverType.ShallowWater or CoverType.DeepWater))
                return true;
        }
        return false;
    }

    /// <summary>What the sound mask depends on: the water levels, the wind and swell, and the map itself.</summary>
    private ulong WaterMaskSignature()
    {
        var hasher = new StateHasher().Add(_seaLevelDm).Add(WaterChanges).Add(WindowShifts)
            .Add((int)Weather.Settings.WindFrom).Add(Weather.Settings.WindSpeed)
            .Add(Waters!.SwellPercent(GameMinute(CurrentTick)) / 5);
        foreach (int river in _windowRivers.OrderBy(r => r))
            hasher.Add(river).Add(RiverRiseDm(river)).Add(_riverSpeed.TryGetValue(river, out int s) ? s : 100);
        return hasher.Value;
    }

    /// <summary>
    /// The cost of wading into a tile, in percent of a step on its cover: with the current it's
    /// easier (down to 70%), against it harder (3% more for every dm/s, up to 220%), and breaking
    /// surf makes it up to half as hard again.
    /// </summary>
    private int WadeCostPercent(TilePos to, Direction direction)
    {
        if (Cover[to] != CoverType.ShallowWater)
            return 100;
        int percent = 100;
        WaterFlow flow = CurrentAt(to);
        if (!flow.IsNone)
        {
            var (dx, dy) = direction.Delta();
            int along = (dx * flow.X + dy * flow.Y) * 100 / (dx != 0 && dy != 0 ? 141 : 100);
            percent = Math.Clamp(100 - along * 3, 70, 220);
        }
        int surf = SurfPercentAt(to);
        if (surf > 30)
            percent = percent * (100 + (surf - 30) * 5 / 7) / 100;
        return percent;
    }

    /// <summary>
    /// After a step: a small body wading in fast water may be carried a tile downstream, likelier the
    /// faster it runs (40% at 1.6 m/s, certain from 2.2 m/s). Returns the time it costs.
    /// </summary>
    private int SweepDownstream(Actor actor)
    {
        if (actor.IsLarge || actor.OnDeck || actor.IsGone)
            return 0;
        TilePos at = actor.Position;
        if (Cover[at] != CoverType.ShallowWater)
            return 0;
        WaterFlow flow = CurrentAt(at);
        int speed = flow.SpeedDm;
        if (speed < SweepSpeedDm)
            return 0;
        int chance = Math.Min(100, (speed - 12) * 10);
        if (Rng.Fork($"sweep:{actor.Id}:{CurrentTick}").NextInt(100) >= chance)
            return 0;
        Direction down = Downstream(flow);
        var (blocked, toDeck) = SmallStep(actor, at, fromDeck: false, down, ignoreSmallActors: false);
        if (blocked is not null || toDeck)
            return 0;
        TilePos to = at.Offset(down);
        Place(actor, to, actor.Facing, onDeck: false);
        EmitNoise(to, SplashLoudness, NoiseKinds.Splashing, actor.Id);
        Swept?.Invoke(actor, to);
        return SweptTicks;
    }

    /// <summary>The one of the eight directions the water runs along most.</summary>
    private static Direction Downstream(WaterFlow flow)
    {
        Direction best = Direction.North;
        long most = long.MinValue;
        foreach (Direction d in Directions.All)
        {
            var (dx, dy) = d.Delta();
            long along = (long)(dx * flow.X + dy * flow.Y) * (dx != 0 && dy != 0 ? 707 : 1000);
            if (along > most)
            {
                most = along;
                best = d;
            }
        }
        return best;
    }
}
