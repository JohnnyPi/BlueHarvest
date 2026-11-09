#nullable enable
using BlueHarvest.Sim;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// Rain streaks slanted by the wind, and a grey veil for fog, drawn over the part of the map on
/// screen. Purely for looks: what rain and fog do to the senses happens in the simulation.
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

    private Simulation? _sim;
    private float _time;
    private bool _drewSomething;

    public void Bind(Simulation sim)
    {
        _sim = sim;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_sim is null)
            return;
        var settings = _sim.Weather.Settings;
        bool active = settings.RainPercent > 0 || settings.FogPercent > 0;
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

        if (settings.FogPercent > 0)
        {
            DrawRect(view, new Color(0.62f, 0.64f, 0.68f, settings.FogPercent * 0.004f));
            _drewSomething = true;
        }

        if (settings.RainPercent <= 0)
            return;

        // Rain falls down the screen and drifts the way the wind blows.
        var (wx, _) = settings.WindFrom.Opposite().Delta();
        float drift = wx * settings.WindSpeed * 40f;
        var velocity = new Vector2(drift, FallSpeed);
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

    /// <summary>A fixed pseudo-random value in [0, 1) for a drop and a channel.</summary>
    private static float Hash(int index, int channel)
    {
        float v = Mathf.Sin(index * 12.9898f + channel * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }
}
