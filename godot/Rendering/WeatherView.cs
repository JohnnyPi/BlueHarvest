#nullable enable
using BlueHarvest.Sim;
using BlueHarvest.Sim.World;
using Godot;
using Sky = BlueHarvest.Sim.World.Sky; // Godot has a Sky of its own

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// Rain streaks slanted by the wind, a grey veil for fog, and in daylight under broken cloud, soft
/// cloud shadows drifting downwind, drawn over the part of the map on screen. Purely for looks: what
/// rain, fog and cloud do to the senses and the light happens in the simulation.
/// Add it after the AsciiMapView so it draws on top.
/// </summary>
public partial class WeatherView : Node2D
{
    /// <summary>Streaks per screen cell at 100% rain.</summary>
    private const float DropsPerCellAtFullRain = 0.5f;
    private const int MaxDrops = 3000;
    private const float FallSpeed = 900f;     // pixels per second
    private const float StreakLength = 16f;

    private static readonly Color RainInk = new(0.70f, 0.78f, 0.95f, 0.38f);

    /// <summary>Cloud shadows sit one to a lattice cell this many pixels across (14 tiles).</summary>
    private const float ShadowCell = 14f * AsciiMapView.CellSize;

    /// <summary>How fast shadows drift, in pixels per real second per m/s of wind.</summary>
    private const float ShadowDriftPerWind = 5f;

    /// <summary>How dark the middle of a shadow is in full sun.</summary>
    private const float ShadowDarkness = 0.22f;

    /// <summary>With more lattice cells than this on screen (zoomed far out), shadows aren't drawn.</summary>
    private const int MaxShadowCells = 600;

    private Simulation? _sim;
    private OverlayState? _overlays;
    private float _time;
    private bool _drewSomething;

    /// <summary>How far the shadow lattice has slid downwind so far, in pixels. Grows with the wind, so a change of speed never makes it jump.</summary>
    private Vector2 _shadowDrift;

    /// <param name="overlays">The map's settings, for its motion level (reduced runs at half speed, still stops); none moves at full speed.</param>
    public void Bind(Simulation sim, OverlayState? overlays = null)
    {
        _sim = sim;
        _overlays = overlays;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float pace = _overlays?.Motion switch
        {
            MotionLevel.Reduced => 0.5f,
            MotionLevel.Still => 0f,
            _ => 1f,
        };
        _time += (float)delta * pace;
        if (_sim is null)
            return;
        var settings = _sim.Weather.Settings;
        var (dx, dy) = settings.WindFrom.Opposite().Delta();
        _shadowDrift += new Vector2(dx, dy).Normalized() * settings.WindSpeed * ShadowDriftPerWind * (float)delta * pace;
        bool active = settings.RainPercent > 0 || settings.FogPercent > 0 || ShadowStrength() > 0f;
        // Keep animating while it rains; one more redraw clears the screen when the weather clears.
        if (active || _drewSomething)
            QueueRedraw();
    }

    public override void _Draw()
    {
        _drewSomething = false;
        if (_sim is null)
            return;

        var settings = _sim.Weather.Settings;
        var view = GetViewport().GetCanvasTransform().AffineInverse() * GetViewportRect();

        float shadow = ShadowStrength();
        if (shadow > 0f)
            DrawCloudShadows(view, settings.CloudPercent, shadow);

        if (settings.FogPercent > 0)
        {
            DrawRect(view, new Color(0.62f, 0.64f, 0.68f, settings.FogPercent * 0.004f));
            _drewSomething = true;
        }

        if (settings.RainPercent <= 0)
            return;

        // Rain falls down the screen and drifts the way the wind blows: across with an east or west
        // wind, a little faster or slower down the screen with a south or north one.
        var (wx, wy) = settings.WindFrom.Opposite().Delta();
        var velocity = new Vector2(wx * settings.WindSpeed * 40f, FallSpeed + wy * settings.WindSpeed * 25f);
        var streak = velocity.Normalized() * StreakLength;

        float cells = view.Size.X * view.Size.Y / (AsciiMapView.CellSize * AsciiMapView.CellSize);
        int drops = Mathf.Min(MaxDrops, (int)(cells * DropsPerCellAtFullRain * settings.RainPercent / 100f));
        for (int i = 0; i < drops; i++)
        {
            // Each drop has a fixed spot in a screen-sized tile that scrolls with time, so the rain
            // keeps falling without storing particles.
            float x = Mathf.PosMod(Hash(i, 1) * view.Size.X + velocity.X * _time, view.Size.X);
            float y = Mathf.PosMod(Hash(i, 2) * view.Size.Y + velocity.Y * _time * (0.8f + 0.4f * Hash(i, 3)), view.Size.Y);
            var at = view.Position + new Vector2(x, y);
            DrawLine(at, at + streak, RainInk, 1f);
        }
        _drewSomething = true;
    }

