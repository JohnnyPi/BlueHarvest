#nullable enable
using System;
using System.Collections.Generic;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>One remembered contact, as the [6] overlay draws it.</summary>
/// <param name="Radius">Uncertainty in tiles: the ring's size.</param>
public readonly record struct ContactMark(TilePos At, int Radius, int Confidence, ContactClass Class, bool Focus);

/// <summary>What the debug overlays show. Main fills it after every turn.</summary>
public sealed class OverlayState
{
    /// <summary>[1] Darken tiles by the simulation's light grid.</summary>
    public bool Lighting = true;

    /// <summary>[2] Fog out tiles the player can't see and hide actors the player hasn't spotted.</summary>
    public bool PlayerView;

    /// <summary>[3] Tint tiles by how well the Rex would see a moving human there.</summary>
    public bool RexView;

    /// <summary>[4] Rings for noises made since the player's last turn.</summary>
    public bool Noise = true;

    /// <summary>[5] The Rex's planned route, when it's hunting.</summary>
    public bool RexPath = true;

    /// <summary>[6] What the Rex remembers: a ring per contact (red prey, yellow unknown, grey ignored), thick for its focus.</summary>
    public bool RexMemory = true;

    /// <summary>[7] Scent on the ground: green dots for body scent, pink for blood, fading as they wash out.</summary>
    public bool Scent;

    /// <summary>[8] Ground vibrations since the player's last turn: a brown ring as far as a person would feel it.</summary>
    public bool Vibration = true;

    public IReadOnlyList<VibrationEvent> Vibrations = Array.Empty<VibrationEvent>();

    public IReadOnlyList<TilePos> RexPathTiles = Array.Empty<TilePos>();
    public IReadOnlyList<ContactMark> Contacts = Array.Empty<ContactMark>();

    /// <summary>The throw cursor while aiming, the thrower's range, and where they stand.</summary>
    public TilePos? Cursor;
    public int CursorRange;
    public TilePos CursorFrom;

    public bool[]? PlayerVisibleTiles;
    public HashSet<int> PlayerSpottedActors = new();
    public int[]? RexScores;
    public int RexThreshold;
    public int PlayerId = -1;
    public IReadOnlyList<NoiseEvent> Noises = Array.Empty<NoiseEvent>();
}

/// <summary>
/// Draws the simulation as colored ASCII cells: cover as glyph and background, height as brightness,
/// the light grid as darkness, cliffs as dark edges, then overlays and actors. Purely visual: it reads
/// simulation state and never changes it.
/// </summary>
public partial class AsciiMapView : Node2D
{
    public const int CellSize = 24;
    private const int FontSize = 20;
    private const float ShadowWidth = 4f;

    private static readonly Color CliffLine = new(0.02f, 0.02f, 0.02f);
    private static readonly Color CliffShadow = new(0f, 0f, 0f, 0.35f);
    private static readonly Color ActorBacking = new(0f, 0f, 0f, 0.5f);
    private static readonly Color LightGlow = new(1f, 0.72f, 0.3f, 0.12f);
    private static readonly Color LightGlyph = new(1f, 0.8f, 0.35f);
    private static readonly Color RexSeen = new(1f, 0.15f, 0.1f, 0.45f);
    private static readonly Color RexGlimpse = new(1f, 0.15f, 0.1f, 0.14f);
    private static readonly Color PlayerNoise = new(1f, 1f, 1f, 0.7f);
    private static readonly Color OtherNoise = new(1f, 0.6f, 0.2f, 0.7f);
    private static readonly Color PathDot = new(1f, 0.3f, 0.25f, 0.8f);
    private static readonly Color RexCore = new(0.35f, 0.06f, 0.05f, 0.85f);
    private static readonly Color RexSoft = new(0.30f, 0.05f, 0.05f, 0.55f);
    private static readonly Color LockedInk = new(1f, 0.35f, 0.3f);
    private static readonly Color DamagedInk = new(1f, 0.3f, 0.25f);
    private static readonly Color DeckFill = new(0.45f, 0.62f, 0.85f, 0.35f);
    private static readonly Color DeckRim = new(0.05f, 0.08f, 0.15f, 0.9f);
    private static readonly Color LadderInk = new(0.95f, 0.80f, 0.45f);
    private static readonly Color DeckActorBacking = new(0.10f, 0.20f, 0.40f, 0.8f);
    private static readonly Color BloodColor = new(0.55f, 0.02f, 0.02f, 0.85f);
    private static readonly Color CorpseInk = new(0.75f, 0.25f, 0.2f, 0.9f);
    private static readonly Color RopeColor = new(0.85f, 0.75f, 0.45f, 0.9f);
    private static readonly Color FlareInk = new(1f, 0.35f, 0.25f);
    private static readonly Color CursorColor = new(1f, 0.9f, 0.3f, 0.85f);
    private static readonly Color VibrationRing = new(0.80f, 0.55f, 0.30f, 0.65f);

