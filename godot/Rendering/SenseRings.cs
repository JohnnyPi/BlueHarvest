#nullable enable
using System;
using System.Collections.Generic;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// The senses drawn on the map (view option "Sense cues"): where a sound, a smell or a tremor comes
/// from gets a faint marker that moves in real time (a sound ripples out, a smell drifts up in wisps,
/// a tremor shakes); what comes only from a direction gets a tick on a small ring around you. Sight
/// needs no marker: you see it. Purely for looks; Main gives it the latest sense report each turn.
/// </summary>
public partial class SenseRings : Node2D
{
    private const int Cell = AsciiMapView.CellSize;

    /// <summary>The compass ring's radius, in tiles.</summary>
    private const float RingTiles = 3f;

    private const double FramesPerSecond = 30;

    private static readonly Color SoundInk = new(0.914f, 0.690f, 0.298f);
    private static readonly Color SmellInk = new(0.70f, 0.80f, 0.55f);
    private static readonly Color FeelInk = new(0.753f, 0.541f, 0.333f);
    private static readonly Color DangerInk = new(1f, 0.416f, 0.333f);

    private AsciiMapView? _map;
    private readonly List<SenseCue> _placed = new();
    private readonly List<SenseCue> _directions = new();
    private TilePos _player;
    private double _time, _sinceDraw;
    private bool _drewSomething;

    /// <summary>Whether to draw at all (the view option).</summary>
    public bool Enabled { get; set; } = true;

    public void Bind(AsciiMapView map) => _map = map;

    /// <summary>The cues to draw from now on, and where the player is.</summary>
    public void Show(SenseReport report, TilePos player)
    {
        _player = player;
        _placed.Clear();
        _directions.Clear();
        foreach (var list in new[] { report.Sound, report.Smell, report.Feel })
        {
            foreach (var cue in list)
            {
                if (cue.At is { } at && at != player)
                    _placed.Add(cue);
                else if (cue.At is null && cue.Toward is not null)
                    _directions.Add(cue);
            }
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _sinceDraw += delta;
        if (_sinceDraw < 1.0 / FramesPerSecond)
            return;
        _sinceDraw = 0;
        bool something = Enabled && _placed.Count + _directions.Count > 0;
        if (something || _drewSomething)
            QueueRedraw(); // once more after the last cue goes, to clear it
        _drewSomething = something;
    }

    public override void _Draw()
    {
        if (!Enabled || _map is null)
            return;
        float t = (float)_time;
        foreach (var cue in _placed)
        {
            var c = _map.CenterOf(cue.At!.Value);
            float strength = Mathf.Clamp(cue.Strength / 100f, 0.25f, 1f);
            var ink = cue.Danger ? DangerInk : Ink(cue.Sense);
            // a little offset per source, so cues on neighbouring tiles don't pulse together
            float seed = (cue.At.Value.X * 7 + cue.At.Value.Y * 13) % 10 / 10f;
            switch (cue.Sense)
            {
                case Sense.Sound:
                    for (int k = 0; k < 2; k++)
                    {
                        float phase = (t / 1.4f + seed + k * 0.5f) % 1f;
                        DrawArc(c, Cell * (0.3f + 1.4f * phase), 0f, Mathf.Tau, 24, ink with { A = 0.55f * (1f - phase) * strength }, 1.5f);
                    }
                    break;
                case Sense.Smell:
                    for (int k = 0; k < 3; k++)
                    {
                        float phase = (t / 2.2f + seed + k / 3f) % 1f;
                        var at = c + new Vector2(Mathf.Sin((phase + k) * 5f) * Cell * 0.3f, -phase * Cell * 1.1f);
                        DrawCircle(at, 1.6f + phase * 1.6f, ink with { A = 0.5f * (1f - phase) * strength });
                    }
                    break;
                default: // feel: the ground shakes
                    for (int k = 0; k < 2; k++)
                    {
                        float shake = Mathf.Sin(t * 38f + k * 2f + seed * 6f) * 2.5f * strength;
                        float y = c.Y + Cell * (0.25f + 0.18f * k);
                        DrawLine(new Vector2(c.X - Cell * 0.35f + shake, y), new Vector2(c.X + Cell * 0.35f + shake, y), ink with { A = 0.6f * strength }, 1.5f);
                    }
                    break;
            }
        }

        if (_directions.Count == 0)
            return;
        var centre = _map.CenterOf(_player);
        float radius = RingTiles * Cell;
        DrawArc(centre, radius, 0f, Mathf.Tau, 48, new Color(1f, 1f, 1f, 0.08f), 1f);
        foreach (var cue in _directions)
        {
            var (dx, dy) = cue.Toward!.Value.Delta();
            var dir = new Vector2(dx, dy).Normalized();
            float pulse = 0.65f + 0.35f * Mathf.Sin(t * 3f);
            var ink = (cue.Danger ? DangerInk : Ink(cue.Sense)) with { A = 0.8f * pulse };
            DrawLine(centre + dir * (radius - 5f), centre + dir * (radius + 5f), ink, 3f);
        }
    }

    private static Color Ink(Sense sense) => sense switch
    {
        Sense.Sound => SoundInk,
        Sense.Smell => SmellInk,
        _ => FeelInk,
    };
}
