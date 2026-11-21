#nullable enable
using System;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// Everything on the map that moves on its own: water (glints on lakes and the open sea, streaks
/// running down rivers, wave lines coming in to the shore, rings where rain hits water), the
/// sparks crawling along live paddock fences, and fire: flames, embers and drifting smoke.
/// It runs on real time, so it keeps moving while the game waits for your turn, like the rain.
/// Purely for looks: it reads the map and never changes it, and nothing in the simulation reads it.
/// Add it after the AsciiMapView (so it draws on top of the ground) and before the WeatherView.
/// </summary>
public partial class AmbientView : Node2D
{
    /// <summary>What moves on a tile.</summary>
    private enum Motion : byte
    {
        None,
        /// <summary>Lake or open sea: slow glints.</summary>
        Still,
        /// <summary>River: streaks along the current.</summary>
        Running,
        /// <summary>Sea within a few tiles of land: wave lines moving to the shore.</summary>
        Surf,
        /// <summary>Land at the water's edge, next to surf: swash on a beach, spray on rock or a structure (Layered look only).</summary>
        Swash,
        /// <summary>Plants the wind can move: gusts pass over them as a travelling shimmer (Layered look only, in wind).</summary>
        Vegetation,
    }

    /// <summary>What kind of shore a <see cref="Motion.Swash"/> tile is, which decides how the waves end on it.</summary>
    private enum Shore : byte
    {
        None,
        /// <summary>Sand, mud or low ground: foam runs up and back, and the sand gleams wet a moment.</summary>
        Beach,
        /// <summary>Rock, a boulder or a step of a metre or more: the wave bursts into spray.</summary>
        Rocky,
        /// <summary>A wall, pier, road or anything built: a small splash.</summary>
        Built,
    }

    private const int Cell = AsciiMapView.CellSize;

    /// <summary>Redraws per second. Water doesn't need more, and it keeps the cost down.</summary>
    private const double FramesPerSecond = 24;

    /// <summary>How far out from the shore the waves show, in tiles.</summary>
    private const int SurfReach = 3;

    /// <summary>Seconds between waves in still air; wind brings them faster.</summary>
    private const float WavePeriod = 4.5f;

    /// <summary>With more animated tiles than this on screen (zoomed far out), only some of them move.</summary>
    private const int TileBudget = 5000;

    private static readonly Color Glint = new(0.78f, 0.90f, 1f, 0.34f);
    private static readonly Color Streak = new(0.80f, 0.92f, 1f, 0.42f);
    private static readonly Color WhiteWater = new(0.95f, 0.98f, 1f, 0.60f);
    private static readonly Color Wave = new(0.85f, 0.94f, 1f, 0.50f);
    private static readonly Color Foam = new(1f, 1f, 1f, 0.70f);
    private static readonly Color RainRing = new(0.80f, 0.88f, 1f, 0.45f);
    private static readonly Color Spark = new(0.75f, 0.92f, 1f, 0.95f);
    private static readonly Color FlameGlow = new(1f, 0.45f, 0.10f, 0.45f);
    private static readonly Color FlameCore = new(1f, 0.85f, 0.35f, 0.95f);
    private static readonly Color FlameEdge = new(1f, 0.35f, 0.08f, 0.9f);
    private static readonly Color Ember = new(1f, 0.6f, 0.2f, 0.9f);
    private static readonly Color SmokePuff = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color WetSheen = new(0.02f, 0.04f, 0.08f, 0.2f);
    private static readonly Color Gust = new(0.88f, 0.96f, 0.72f, 1f);
    private static readonly Color Leaf = new(0.75f, 0.90f, 0.45f, 0.9f);
    private static readonly Color LampDim = new(0f, 0f, 0f, 0.38f);
    private static readonly Color Beacon = new(1f, 0.32f, 0.25f, 0.5f);

    /// <summary>Wind below this (m/s) moves no plants visibly; gusts are at their strongest from 15 m/s.</summary>
    private const float GustFromWind = 3f, GustFullWind = 15f;

    /// <summary>Distance between gust fronts, in tiles.</summary>
    private const float GustSpacing = 18f;

    /// <summary>A site's lamps flicker when it runs on its batteries and they're below this charge.</summary>
    private const int FlickerBelowPercent = 25;
    private static readonly Color SwashFilm = new(0.85f, 0.94f, 1f, 0.16f);

    private Simulation? _sim;
    private AsciiMapView? _map;

    // one entry per map tile, row-major; rebuilt by MapChanged
    private Motion[] _motion = Array.Empty<Motion>();
    private float[] _dirX = Array.Empty<float>(), _dirY = Array.Empty<float>();
    /// <summary>Running water: speed in m/s. Surf: distance from land in tiles.</summary>
    private float[] _amount = Array.Empty<float>();
    /// <summary>Swash tiles: the kind of shore, and the map index of the surf tile the waves come from.</summary>
    private Shore[] _shore = Array.Empty<Shore>();
    private int[] _seaward = Array.Empty<int>();
    /// <summary>Water over coral (the generator's reef), where waves break offshore.</summary>
    private bool[] _reef = Array.Empty<bool>();
    /// <summary>How many plant tiles the wind could move, and whether the lamps had something to show last frame.</summary>
    private int _plants;
    private bool _lampEffects;
    private int _width, _height, _animated;