    /// <summary>Background, glyph and glyph color per cover type, indexed by CoverType.</summary>
    private static readonly (Color Background, char Glyph, Color Ink)[] CoverLook =
    {
        (new Color(0.16f, 0.30f, 0.14f), '.', new Color(1f, 1f, 1f, 0.18f)),          // Grass
        (new Color(0.24f, 0.32f, 0.15f), ',', new Color(0.75f, 0.85f, 0.45f, 0.55f)), // Scrub
        (new Color(0.10f, 0.25f, 0.10f), '"', new Color(0.45f, 0.80f, 0.35f, 0.75f)), // Sparse jungle
        (new Color(0.05f, 0.16f, 0.06f), '&', new Color(0.30f, 0.65f, 0.25f, 0.85f)), // Dense jungle
        (new Color(0.36f, 0.30f, 0.20f), '=', new Color(0.75f, 0.65f, 0.45f, 0.45f)), // Track
        (new Color(0.28f, 0.28f, 0.28f), '.', new Color(1f, 1f, 1f, 0.25f)),          // Floor
        (new Color(0.55f, 0.55f, 0.53f), '#', new Color(0.15f, 0.15f, 0.15f)),        // Wall
        (new Color(0.40f, 0.26f, 0.12f), '+', new Color(0.95f, 0.80f, 0.55f)),        // Door
        (new Color(0.16f, 0.22f, 0.12f), '#', new Color(0.95f, 0.85f, 0.20f)),        // Fence
        (new Color(0.38f, 0.36f, 0.33f), '^', new Color(0.80f, 0.78f, 0.72f)),        // Rock
        (new Color(0.20f, 0.28f, 0.40f), '#', new Color(0.65f, 0.80f, 1f)),           // Vehicle
        (new Color(0.18f, 0.22f, 0.12f), '%', new Color(0.70f, 0.60f, 0.20f)),        // Broken fence
        (new Color(0.30f, 0.22f, 0.14f), '|', new Color(0.85f, 0.65f, 0.40f)),        // Support post
        (new Color(0.26f, 0.24f, 0.22f), ':', new Color(0.65f, 0.60f, 0.55f, 0.7f)),  // Rubble
    };

    private Simulation? _sim;
    private Font _font = null!;
    private int _cliffThresholdDm;
    private int _minHeightDm;
    private int _heightRangeDm = 1;

    public OverlayState Overlays { get; } = new();

    /// <summary>Pixel center of a tile, in this node's coordinates.</summary>
    public static Vector2 TileCenter(TilePos p) => new((p.X + 0.5f) * CellSize, (p.Y + 0.5f) * CellSize);

