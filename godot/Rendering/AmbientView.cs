#nullable enable
using System;
using BlueHarvest.Sim;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// Everything on the map that moves on its own: for now, water. Glints on lakes and the open sea,
/// streaks running down rivers, wave lines coming in to the shore, and rings where rain hits water.
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

    private Simulation? _sim;
    private AsciiMapView? _map;

    // one entry per map tile, row-major; rebuilt by MapChanged
    private Motion[] _motion = Array.Empty<Motion>();
    private float[] _dirX = Array.Empty<float>(), _dirY = Array.Empty<float>();
    /// <summary>Running water: speed in m/s. Surf: distance from land in tiles.</summary>
    private float[] _amount = Array.Empty<float>();
    private int _width, _height, _animated;

    private double _time, _sinceDraw;
    private Rect2 _onScreen;
    private bool _drewSomething;

    /// <summary>How many tiles the last draw animated: for the debug readout.</summary>
    public int DrawnTiles { get; private set; }

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
        }
        _animated = 0;

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
                if (!water)
                    continue;
                _animated++;
                WaterFlow flow = map.Flow[p];
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
                sea[i] = map.Heights[p] == 0;
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
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _sinceDraw += delta;
        if (_sim is null || _map is null)
            return;
        bool on = _map.Overlays.Animation && _animated > 0;
        // one more redraw after it's switched off (or the water scrolls away) clears what was drawn
        if ((on || _drewSomething) && _sinceDraw >= 1.0 / FramesPerSecond)
        {
            _sinceDraw = 0;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        _drewSomething = false;
        DrawnTiles = 0;
        if (_sim is null || _map is null || !_map.Overlays.Animation || _animated == 0)
            return;
        var sim = _sim;
        var map = _map;
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
        float time = (float)(_time % 3600.0); // kept small so the fractions below stay smooth

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int i = y * _width + x;
                Motion motion = _motion[i];
                if (motion == Motion.None)
                    continue;
                if (stride > 1 && MapLook.Hash(x, y, 31) % (uint)stride != 0)
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
                        DrawWave(cell, x, y, time, wind, i, lit);
                        break;
                }
                if (rain > 0)
                    DrawRainRing(cell, x, y, time, rain, lit);
                DrawnTiles++;
            }
        }
        _drewSomething = DrawnTiles > 0;
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
    private void DrawWave(Rect2 cell, int x, int y, float time, float wind, int i, float lit)
    {
        float period = WavePeriod / (1f + wind / 12f);
        // a slow wobble along the coast, so the whole beach doesn't break at the same instant
        float offset = 0.15f * Mathf.Sin((x + y) * 0.35f);
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
        // waves grow as they come in and break white on the last tile
        float grown = 1f - front / SurfReach;
        bool breaking = distance < 1.6f;
        DrawLine(a, b, Faded(breaking ? Foam : Wave, (0.25f + 0.75f * grown) * lit), breaking ? 2f : 1f);
    }

    /// <summary>A ring spreading where a raindrop lands. More tiles ring at once in heavier rain.</summary>
    private void DrawRainRing(Rect2 cell, int x, int y, float time, int rain, float lit)
    {
        const float life = 0.9f;
        float clock = time / life + Unit(x, y, 21);
        int drop = Mathf.FloorToInt(clock);
        if (MapLook.Hash(x * 7919 + drop, y, 22) % 100 >= (uint)(rain / 4))
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

    /// <summary>A fixed value in [0, 1) for a tile and a channel.</summary>
    private static float Unit(int x, int y, int channel) => MapLook.Hash(x, y, channel) % 1000 / 1000f;
}