    /// <summary>
    /// How dark cloud shadows are now, 0 for none: only by day, under broken cloud. A clear sky has
    /// no clouds to cast them and an overcast one has no gaps between them (the sky's light is
    /// already dimmed for that).
    /// </summary>
    private float ShadowStrength()
    {
        if (_sim is not { Clock: { } clock } sim)
            return 0f;
        int cloud = sim.Weather.Settings.CloudPercent;
        if (cloud < 10 || sim.Weather.Settings.RainPercent > 0)
            return 0f;
        float sun = Sky.SunPercent(clock.MinuteOfDay(sim.CurrentTick)) / 100f;
        float gaps = Mathf.Clamp((95 - cloud) / 15f, 0f, 1f);
        return ShadowDarkness * sun * gaps;
    }

    /// <summary>
    /// Soft dark patches, one in some cells of a lattice fixed to the island (more of them the
    /// cloudier it is), sliding downwind. Each is a few stacked translucent discs for a soft edge.
    /// </summary>
    private void DrawCloudShadows(Rect2 view, int cloudPercent, float darkness)
    {
        // where the island's (0, 0) would be, so the lattice doesn't jump when the map scrolls
        var origin = _sim!.WindowOrigin;
        var island = new Vector2(origin.X, origin.Y) * AsciiMapView.CellSize - _shadowDrift;
        int i0 = Mathf.FloorToInt((view.Position.X + island.X) / ShadowCell) - 1;
        int i1 = Mathf.FloorToInt((view.End.X + island.X) / ShadowCell) + 1;
        int j0 = Mathf.FloorToInt((view.Position.Y + island.Y) / ShadowCell) - 1;
        int j1 = Mathf.FloorToInt((view.End.Y + island.Y) / ShadowCell) + 1;
        if ((i1 - i0 + 1) * (j1 - j0 + 1) > MaxShadowCells)
            return;
        float chance = Mathf.Min(0.85f, cloudPercent / 100f);
        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                if (CellUnit(i, j, 1) >= chance)
                    continue;
                var center = new Vector2((i + CellUnit(i, j, 2)) * ShadowCell, (j + CellUnit(i, j, 3)) * ShadowCell) - island;
                float radius = ShadowCell * (0.55f + 0.5f * CellUnit(i, j, 4));
                for (int ring = 0; ring < 4; ring++)
                    DrawCircle(center, radius * (1f - ring * 0.18f), new Color(0.02f, 0.03f, 0.06f, darkness / 4f));
            }
        }
        _drewSomething = true;
    }

    /// <summary>A fixed value in [0, 1) for a lattice cell and a channel, from integer hashing (stable for any cell).</summary>
    private static float CellUnit(int i, int j, int channel)
    {
        unchecked
        {
            uint h = (uint)i * 0x8DA6B343u ^ (uint)j * 0xD8163841u ^ (uint)channel * 0xCB1AB31Fu;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>A fixed pseudo-random value in [0, 1) for a drop and a channel.</summary>
    private static float Hash(int index, int channel)
    {
        float v = Mathf.Sin(index * 12.9898f + channel * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }
}