    private double _time, _sinceDraw;
    private Rect2 _onScreen;
    private bool _drewSomething;

    /// <summary>
    /// The map view's <see cref="AsciiMapView.LookOrigin"/> as this redraw began: each tile's beat, lane
    /// and glint go by its island position, so the water and the fences move the same way when you come back.
    /// </summary>
    private (int X, int Y) _look;

    /// <summary>How many tiles the last draw animated: for the debug readout.</summary>
    public int DrawnTiles { get; private set; }

    /// <summary>How long the last draw took, in milliseconds (smoothed), for the debug readout: the animation's frame cost.</summary>
    public double DrawMs { get; private set; }

    public void Bind(Simulation sim, AsciiMapView map)
    {
        _sim = sim;
        _map = map;
        MapChanged();
    }

    /// <summary>Tells the view what part of the map is on screen, in the map view's pixels. Call it every frame.</summary>
    public void SetView(Rect2 onScreen) => _onScreen = onScreen;

    /// <summary>
    /// Works out again what moves where. Call it when the map's water changes as a whole: at the
    /// start, and whenever the streamed map scrolls.
    /// </summary>
    public void MapChanged()
    {
        if (_sim is null)
            return;
        var map = _sim.Map;
        _width = map.Width;
        _height = map.Height;
        int n = _width * _height;
        if (_motion.Length != n)
        {
            _motion = new Motion[n];
            _dirX = new float[n];
            _dirY = new float[n];
            _amount = new float[n];
            _shore = new Shore[n];
            _seaward = new int[n];
            _reef = new bool[n];
        }
        _animated = 0;
        _plants = 0;

        // first pass: water or not, and which water is sea (water standing at sea level)
        var sea = new bool[n];
        var land = new bool[n];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var p = new TilePos(x, y);
                int i = y * _width + x;
                bool water = map.Cover[p] is CoverType.ShallowWater or CoverType.DeepWater;
                land[i] = !water;
                _motion[i] = Motion.None;
                _dirX[i] = _dirY[i] = _amount[i] = 0f;
                _shore[i] = Shore.None;
                _seaward[i] = -1;
                _reef[i] = water && _map is not null && _map.GroundAt(p) == Cover.Reef;
                if (!water)
                {
                    if (IsPlant(map.Cover[p]))
                    {
                        _motion[i] = Motion.Vegetation;
                        _plants++;
                    }
                    continue;
                }
                _animated++;
                WaterFlow flow = _sim.CurrentAt(p); // faster when the river is high
                if (!flow.IsNone)
                {
                    float speed = Mathf.Sqrt(flow.X * flow.X + flow.Y * flow.Y);
                    _motion[i] = Motion.Running;
                    _dirX[i] = flow.X / speed;
                    _dirY[i] = flow.Y / speed;
                    _amount[i] = speed / 10f;
                    continue;
                }
                _motion[i] = Motion.Still;
                // sea: water at sea level, or the sea's edge the tide has covered (beaches stand a little higher)
                sea[i] = map.Heights[p] == 0 || map.Water[p].Kind == WaterKind.Sea || map.Water[p].ShoreFacing is not null;
            }
        }

        // second pass: sea near land becomes surf, heading for the land around it
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int i = y * _width + x;
                if (!sea[i])
                    continue;
                float towardX = 0f, towardY = 0f, nearest = float.MaxValue;
                for (int dy = -SurfReach; dy <= SurfReach; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= _height)
                        continue;
                    for (int dx = -SurfReach; dx <= SurfReach; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= _width || !land[yy * _width + xx])
                            continue;
                        float d2 = dx * dx + dy * dy;
                        towardX += dx / d2;
                        towardY += dy / d2;
                        nearest = Mathf.Min(nearest, Mathf.Sqrt(d2));
                    }
                }
                float length = Mathf.Sqrt(towardX * towardX + towardY * towardY);
                if (nearest > SurfReach || length < 0.001f)
                    continue;
                _motion[i] = Motion.Surf;
                _dirX[i] = towardX / length;
                _dirY[i] = towardY / length;
                _amount[i] = nearest;
            }
        }

        // third pass: land touching surf is where the waves end (drawn in the Layered look only)
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int i = y * _width + x;
                if (!land[i])
                    continue;
                float towardX = 0f, towardY = 0f;
                int seaward = -1, best = int.MaxValue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if ((dx == 0 && dy == 0) || xx < 0 || yy < 0 || xx >= _width || yy >= _height)
                            continue;
                        int k = yy * _width + xx;
                        if (_motion[k] != Motion.Surf)
                            continue;
                        towardX -= dx;
                        towardY -= dy;
                        int rank = dx * dx + dy * dy; // a side neighbour before a corner one
                        if (rank < best)
                        {
                            best = rank;
                            seaward = k;
                        }
                    }
                }
                float length = Mathf.Sqrt(towardX * towardX + towardY * towardY);
                if (seaward < 0 || length < 0.001f)
                    continue;
                var p = new TilePos(x, y);
                var source = new TilePos(seaward % _width, seaward / _width);
                if (_motion[i] == Motion.Vegetation)
                    _plants--;
                _motion[i] = Motion.Swash;
                _dirX[i] = towardX / length;
                _dirY[i] = towardY / length;
                _seaward[i] = seaward;
                _shore[i] = ShoreOf(map.Cover[p], map.Heights[p] - map.Heights[source]);
                _animated++;
            }
        }
        QueueRedraw();
    }

    /// <summary>Whether the wind visibly moves this cover.</summary>
    private static bool IsPlant(CoverType cover) => cover is CoverType.Grass or CoverType.Scrub or CoverType.SparseJungle
        or CoverType.DenseJungle or CoverType.TallGrass or CoverType.Reeds or CoverType.Ferns;

    /// <summary>Whether gusts show now: a Layered look, full motion, and enough wind.</summary>
    private bool GustsShow(Simulation sim, AsciiMapView map) =>
        map.Overlays.Look >= LookMode.Layered && map.Overlays.Varied && map.Overlays.Motion == MotionLevel.Full
        && sim.Weather.Settings.WindSpeed >= GustFromWind;

    /// <summary>How the waves end on a tile of shore: by what it is, and by how far it stands above the water.</summary>
    private static Shore ShoreOf(CoverType cover, int stepDm) => cover switch
    {
        _ when stepDm >= 10 => Shore.Rocky,
        CoverType.Rock or CoverType.BareRock or CoverType.Lava => Shore.Rocky,
        CoverType.Wall or CoverType.Floor or CoverType.Track or CoverType.Door or CoverType.Fence or CoverType.WeakFence
            or CoverType.BrokenFence or CoverType.Support or CoverType.Equipment or CoverType.Vehicle or CoverType.Rubble => Shore.Built,
        _ => Shore.Beach,
    };

    public override void _Process(double delta)
    {
        _sinceDraw += delta;
        if (_sim is null || _map is null)
            return;
        // reduced motion runs everything at half speed; still stops the clock
        var motion = _map.Overlays.Motion;
        _time += motion == MotionLevel.Full ? delta : motion == MotionLevel.Reduced ? delta * 0.5 : 0;
        bool on = _map.Overlays.Animation && motion != MotionLevel.Still
            && (_animated > 0 || _sim.LiveFenceTiles.Count > 0 || _sim.Fires.Count > 0 || _lampEffects
                || _plants > 0 && GustsShow(_sim, _map));
        // one more redraw after it's switched off (or the water scrolls away) clears what was drawn
        if ((on || _drewSomething) && _sinceDraw >= 1.0 / FramesPerSecond)
        {
            _sinceDraw = 0;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        DrawAll();
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        DrawMs = DrawMs == 0 ? ms : DrawMs * 0.8 + ms * 0.2;
    }

    private void DrawAll()
    {
        _drewSomething = false;
        DrawnTiles = 0;
        _lampEffects = false;
        if (_sim is null || _map is null || !_map.Overlays.Animation || _map.Overlays.Motion == MotionLevel.Still)
            return;
        var sim = _sim;
        var map = _map;
        _look = map.LookOrigin;
        DrawFenceSparks(sim, map, (float)(_time % 3600.0));
        DrawFire(sim, map, (float)(_time % 3600.0));
        DrawLamps(sim, map, (float)(_time % 3600.0));
        bool gusts = _plants > 0 && GustsShow(sim, map);
        if (_animated == 0 && !gusts)
            return;
        if (sim.Map.Width != _width || sim.Map.Height != _height)
            return; // the map changed size under us; MapChanged will catch up

        // the tiles that can be on screen: in the raised view, ground south of the screen is lifted into it
        int x0 = Math.Max(0, Mathf.FloorToInt(_onScreen.Position.X / Cell));
        int x1 = Math.Min(_width - 1, Mathf.FloorToInt(_onScreen.End.X / Cell));
        int y0 = Math.Max(0, Mathf.FloorToInt(_onScreen.Position.Y / Cell));
        int y1 = Math.Min(_height - 1, Mathf.FloorToInt((_onScreen.End.Y + map.MaxLift) / Cell));
        if (x1 < x0 || y1 < y0)
            return;

        int stride = Math.Max(1, (x1 - x0 + 1) * (y1 - y0 + 1) / TileBudget);
        var weather = sim.Weather.Settings;
        float wind = weather.WindSpeed;
        int rain = weather.RainPercent;
        bool lighting = map.Overlays.Lighting;
        bool raised = map.Overlays.Raised;
        bool layered = map.Overlays.Look >= LookMode.Layered && map.Overlays.Varied;
        bool expressive = layered && map.Overlays.Look == LookMode.Expressive;
        bool atmosphere = layered && map.Overlays.Motion == MotionLevel.Full; // swash, spray and reef crests are for atmosphere only
        var (downX, downY) = sim.Weather.Downwind;
        var downwind = new Vector2(downX, downY) / 1000f;
        float gustStrength = Mathf.Clamp((wind - GustFromWind) / (GustFullWind - GustFromWind), 0f, 1f);
        int swell = sim.Waters is { } waters ? waters.SwellPercent(sim.GameMinute(sim.CurrentTick)) : 0;
        float time = (float)(_time % 3600.0); // kept small so the fractions below stay smooth

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int i = y * _width + x;
                Motion motion = _motion[i];
                if (motion == Motion.None || motion == Motion.Swash && !atmosphere || motion == Motion.Vegetation && !gusts)
                    continue;
                if (stride > 1 && MapLook.Hash(x + _look.X, y + _look.Y, 31) % (uint)stride != 0)
                    continue;
                var p = new TilePos(x, y);
                if (map.FogAt(p) != FogState.Visible || map.RoofShown(p))
                    continue;
                // moving water under somebody's feet would be drawn across them
                if (sim.ActorAt(p) is { IsGone: false })
                    continue;

                float top = map.CenterOf(p).Y - Cell / 2f;
                if (top > _onScreen.End.Y || top + Cell < _onScreen.Position.Y)
                    continue;
                // in the raised view the bank to the south may stand in front of this tile's lower part
                float bottom = top + Cell;
                if (raised)
                {
                    for (int k = 1; k <= 2 && y + k < _height; k++)
                        bottom = Mathf.Min(bottom, map.CenterOf(new TilePos(x, y + k)).Y - Cell / 2f);
                    if (bottom - top < 5f)
                        continue;
                }

                float lit = lighting ? 0.12f + 0.88f * Mathf.Min(100, sim.Light.LevelAt(p)) / 100f : 1f;
                var cell = new Rect2(x * Cell, top, Cell, bottom - top);
                switch (motion)
                {
                    case Motion.Still:
                        DrawGlint(cell, x, y, time, wind, lit);
                        break;
                    case Motion.Running:
                        DrawStreak(cell, x, y, 0, time, i, lit);
                        if (_amount[i] > 1.8f)
                            DrawStreak(cell, x, y, 1, time, i, lit);
                        break;
                    case Motion.Surf:
                        DrawWave(cell, x, y, time, wind, i, lit, sim.SurfPercentAt(p));
                        break;
                    case Motion.Swash:
                    {
                        int sea = _seaward[i];
                        DrawSwash(cell, i, sea % _width, sea / _width, time, wind, lit,
                            sim.SurfPercentAt(new TilePos(sea % _width, sea / _width)));
                        break;
                    }
                    case Motion.Vegetation:
                        if (IsPlant(sim.Cover[p])) // it may have burnt since
                            DrawGust(cell, x, y, time, wind, downwind, gustStrength, expressive, lit);
                        break;
                }
                if (atmosphere && _reef[i] && sim.Cover[p] == CoverType.ShallowWater)
                    DrawReefBreak(cell, x, y, time, lit, swell);
                if (rain > 0 && motion is not (Motion.Swash or Motion.Vegetation))
                    DrawRainRing(cell, x, y, time, rain, lit);
                DrawnTiles++;
            }
        }
        _drewSomething |= DrawnTiles > 0; // the fence sparks may have drawn too
    }

    /// <summary>
    /// Now and then a live fence tile in view crackles: a jagged blue-white spark for a tenth of a
    /// second, every few seconds, each tile on its own beat.
    /// </summary>
    private void DrawFenceSparks(Simulation sim, AsciiMapView map, float time)
    {
        if (sim.LiveFenceTiles.Count == 0)
            return;
        foreach (TilePos p in sim.LiveFenceTiles)
        {
            float period = 2.5f + 3f * Unit(p.X, p.Y, 41);
            float u = Frac(time / period + Unit(p.X, p.Y, 42));
            if (u > 0.04f)
                continue;
            if (map.FogAt(p) != FogState.Visible)
                continue;
            var c = map.CenterOf(p);
            if (!_onScreen.HasPoint(c))
                continue;
            float a = 1f - u / 0.04f;
            var ink = Faded(Spark, a);
            var p0 = c + new Vector2(-6f, -2f);
            var p1 = c + new Vector2(-2f, 3f);
            var p2 = c + new Vector2(2f, -3f);
            var p3 = c + new Vector2(6f, 2f);
            DrawLine(p0, p1, ink, 1.5f);
            DrawLine(p1, p2, ink, 1.5f);
            DrawLine(p2, p3, ink, 1.5f);
            _drewSomething = true;
        }
    }

    /// <summary>
    /// Fire: a glow and flickering tongues of flame on every burning tile in view (taller the hotter),
    /// embers lifting off it, and smoke puffs drifting downwind over the tiles the smoke lies on.
    /// </summary>
    private void DrawFire(Simulation sim, AsciiMapView map, float time)
    {
        if (sim.Fires.Count == 0)
            return;
        var (wx, wy) = sim.Weather.Downwind;
        var drift = new Vector2(wx, wy) / 1000f;

        // smoke first, under the flames: only over the part of the map on screen
        int x0 = Math.Max(0, Mathf.FloorToInt(_onScreen.Position.X / Cell)), x1 = Math.Min(sim.Map.Width - 1, Mathf.FloorToInt(_onScreen.End.X / Cell));
        int y0 = Math.Max(0, Mathf.FloorToInt(_onScreen.Position.Y / Cell)), y1 = Math.Min(sim.Map.Height - 1, Mathf.FloorToInt(_onScreen.End.Y / Cell) + 2);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var p = new TilePos(x, y);
                int smoke = sim.Map.Smoke[p];
                if (smoke <= 0 || map.FogAt(p) != FogState.Visible)
                    continue;
                float u = Frac(time / (3f + 2f * Unit(x, y, 51)) + Unit(x, y, 52));
                var c = map.CenterOf(p) + (drift * u + new Vector2(0f, -0.4f * u)) * Cell;
                DrawCircle(c, Cell * (0.35f + 0.35f * u), Faded(SmokePuff, smoke / 100f * 0.55f * (1f - u * 0.6f)));
                _drewSomething = true;
            }
        }

        foreach (Fire fire in sim.Fires)
        {
            TilePos p = fire.Position;
            if (map.FogAt(p) != FogState.Visible)
                continue;
            var c = map.CenterOf(p);
            if (!_onScreen.Grow(Cell).HasPoint(c))
                continue;
            float heat = fire.Intensity / 100f;
            float flicker = 0.75f + 0.25f * Mathf.Sin(time * 11f + 7f * Unit(p.X, p.Y, 53));
            DrawRect(new Rect2(c - new Vector2(Cell / 2f, Cell / 2f), new Vector2(Cell, Cell)), Faded(FlameGlow, (0.3f + 0.7f * heat) * flicker));
            for (int k = 0; k < 3; k++)
            {
                float jitter = Mathf.Sin(time * (9f + 3f * k) + 13f * Unit(p.X, p.Y, 54 + k));
                float tall = Cell * (0.35f + 0.65f * heat) * (0.7f + 0.3f * jitter);
                var foot = c + new Vector2((k - 1) * Cell * 0.28f, Cell * 0.4f);
                var tip = foot + new Vector2(jitter * Cell * 0.12f + drift.X * Cell * 0.2f, -tall);
                DrawLine(foot, tip, Faded(k == 1 ? FlameCore : FlameEdge, 0.6f + 0.4f * heat), k == 1 ? 2.5f : 1.8f);
            }
            // an ember rising off it now and then
            float e = Frac(time / 1.4f + Unit(p.X, p.Y, 58));
            if (e < 0.6f && heat > 0.3f)
            {
                var ember = c + new Vector2((Unit(p.X, p.Y, 59) - 0.5f) * Cell + drift.X * e * Cell, -e * Cell * 1.6f + drift.Y * e * Cell);
                DrawRect(new Rect2(ember, new Vector2(1.5f, 1.5f)), Faded(Ember, 1f - e / 0.6f));
            }
            _drewSomething = true;
        }
    }

    /// <summary>A short bright dash that fades in and out and drifts a little: light on slow water.</summary>
    private void DrawGlint(Rect2 cell, int x, int y, float time, float wind, float lit)
    {
        float period = (3.5f + 3f * Unit(x, y, 11)) / (1f + wind / 10f);
        float u = Frac(time / period + Unit(x, y, 12));
        const float shown = 0.35f;
        if (u > shown)
            return;
        float a = Mathf.Sin(Mathf.Pi * u / shown);
        float gy = cell.Position.Y + (0.2f + 0.6f * Unit(x, y, 13)) * Cell;
        if (gy > cell.End.Y - 1f)
            return;
        float length = 5f + 4f * Unit(x, y, 14);
        float gx = cell.Position.X + 2f + (Cell - length - 4f) * Unit(x, y, 15) + 3f * u / shown;
        DrawLine(new Vector2(gx, gy), new Vector2(gx + length, gy), Faded(Glint, a * lit), 1f);
    }

    /// <summary>A streak carried across the tile by the current, faster and whiter in fast water.</summary>
    private void DrawStreak(Rect2 cell, int x, int y, int which, float time, int i, float lit)
    {
        float speed = Mathf.Clamp(_amount[i], 0.3f, 4f);           // m/s; a tile is a metre
        const float travel = Cell * 0.8f;                           // how far a streak runs before it fades
        float u = Frac(time * speed * Cell / travel * 0.6f + Unit(x, y, 16 + which * 4));
        var dir = new Vector2(_dirX[i], _dirY[i]);
        var across = new Vector2(-dir.Y, dir.X);
        var center = new Vector2(cell.Position.X + Cell / 2f, cell.Position.Y + Cell / 2f);
        float lane = (Unit(x, y, 17 + which * 4) - 0.5f) * Cell * 0.7f;
        float length = 4f + 2.5f * speed;
        var head = center + across * lane + dir * ((u - 0.5f) * travel);
        var tail = head - dir * length;
        if (!Inside(cell, head) || !Inside(cell, tail))
            return;
        float a = Mathf.Sin(Mathf.Pi * u);
        var ink = speed > 2.5f ? WhiteWater : Streak;
        DrawLine(tail, head, Faded(ink, a * lit), speed > 2.5f ? 1.5f : 1f);
    }

    /// <summary>
    /// One wave front crossing the surf zone toward the land. Every tile works out where the front is
    /// from the time and its own distance from the shore, so neighbouring tiles draw one moving line.
    /// </summary>
    /// <param name="surf">How big the surf is here (Simulation.SurfPercentAt): bigger waves are brighter and break white
    /// farther out; 0 off the island's coast, where the waves keep their old look.</param>
    private void DrawWave(Rect2 cell, int x, int y, float time, float wind, int i, float lit, int surf)
    {
        float size = surf > 0 ? 0.35f + 0.65f * surf / 100f : 0.6f;
        float period = WavePeriod / (1f + wind / 12f) * (surf > 0 ? 1.3f - 0.5f * surf / 100f : 1f);
        // a slow wobble along the coast, so the whole beach doesn't break at the same instant
        float offset = 0.15f * Mathf.Sin((x + _look.X + y + _look.Y) * 0.35f);
        float front = SurfReach * (1f - Frac(time / period + offset));   // tiles from the land
        float distance = _amount[i];
        float local = distance - front;                                    // 0 at the tile's seaward edge, 1 at its landward edge
        if (local < 0f || local >= 1f)
            return;
        var dir = new Vector2(_dirX[i], _dirY[i]);
        var along = new Vector2(-dir.Y, dir.X);
        var center = new Vector2(cell.Position.X + Cell / 2f, cell.Position.Y + Cell / 2f);
        var mid = center + dir * ((local - 0.5f) * Cell);
        var a = mid - along * (Cell / 2f);
        var b = mid + along * (Cell / 2f);
        if (!Inside(cell, a) && !Inside(cell, b))
            return;
        a = Clamped(cell, a);
        b = Clamped(cell, b);
        // waves grow as they come in and break white on the last tile (big surf breaks a tile farther out)
        float grown = 1f - front / SurfReach;
        bool breaking = distance < (surf >= 60 ? 2.6f : 1.6f);
        DrawLine(a, b, Faded(breaking ? Foam : Wave, (0.25f + 0.75f * grown) * lit * size * 1.6f), breaking ? (surf >= 60 ? 3f : 2f) : 1f);
    }

    /// <summary>A ring spreading where a raindrop lands. More tiles ring at once in heavier rain.</summary>
    /// <summary>
    /// A gust passing over plants: bands of lighter leaves (the undersides turning) sweep downwind,
    /// one every <see cref="GustSpacing"/> tiles, faster and brighter in a stronger wind and patchy along
    /// their front. In the Expressive look they're stronger and blow a few leaves along.
    /// </summary>
    private void DrawGust(Rect2 cell, int x, int y, float time, float wind, Vector2 downwind, float strength, bool expressive, float lit)
    {
        float ax = x + _look.X, ay = y + _look.Y;
        float along = ax * downwind.X + ay * downwind.Y;
        float across = -ax * downwind.Y + ay * downwind.X;
        float speed = 1.5f + 0.35f * wind; // tiles a second
        float wave = Mathf.Sin(Mathf.Tau * (along - time * speed) / GustSpacing);
        if (wave <= 0f)
            return;
        float band = Mathf.Pow(wave, 8f);
        float patch = 0.5f + 0.5f * Mathf.Sin(across / 23f + time * 0.05f + 1.7f * Mathf.Sin(across / 61f));
        float alpha = band * patch * strength * (expressive ? 0.16f : 0.09f) * lit;
        if (alpha > 0.012f)
            DrawRect(cell, Faded(Gust, alpha));
        if (!expressive || band < 0.55f || Unit(x, y, 95) >= 0.12f)
            return;
        // a leaf carried across the tile with the gust
        float u = Frac(time * 0.9f + Unit(x, y, 96));
        var center = new Vector2(cell.Position.X + Cell / 2f, cell.Position.Y + Cell / 2f);
        var at = center + downwind * ((u - 0.5f) * Cell) + new Vector2(-downwind.Y, downwind.X) * ((Unit(x, y, 97) - 0.5f) * Cell * 0.6f);
        if (Inside(cell, at))
            DrawRect(new Rect2(at.X - 1f, at.Y - 1f, 2f, 2f), Faded(Leaf, band * strength * lit));
    }

    /// <summary>
    /// What the facilities' lamps show of their power, in a Layered look (the power plan's lamps only:
    /// carried lights, fires and flares are left alone). A ceiling light at a site running on its
    /// batteries below <see cref="FlickerBelowPercent"/>% charge flickers (full motion) or stays dimmed
    /// (reduced motion: nothing flashes). In the Expressive look a lit red emergency light sends out a
    /// slow pulse. Only lamps in view, as the map draws them.
    /// </summary>
    private void DrawLamps(Simulation sim, AsciiMapView map, float time)
    {
        if (sim.Power is not { } grid || grid.Lamps.Count == 0 || map.Overlays.Look < LookMode.Layered || !map.Overlays.Varied)
            return;
        bool full = map.Overlays.Motion == MotionLevel.Full;
        bool expressive = map.Overlays.Look == LookMode.Expressive && full;
        foreach (PowerLamp lamp in grid.Lamps)
        {
            if (!lamp.Lit || lamp.Kind == LampKind.Flood || lamp.Kind == LampKind.Emergency && !expressive)
                continue;
            var p = sim.FromIsland(lamp.X, lamp.Y);
            if (!sim.Map.InBounds(p) || map.FogAt(p) != FogState.Visible || map.RoofShown(p))
                continue;
            var center = map.CenterOf(p);
            if (!_onScreen.Grow(Cell * 2f).HasPoint(center))
                continue;
            if (lamp.Kind == LampKind.Emergency)
            {
                _lampEffects = true;
                float k = Frac(time / 2.4f + Unit(p.X, p.Y, 102));
                DrawArc(center, Cell * (0.4f + 1.6f * k), 0f, Mathf.Tau, 24, Faded(Beacon, 1f - k), 1.5f);
                _drewSomething = true;
                continue;
            }
            if (lamp.SiteId < 0 || lamp.SiteId >= grid.Sites.Count)
                continue;
            var site = grid.Sites[lamp.SiteId];
            if (site.Source != PowerSource.Battery || grid.BatteryOf(site) is not { ChargePercent: < FlickerBelowPercent })
                continue;
            _lampEffects = true;
            float dim;
            if (full)
            {
                // short drop-outs at an irregular beat, each lamp on its own
                float u = Frac(time / (1.3f + Unit(p.X, p.Y, 100)) + Unit(p.X, p.Y, 101));
                dim = (u < 0.06f || (u is > 0.11f and < 0.14f)) ? 1f : (u is > 0.40f and < 0.43f) ? 0.5f : 0f;
            }
            else
            {
                dim = 0.45f;
            }
            if (dim > 0f)
                DrawCircle(center, Cell * 1.4f, Faded(LampDim, dim));
            _drewSomething = true;
        }
    }

    /// <summary>
    /// The end of a wave on a tile of shore, in time with the surf tile it comes from (<paramref name="sx"/>,
    /// <paramref name="sy"/>): just as the wave line there reaches the land, foam runs up a beach and back
    /// with a thin film behind it and the sand darkens wet for a moment; on rock or a structure it bursts
    /// into spray thrown up and landward.
    /// </summary>
    private void DrawSwash(Rect2 cell, int i, int sx, int sy, float time, float wind, float lit, int surf)
    {
        float size = surf > 0 ? 0.35f + 0.65f * surf / 100f : 0.6f;
        float period = WavePeriod / (1f + wind / 12f) * (surf > 0 ? 1.3f - 0.5f * surf / 100f : 1f);
        float offset = 0.15f * Mathf.Sin((sx + _look.X + sy + _look.Y) * 0.35f);
        float s = Frac(time / period + offset); // 0 the moment the wave reaches the land (see DrawWave)
        var dir = new Vector2(_dirX[i], _dirY[i]);
        var along = new Vector2(-dir.Y, dir.X);
        var center = new Vector2(cell.Position.X + Cell / 2f, cell.Position.Y + Cell / 2f);
        var edge = center - dir * (Cell / 2f);   // the seaward side of the tile
        int x = i % _width, y = i / _width;

        if (_shore[i] == Shore.Beach)
        {
            const float run = 0.35f, wet = 0.65f;
            if (s < wet)
                DrawRect(cell, Faded(WetSheen, (1f - s / wet) * lit * 1.5f));
            if (s >= run)
                return;
            float reach = 0.7f * size * Mathf.Sin(Mathf.Pi * s / run);
            var mid = edge + dir * (reach * Cell);
            Vector2 a = Clamped(cell, mid - along * (Cell / 2f)), b = Clamped(cell, mid + along * (Cell / 2f));
            Vector2 ea = Clamped(cell, edge - along * (Cell / 2f)), eb = Clamped(cell, edge + along * (Cell / 2f));
            DrawColoredPolygon(new[] { ea, eb, b, a }, Faded(SwashFilm, (1f - s / run) * lit * 1.5f));
            DrawLine(a, b, Faded(Foam, (1f - s / run) * lit * size * 1.4f), 1.5f);
            return;
        }

        // rock or a structure: a white flash along the seaward side, then spray in an arc
        const float burst = 0.3f;
        if (s >= burst)
            return;
        float t = s / burst;
        if (t < 0.3f)
            DrawLine(Clamped(cell, edge - along * (Cell / 2f)), Clamped(cell, edge + along * (Cell / 2f)),
                Faded(Foam, (1f - t / 0.3f) * lit * size * 1.6f), 2f);
        int drops = _shore[i] == Shore.Rocky ? 6 : 3;
        float height = Cell * (_shore[i] == Shore.Rocky ? 0.9f : 0.45f) * size;
        for (int k = 0; k < drops; k++)
        {
            float angle = (Unit(x, y, 70 + k) - 0.5f) * 1.8f;
            float speed = 0.5f + 0.5f * Unit(x, y, 80 + k);
            var heading = dir.Rotated(angle);
            var at = edge + heading * (t * Cell * 0.8f * speed) - new Vector2(0f, 4f * height * speed * t * (1f - t));
            DrawCircle(at, 1.2f + 0.8f * Unit(x, y, 90 + k), Faded(WhiteWater, (1f - t) * lit * size * 1.3f));
        }
    }

    /// <summary>
    /// Waves breaking over a reef, out from the shore: now and then a short white crest crosses a reef
    /// tile in the swell's direction, more often and brighter in a big swell. Half the reef's tiles break.
    /// </summary>
    private void DrawReefBreak(Rect2 cell, int x, int y, float time, float lit, int swell)
    {
        if (Unit(x, y, 64) >= 0.5f || _sim?.Waters is not { } waters)
            return;
        const float duty = 0.12f;
        float period = 7f + 5f * Unit(x, y, 61);
        float u = Frac(time / period + Unit(x, y, 62));
        if (u >= duty)
            return;
        var (dx, dy) = waters.SwellFrom.Delta();
        var travel = new Vector2(-dx, -dy).Normalized(); // the swell runs away from where it comes from
        var along = new Vector2(-travel.Y, travel.X);
        float k = u / duty;
        var center = new Vector2(cell.Position.X + Cell / 2f, cell.Position.Y + Cell / 2f) + travel * ((k - 0.5f) * Cell * 0.6f);
        float half = Cell * 0.35f * (0.6f + 0.4f * Unit(x, y, 63));
        DrawLine(Clamped(cell, center - along * half), Clamped(cell, center + along * half),
            Faded(WhiteWater, Mathf.Sin(Mathf.Pi * k) * lit * (0.25f + 0.75f * swell / 100f) * 1.4f), 2f);
    }

    private void DrawRainRing(Rect2 cell, int x, int y, float time, int rain, float lit)
    {
        const float life = 0.9f;
        float clock = time / life + Unit(x, y, 21);
        int drop = Mathf.FloorToInt(clock);
        if (MapLook.Hash((x + _look.X) * 7919 + drop, y + _look.Y, 22) % 100 >= (uint)(rain / 4))
            return;
        float u = clock - drop;
        var at = new Vector2(cell.Position.X + 4f + (Cell - 8f) * Unit(x + drop, y, 23), cell.Position.Y + 4f + (Cell - 8f) * Unit(x, y + drop, 24));
        float radius = 1.5f + 5f * u;
        if (at.Y + radius > cell.End.Y)
            return;
        DrawArc(at, radius, 0f, Mathf.Tau, 10, Faded(RainRing, (1f - u) * lit), 1f);
    }

    private static bool Inside(Rect2 cell, Vector2 point) =>
        point.X >= cell.Position.X && point.X <= cell.End.X && point.Y >= cell.Position.Y && point.Y <= cell.End.Y;

    private static Vector2 Clamped(Rect2 cell, Vector2 point) =>
        new(Mathf.Clamp(point.X, cell.Position.X, cell.End.X), Mathf.Clamp(point.Y, cell.Position.Y, cell.End.Y));

    private static Color Faded(Color c, float by) => new(c.R, c.G, c.B, c.A * Mathf.Clamp(by, 0f, 1f));

    private static float Frac(float v) => v - Mathf.Floor(v);

    /// <summary>A fixed value in [0, 1) for a map tile and a channel, by the tile's island position.</summary>
    private float Unit(int x, int y, int channel) => MapLook.Hash(x + _look.X, y + _look.Y, channel) % 1000 / 1000f;
}
