#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Game.Rendering;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.World;
using Godot;
using Direction = BlueHarvest.Sim.World.Direction;

namespace BlueHarvest.Game;

/// <summary>
/// Turn-based front end: loads the testbed map, draws it, and turns key presses into player actions.
/// Falls back to the built-in demo map if the testbed file is missing or broken.
/// </summary>
public partial class Main : Node2D
{
    private const ulong Seed = 12345;
    private const string MapPath = "res://Maps/testbed.bhmap";
    private const string Controls =
        "Move: arrows / numpad / hjkl yubn   Wait: . or numpad 5   Close a door: c   Zoom: + -\n" +
        "Overlays: [1] lighting  [2] your view  [3] rex view  [4] noise  [5] rex path  [6] rex memory   [G] omniscient rex (debug)";

    private Simulation _sim = null!;
    private PlayerController _player = null!;
    private Actor _playerActor = null!;
    private Actor? _rex;
    private AsciiMapView _map = null!;
    private Camera2D _camera = null!;
    private Label _hud = null!;
    private string _message = "";
    private long _lastTurnTick;
    private GoToController? _rexHunt;
    private CreatureBrain? _rexBrain;
    private Director _director = new();

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.02f, 0.02f, 0.03f));
        var font = new SystemFont
        {
            FontNames = new[] { "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Courier New", "monospace" },
        };

        _player = new PlayerController();
        LoadSimulation();
        _playerActor = _sim.Actors.First(a => a.Controller == _player);
        _rex = _sim.Actors.FirstOrDefault(a => a.Species == Species.Rex);
        _rexBrain = _rex?.Controller as CreatureBrain;
        _sim.MoveBlocked += OnMoveBlocked;
        _sim.ActorStruck += OnActorStruck;
        _sim.ActorBitten += OnActorBitten;
        _sim.VehicleDamaged += OnVehicleDamaged;
        _sim.VehicleShoved += OnVehicleShoved;

        _map = new AsciiMapView();
        _map.Bind(_sim, font, MovementProfile.Human.MaxStepUpDm);
        _map.Overlays.PlayerId = _playerActor.Id;
        AddChild(_map);

        _camera = new Camera2D { PositionSmoothingEnabled = true, PositionSmoothingSpeed = 12f };
        AddChild(_camera);
        _camera.MakeCurrent();

        var hudLayer = new CanvasLayer();
        AddChild(hudLayer);
        _hud = new Label { Position = new Vector2(12, 8) };
        _hud.AddThemeFontOverride("font", font);
        _hud.AddThemeFontSizeOverride("font_size", 16);
        _hud.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.86f));
        _hud.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        hudLayer.AddChild(_hud);

        _sim.RunUntilInputNeeded();
        _director.Update(_sim, _playerActor);
        Refresh(snapCamera: true);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;

        if (TryZoom(key.Keycode) || (!key.Echo && (TryToggleOverlay(key.Keycode) || TryToggleHunt(key.Keycode))))
        {
            GetViewport().SetInputAsHandled();
            Refresh(snapCamera: false);
            return;
        }

        var action = key.Keycode == Key.C ? CloseDoorAction() : ActionFor(key.Keycode);
        if (action is null)
            return;

        GetViewport().SetInputAsHandled();
        _message = "";
        _lastTurnTick = _sim.CurrentTick;
        _player.Pending = action;
        _sim.RunUntilInputNeeded();
        _director.Update(_sim, _playerActor);
        Refresh(snapCamera: false);
    }

    /// <summary>Builds the simulation from the testbed file, or the demo map if that fails.</summary>
    private void LoadSimulation()
    {
        if (!Godot.FileAccess.FileExists(MapPath))
        {
            UseDemo($"{MapPath} not found; showing the demo map.");
            return;
        }

        try
        {
            var data = MapFile.Parse(Godot.FileAccess.GetFileAsString(MapPath));
            _sim = ScenarioBuilder.Build(data, Seed, _player);
            _message = "Night, rain, power out. The Rex is loose. Stay still in the light; move in the dark.";
        }
        catch (Exception e)
        {
            GD.PushError($"Could not load {MapPath}: {e.Message}");
            UseDemo($"Testbed map is broken ({e.Message}); showing the demo map.");
        }
    }

    private void UseDemo(string message)
    {
        _player = new PlayerController();
        _sim = DemoWorld.Create(Seed, _player);
        _message = message;
    }

    private ActorAction? ActionFor(Key key) => key switch
    {
        Key.Up or Key.Kp8 or Key.K => new MoveAction(Direction.North),
        Key.Down or Key.Kp2 or Key.J => new MoveAction(Direction.South),
        Key.Left or Key.Kp4 or Key.H => new MoveAction(Direction.West),
        Key.Right or Key.Kp6 or Key.L => new MoveAction(Direction.East),
        Key.Kp7 or Key.Y => new MoveAction(Direction.NorthWest),
        Key.Kp9 or Key.U => new MoveAction(Direction.NorthEast),
        Key.Kp1 or Key.B => new MoveAction(Direction.SouthWest),
        Key.Kp3 or Key.N => new MoveAction(Direction.SouthEast),
        Key.Kp5 or Key.Period or Key.Space => new WaitAction(_playerActor.Movement.StepTicks),
        _ => null,
    };

    /// <summary>Closes the first open door next to you (north first, clockwise), or says there isn't one.</summary>
    private ActorAction? CloseDoorAction()
    {
        foreach (var direction in Directions.All)
        {
            var p = _playerActor.Position.Offset(direction);
            if (_sim.Map.InBounds(p) && _sim.Cover[p] == CoverType.Door && _sim.Map.Structures.DoorAt(p) == DoorState.Open)
                return new UseDoorAction(direction);
        }
        _message = "There's no open door next to you.";
        Refresh(snapCamera: false);
        return null;
    }

    private bool TryZoom(Key key)
    {
        float factor = key switch
        {
            Key.Equal or Key.KpAdd => 1.25f,
            Key.Minus or Key.KpSubtract => 0.8f,
            _ => 0f,
        };
        if (factor == 0f)
            return false;

        float zoom = Mathf.Clamp(_camera.Zoom.X * factor, 0.4f, 3f);
        _camera.Zoom = new Vector2(zoom, zoom);
        return true;
    }

    private bool TryToggleOverlay(Key key)
    {
        var o = _map.Overlays;
        switch (key)
        {
            case Key.Key1: o.Lighting = !o.Lighting; return true;
            case Key.Key2: o.PlayerView = !o.PlayerView; return true;
            case Key.Key3: o.RexView = !o.RexView; return true;
            case Key.Key4: o.Noise = !o.Noise; return true;
            case Key.Key5: o.RexPath = !o.RexPath; return true;
            case Key.Key6: o.RexMemory = !o.RexMemory; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Debug: G makes the Rex walk straight at you by pathfinding, knowing where you are without
    /// perceiving you. G again hands it back to its own brain, which keeps what it remembered.
    /// </summary>
    private bool TryToggleHunt(Key key)
    {
        if (key != Key.G || _rex is null)
            return false;

        if (_rexHunt is null)
        {
            var player = _playerActor;
            _rexHunt = new GoToController(() => player.Position, tolerance: 3);
            _sim.SetController(_rex, _rexHunt);
            _message = "DEBUG: the Rex is coming for you.";
        }
        else
        {
            _rexHunt = null;
            _sim.SetController(_rex, _rexBrain is not null
                ? (IActorController)_rexBrain
                : new RandomWalker(_sim.Rng.Fork($"ai:rex-wander:{_sim.CurrentTick}")));
            _message = "DEBUG: the Rex is back on its own senses.";
        }
        return true;
    }

    private void OnActorStruck(Actor striker, Actor victim)
    {
        _message = victim == _playerActor
            ? $"The {striker.Name}'s tail slams into you!"
            : $"The {striker.Name}'s tail hits {victim.Name}.";
    }

    private void OnActorBitten(Actor biter, Actor victim)
    {
        _message = victim == _playerActor
            ? $"The {biter.Name}'s jaws close on you! (Injuries arrive in build step 8.)"
            : $"The {biter.Name} bites {victim.Name}!";
    }

    private void OnVehicleDamaged(Vehicle vehicle, Actor by, int amount)
    {
        bool inside = _sim.VehicleAt(_playerActor.Position) == vehicle;
        string who = inside ? "your vehicle" : vehicle.Name;
        _message = vehicle.Breached
            ? $"The {by.Name} tears {who} open!"
            : $"The {by.Name} crunches into {who} ({vehicle.Integrity}% left).";
    }

    private void OnVehicleShoved(Vehicle vehicle, Actor by)
    {
        bool inside = _sim.VehicleAt(_playerActor.Position) == vehicle;
        _message = inside ? $"The {by.Name} shoves your vehicle along with you in it!" : $"The {by.Name} shoves {vehicle.Name}.";
    }

    private void OnMoveBlocked(Actor actor, string reason)
    {
        if (actor == _playerActor)
            _message = $"Blocked: {reason}.";
    }

    private void Refresh(bool snapCamera)
    {
        var playerPercepts = PerceptionSystem.Perceive(_sim, _playerActor, _lastTurnTick);
        var rexPercepts = _rex is null ? null : PerceptionSystem.Perceive(_sim, _rex, _lastTurnTick);

        UpdateOverlays(playerPercepts);
        _map.QueueRedraw();

        _camera.Position = AsciiMapView.TileCenter(_playerActor.Position);
        if (snapCamera)
            _camera.ResetSmoothing();

        var here = _playerActor.Position;
        bool moving = _playerActor.MovedRecently(_sim.CurrentTick, PerceptionSystem.MotionWindowTicks);
        _hud.Text =
            $"Blue Harvest {SimInfo.Version}   time {Ticks.Format(_sim.CurrentTick)}   seed {Seed}\n" +
            $"You at {here}   ground {Units.FormatMeters(_sim.Terrain[here])}   light {_sim.Light.LevelAt(here)}%   " +
            $"{(moving ? "moving" : "still")}{Shelter(here)}\n" +
            RexLine(rexPercepts) + "\n" +
            MindLine() + "\n" +
            HearingLine(playerPercepts) + "\n" +
            $"{_message}\n" +
            Controls;
    }

    private void UpdateOverlays(Percepts playerPercepts)
    {
        var o = _map.Overlays;
        int width = _sim.Map.Width, height = _sim.Map.Height;

        o.Noises = _sim.RecentNoises.Where(n => n.Tick >= _lastTurnTick).ToList();
        var rexPath = _rexHunt?.RemainingPath ?? _rexBrain?.PlannedPath ?? Array.Empty<Direction>();
        o.RexPathTiles = _rex is not null
            ? Pathfinder.Trace(_rex, rexPath).Select(p => p.Anchor).Distinct().ToList()
            : (IReadOnlyList<TilePos>)Array.Empty<TilePos>();

        long now = _sim.CurrentTick;
        var brain = _rexHunt is null ? _rexBrain : null;
        if (brain is not null)
        {
            var profile = brain.Profile;
            var focusKey = brain.Focus?.Key;
            o.Contacts = brain.Memory.All
                .Select(c => new ContactMark(c.Estimate, c.Uncertainty(now, profile), c.Confidence(now, profile),
                    c.Class, c.Key == focusKey))
                .ToList();
        }
        else
        {
            o.Contacts = Array.Empty<ContactMark>();
        }
        o.PlayerSpottedActors = playerPercepts.Seen.Where(c => c.Detected).Select(c => c.Target.Id).ToHashSet();

        if (o.PlayerView)
        {
            var visible = new bool[width * height];
            int threshold = _playerActor.Species.Senses.DetectThreshold;
            int bodyHeight = Species.Human.BodyHeightDm;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    visible[y * width + x] = PerceptionSystem.SightScore(
                        _sim, _playerActor, new TilePos(x, y), bodyHeight, moving: true) >= threshold;
            o.PlayerVisibleTiles = visible;
        }

        if (o.RexView && _rex is not null)
        {
            var scores = new int[width * height];
            int range = _rex.Species.Senses.SightRangeTiles;
            var at = _rex.Position;
            for (int y = Math.Max(0, at.Y - range); y <= Math.Min(height - 1, at.Y + range); y++)
                for (int x = Math.Max(0, at.X - range); x <= Math.Min(width - 1, at.X + range); x++)
                    scores[y * width + x] = PerceptionSystem.SightScore(
                        _sim, _rex, new TilePos(x, y), Species.Human.BodyHeightDm, moving: true);
            o.RexScores = scores;
            o.RexThreshold = _rex.Species.Senses.DetectThreshold;
        }
    }

    private string RexLine(Percepts? rex)
    {
        if (rex is null || _rex is null)
            return "No Rex on this map.";

        int threshold = _rex.Species.Senses.DetectThreshold;
        var contact = rex.ContactWith(_playerActor);
        string sight = contact switch
        {
            { Detected: true } c => $"Rex SEES you (score {c.Score}, needs {threshold})",
            { } c => $"Rex can't make you out (score {c.Score}, needs {threshold})",
            null => "Rex has no view of you",
        };
        string hearing = rex.Hears(_playerActor) ? ", HEARD you" : "";
        return $"{sight}{hearing}.   It sees {rex.Lights.Count} light(s).";
    }

    private string Shelter(TilePos here)
    {
        if (_sim.VehicleAt(here) is { } vehicle)
            return vehicle.Breached ? $"   in {vehicle.Name} (BREACHED)" : $"   in {vehicle.Name} ({vehicle.Integrity}%)";
        return _sim.Map.IsIndoor(here) ? "   indoors" : "";
    }

    /// <summary>What the Rex is doing and whether you've escaped it (out of reach and not its focus).</summary>
    private string MindLine()
    {
        if (_rex is null)
            return "";

        string mind = _rexHunt is not null
            ? "DEBUG: walking straight at you"
            : _rexBrain?.Describe(_sim) ?? "no brain";
        string calm = _director.InCalmWindow(_sim.CurrentTick) ? " (calm window)" : "";
        string escape = "";
        if (_rexHunt is null && _rexBrain is not null)
        {
            var status = EscapeCheck.Evaluate(_sim, _rex, _rexBrain, _playerActor);
            escape = status.Escaped
                ? "   ESCAPED"
                : $"   out of reach: {(status.OutOfReach ? "yes" : "no")}, attention elsewhere: {(status.AttentionElsewhere ? "yes" : "no")}";
        }
        return $"Rex is {mind}.\nTension {_director.Tension}{calm}{escape}";
    }

    private string HearingLine(Percepts percepts)
    {
        var sounds = percepts.Heard
            .Where(h => h.Noise.ActorId is not null)
            .GroupBy(h => h.Noise.ActorId!.Value)
            .Select(g => g.OrderByDescending(h => h.Perceived).First())
            .OrderByDescending(h => h.Perceived)
            .Select(Describe)
            .ToList();
        return sounds.Count == 0 ? "You hear only the rain." : "You hear: " + string.Join("; ", sounds);
    }

    private string Describe(HeardNoise heard)
    {
        var noise = heard.Noise;
        string who = noise.ActorId is { } id ? _sim.Actors[id].Name : "something";
        var here = _playerActor.Position;
        string where = Directions.FromDelta(noise.Position.X - here.X, noise.Position.Y - here.Y).Compass();
        string loudness = heard.Perceived >= 20 ? "loud " : heard.Perceived <= 6 ? "faint " : "";
        return $"{loudness}{noise.Kind} ({who}, {where})";
    }
}