    /// <summary>
    /// Connects the view to a simulation. A height step bigger than <paramref name="cliffThresholdDm"/>
    /// between neighbors is drawn as a cliff edge.
    /// </summary>
    public void Bind(Simulation sim, Font font, int cliffThresholdDm)
    {
        _sim = sim;
        _font = font;
        _cliffThresholdDm = cliffThresholdDm;

        var terrain = sim.Terrain;
        int min = int.MaxValue, max = int.MinValue;
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < terrain.Width; x++)
            {
                int h = terrain[new TilePos(x, y)];
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
        }
        _minHeightDm = min;
        _heightRangeDm = Math.Max(1, max - min);

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_sim is null)
            return;

        var terrain = _sim.Terrain;
        int width = terrain.Width;
        float ascent = _font.GetAscent(FontSize);
        float descent = _font.GetDescent(FontSize);
        float baseline = (CellSize - (ascent + descent)) / 2f + ascent;
        var o = Overlays;

        // Ground: cover color, brighter on higher ground, darker where the light grid is dark.
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var p = new TilePos(x, y);
                var look = CoverLook[(int)_sim.Cover[p]];
                float t = (float)(terrain[p] - _minHeightDm) / _heightRangeDm;
                float shade = 0.55f + 0.65f * t;
                if (o.Lighting)
                    shade *= 0.12f + 0.88f * _sim.Light.LevelAt(p) / 100f;
                if (o.PlayerView && o.PlayerVisibleTiles is { } visible && !visible[y * width + x])
                    shade *= 0.25f;

                var (glyph, ink) = StructureLook(p, look.Glyph, look.Ink);
                DrawRect(new Rect2(x * CellSize, y * CellSize, CellSize, CellSize), ScaleColor(look.Background, shade));
                DrawGlyph(glyph, x, y, ScaleColor(ink, Math.Min(1f, shade * 1.3f)), baseline);
            }
        }

        // Cliffs: a dark line on the shared edge, with a shadow on the low side.
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var p = new TilePos(x, y);
                if (x + 1 < width)
                    DrawEastEdge(x, y, terrain.StepDm(p, new TilePos(x + 1, y)));
                if (y + 1 < terrain.Height)
                    DrawSouthEdge(x, y, terrain.StepDm(p, new TilePos(x, y + 1)));
            }
        }

        // Rex view: where the Rex would notice a moving human (strong red) or only glimpse one (faint red).
        if (o.RexView && o.RexScores is { } scores)
        {
            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] <= 0)
                    continue;
                var color = scores[i] >= o.RexThreshold ? RexSeen : RexGlimpse;
                DrawRect(new Rect2(i % width * CellSize, i / width * CellSize, CellSize, CellSize), color);
            }
        }

        // Light sources. With lighting on, the light grid already shows their glow.
        foreach (var light in _sim.Lights)
        {
            if (!light.On)
                continue;
            if (!o.Lighting)
            {
                DrawCircle(TileCenter(light.Position), CellSize * 2.5f, LightGlow);
                DrawCircle(TileCenter(light.Position), CellSize * 1.2f, LightGlow);
            }
            if (light.Facing is null)
                DrawGlyph('*', light.Position.X, light.Position.Y, LightGlyph, baseline);
        }

        // Noise rings: radius is roughly how far the noise carries in the open.
        if (o.Noise)
        {
            foreach (var noise in o.Noises)
            {
                var color = noise.ActorId == o.PlayerId ? PlayerNoise : OtherNoise;
                DrawArc(TileCenter(noise.Position), Math.Max(1, noise.Loudness) * CellSize, 0f, Mathf.Tau, 64, color, 1.5f);
            }
        }

        // The Rex's planned route: a dot per anchor position.
        if (o.RexPath)
        {
            foreach (var p in o.RexPathTiles)
                DrawCircle(TileCenter(p), 3f, PathDot);
        }

        // The Rex's memory: where it thinks things are, how sure it is, and what it's after.
        if (o.RexMemory)
        {
            foreach (var mark in o.Contacts)
            {
                float alpha = mark.Class == ContactClass.Object ? 0.3f : 0.35f + 0.6f * mark.Confidence / 100f;
                var color = mark.Class switch
                {
                    ContactClass.Prey => new Color(1f, 0.2f, 0.15f, alpha),
                    ContactClass.Unknown => new Color(1f, 0.85f, 0.2f, alpha),
                    _ => new Color(0.65f, 0.65f, 0.65f, alpha),
                };
                float radius = (mark.Radius + 0.5f) * CellSize;
                DrawArc(TileCenter(mark.At), radius, 0f, Mathf.Tau, 48, color, mark.Focus ? 3f : 1.5f);
                char glyph = mark.Class switch
                {
                    ContactClass.Prey => '!',
                    ContactClass.Unknown => '?',
                    _ => 'x',
                };
                DrawGlyph(glyph, mark.At.X, mark.At.Y, color, baseline);
            }
        }

        // Blood: a dark spot where someone bleeding has passed.
        foreach (var spot in _sim.BloodSpots)
            DrawCircle(TileCenter(spot.Position) + new Vector2(3f, 4f), 2.5f, BloodColor);

        // Scent: a dot per mark, bigger and brighter the stronger it still is.
        if (o.Scent)
        {
            long wash = _sim.Weather.Wash(_sim.CurrentTick);
            foreach (var mark in _sim.ScentMarks)
            {
                int strength = mark.StrengthAt(wash);
                if (strength <= 0)
                    continue;
                float alpha = 0.2f + 0.6f * strength / 100f;
                var color = mark.Blood ? new Color(1f, 0.35f, 0.6f, alpha) : new Color(0.45f, 0.95f, 0.35f, alpha);
                DrawCircle(TileCenter(mark.Position) + new Vector2(-4f, -4f), 1.5f + 3f * strength / 100f, color);
            }
        }

        // Vibration: how far a person would feel each footfall or crash (their threshold is 10).
        if (o.Vibration)
        {
            foreach (var vibration in o.Vibrations)
            {
                float reach = (vibration.Strength - Species.Human.Senses.VibrationThreshold)
                    / (float)ScentRules.VibrationFalloffPerTile;
                if (reach <= 0f)
                    continue;
                DrawArc(TileCenter(vibration.Position), reach * CellSize, 0f, Mathf.Tau, 64, VibrationRing, 2f);
            }
        }

        // Decks: translucent planks over whatever is beneath, a dark rim on their edges, 'H' for ladders.
        var structures = _sim.Map.Structures;
        for (int y = 0; y < terrain.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var p = new TilePos(x, y);
                if (!structures.HasDeck(p))
                    continue;
                float lit = o.Lighting ? 0.25f + 0.75f * _sim.Light.LevelAt(p) / 100f : 1f;
                var cell = new Rect2(x * CellSize, y * CellSize, CellSize, CellSize);
                DrawRect(cell, ScaleColor(DeckFill, lit));
                foreach (var d in Directions.Cardinal)
                {
                    var n = p.Offset(d);
                    if (_sim.Map.InBounds(n) && structures.HasDeck(n))
                        continue;
                    var (dx, dy) = d.Delta();
                    var a = TileCenter(p) + new Vector2(dx, dy) * (CellSize / 2f) + new Vector2(-dy, dx) * (CellSize / 2f);
                    var b = TileCenter(p) + new Vector2(dx, dy) * (CellSize / 2f) - new Vector2(-dy, dx) * (CellSize / 2f);
                    DrawLine(a, b, DeckRim, 2f);
                }
                if (structures.HasLadder(p))
                    DrawGlyph('H', x, y, ScaleColor(LadderInk, lit), baseline);
            }
        }

        // Ropes left tied at the top of a drop.
        foreach (var rope in _sim.Ropes)
            DrawLine(TileCenter(rope.Top), TileCenter(rope.Bottom), RopeColor, 2f);

        // Lit flares glow on top of the light grid so they're easy to spot.
        foreach (var light in _sim.Lights)
        {
            if (light.On && light.HeightDm < 5 && light.Facing is null)
                DrawGlyph('*', light.Position.X, light.Position.Y, FlareInk, baseline);
        }

        // Aiming: the thrower's range and the cursor.
        if (o.Cursor is { } cursor)
        {
            var from = TileCenter(o.CursorFrom);
            DrawArc(from, (o.CursorRange + 0.5f) * CellSize, 0f, Mathf.Tau, 64, CursorColor, 1f);
            DrawLine(from, TileCenter(cursor), CursorColor, 1f);
            DrawRect(new Rect2(cursor.X * CellSize, cursor.Y * CellSize, CellSize, CellSize), CursorColor, false, 2f);
        }

        // Actors on top: those on the ground first, then anyone up on a deck.
        foreach (bool onDeck in new[] { false, true })
        {
            foreach (var actor in _sim.Actors)
            {
                if (actor.OnDeck != onDeck)
                    continue;
                if (o.PlayerView && actor.Id != o.PlayerId && !o.PlayerSpottedActors.Contains(actor.Id))
                    continue;
                if (actor.IsLarge)
                {
                    DrawLargeBody(actor, baseline);
                    continue;
                }
                var p = actor.Position;
                if (actor.IsDead)
                {
                    DrawGlyph('%', p.X, p.Y, CorpseInk, baseline);
                    continue;
                }
                DrawRect(new Rect2(p.X * CellSize + 2, p.Y * CellSize + 2, CellSize - 4, CellSize - 4),
                    onDeck ? DeckActorBacking : ActorBacking);
                DrawGlyph(actor.Glyph, p.X, p.Y, ColorFor(actor.Glyph), baseline);
                DrawFacing(actor.Position, actor.Facing);
            }
        }
    }

    /// <summary>Core as solid 'O' tiles, neck 'o', tail '~', and the actor's glyph on the head tip.</summary>
    private void DrawLargeBody(Actor actor, float baseline)
    {
        var sim = _sim!;
        var map = sim.Map;
        var tip = sim.EyeTile(actor);
        var ink = ColorFor(actor.Glyph);
        foreach (var tile in actor.Body)
        {
            var p = tile.Position;
            if (!map.InBounds(p))
                continue;
            var rect = new Rect2(p.X * CellSize + 1, p.Y * CellSize + 1, CellSize - 2, CellSize - 2);
            switch (tile.Part)
            {
                case BodyPart.Core:
                    DrawRect(rect, RexCore);
                    DrawGlyph('O', p.X, p.Y, ink, baseline);
                    break;
                case BodyPart.Head:
                    DrawRect(rect, RexSoft);
                    DrawGlyph(p == tip ? actor.Glyph : 'o', p.X, p.Y, ink, baseline);
                    break;
                default:
                    DrawRect(rect, RexSoft);
                    DrawGlyph('~', p.X, p.Y, ink, baseline);
                    break;
            }
        }
        DrawFacing(tip, actor.Facing);
    }

    /// <summary>
    /// Doors show their state ('/' open, '+' closed, red '+' locked). Vehicles redden as they lose
    /// integrity and show '%' once breached.
    /// </summary>
    private (char Glyph, Color Ink) StructureLook(TilePos p, char glyph, Color ink)
    {
        var sim = _sim!;
        switch (sim.Cover[p])
        {
            case CoverType.Door:
                return sim.Map.Structures.DoorAt(p) switch
                {
                    DoorState.Open => ('/', ink),
                    DoorState.Closed => ('+', ink),
                    _ => ('+', LockedInk),
                };
            case CoverType.Vehicle when sim.VehicleAt(p) is { } vehicle:
                var damaged = ink.Lerp(DamagedInk, 1f - vehicle.Integrity / (float)Vehicle.MaxIntegrity);
                return (vehicle.Breached ? '%' : glyph, damaged);
            default:
                return (glyph, ink);
        }
    }

    private static Color ScaleColor(Color c, float factor) => new(c.R * factor, c.G * factor, c.B * factor, c.A);

    private void DrawFacing(TilePos p, Direction facing)
    {
        var (dx, dy) = facing.Delta();
        var center = TileCenter(p);
        var tip = center + new Vector2(dx, dy).Normalized() * (CellSize * 0.5f);
        DrawLine(center + new Vector2(dx, dy).Normalized() * (CellSize * 0.3f), tip, new Color(1f, 1f, 1f, 0.8f), 2f);
    }

    private void DrawEastEdge(int x, int y, int stepDm)
    {
        if (Math.Abs(stepDm) <= _cliffThresholdDm)
            return;

        float edgeX = (x + 1) * CellSize;
        float shadowX = stepDm > 0 ? edgeX - ShadowWidth : edgeX; // low side is west when east is higher
        DrawRect(new Rect2(shadowX, y * CellSize, ShadowWidth, CellSize), CliffShadow);
        DrawLine(new Vector2(edgeX, y * CellSize), new Vector2(edgeX, (y + 1) * CellSize), CliffLine, 2f);
    }

    private void DrawSouthEdge(int x, int y, int stepDm)
    {
        if (Math.Abs(stepDm) <= _cliffThresholdDm)
            return;

        float edgeY = (y + 1) * CellSize;
        float shadowY = stepDm > 0 ? edgeY - ShadowWidth : edgeY; // low side is north when south is higher
        DrawRect(new Rect2(x * CellSize, shadowY, CellSize, ShadowWidth), CliffShadow);
        DrawLine(new Vector2(x * CellSize, edgeY), new Vector2((x + 1) * CellSize, edgeY), CliffLine, 2f);
    }

    private void DrawGlyph(char glyph, int x, int y, Color color, float baseline)
    {
        string text = glyph.ToString();
        float width = _font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
        var position = new Vector2(x * CellSize + (CellSize - width) / 2f, y * CellSize + baseline);
        DrawString(_font, position, text, HorizontalAlignment.Left, -1, FontSize, color);
    }

    private static Color ColorFor(char glyph) => glyph switch
    {
        '@' => new Color(1f, 0.95f, 0.6f),
        'h' => new Color(0.55f, 0.90f, 1f),
        'R' => new Color(1f, 0.6f, 0.2f),
        'T' => new Color(1f, 0.28f, 0.22f),
        _ => Colors.White,
    };
}
