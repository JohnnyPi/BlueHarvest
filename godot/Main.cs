#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Game.Rendering;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Godot;
using Direction = BlueHarvest.Sim.World.Direction;

namespace BlueHarvest.Game;

/// <summary>
/// Turn-based front end: loads the testbed map, draws it, and turns keys and clicks into player actions.
/// Falls back to the built-in demo map if the testbed file is missing or broken.
/// This file holds setup, keys, turns and the camera; Main.Hud.cs the sidebar and log, Main.Mouse.cs
/// hovering, click-to-walk and the right-click menu, Main.Events.cs what the simulation's events say.
/// </summary>
public partial class Main : Node2D
{
    private const ulong Seed = 12345;
    private const string MapPath = "res://Maps/testbed.bhmap";

    /// <summary>WASD panning speed in screen pixels per second.</summary>
    private const float PanSpeed = 900f;

    /// <summary>What the debug panel's weather list holds, in order.</summary>
    private static readonly (string Name, WeatherSettings Settings)[] WeatherPresets =
    {
        ("clear", WeatherSettings.Clear),
        ("rain", new WeatherSettings(RainPercent: 70, WindFrom: Direction.East, WindSpeed: 5)),
        ("storm", WeatherSettings.TestbedStorm),
        ("fog", new WeatherSettings(FogPercent: 70)),
    };

    private Simulation _sim = null!;
    private PlayerController _player = null!;
    private Actor _playerActor = null!;
    private Actor? _rex;

    /// <summary>On the island: the streamed map the simulation runs on. Null on the testbed.</summary>
    private IslandWindow? _island;
    private bool _windowShifted;
    private CreatureBrain? _rexBrain;
    private readonly Director _director = new();
    private AsciiMapView _map = null!;
    private AmbientView _ambient = null!;
    private WeatherView _weather = null!;
    private Camera2D _camera = null!;
    private ColorRect _flash = null!;
    private float _flashAlpha;

    /// <summary>How far WASD has moved the view from the player, in world pixels. Each turn resets it.</summary>
    private Vector2 _pan;

    private long _lastTurnTick;
    private int _turn;
    private GoToController? _rexHunt;
    private InputMode _mode = InputMode.Normal;
    private TilePos _cursor;
    private Actor? _orderTarget;

    /// <summary>What the next keys mean: normal play, aiming a throw, choosing which way to rappel, or an order.</summary>
    private enum InputMode
    {
        Normal,
        Aim,
        Rappel,
        Order,
    }

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
        SubscribeToEvents();

        _map = new AsciiMapView();
        _map.Bind(_sim, font, MovementProfile.Human.MaxStepUpDm, plan: _island is null);
        _sim.WindowShifted += OnWindowShifted;
        var overlays = _map.Overlays;
        overlays.PlayerId = _playerActor.Id;
        // The Rex's path, memory, noise and vibration rings are debug views now (F9).
        overlays.Noise = false;
        overlays.RexPath = false;
        overlays.RexMemory = false;
        overlays.Vibration = false;
        AddChild(_map);

        // water that moves on its own, over the ground and under the rain
        _ambient = new AmbientView();
        _ambient.Bind(_sim, _map);
        AddChild(_ambient);

        _weather = new WeatherView();
        _weather.Bind(_sim);
        AddChild(_weather);

        _camera = new Camera2D { PositionSmoothingEnabled = true, PositionSmoothingSpeed = 12f };
        AddChild(_camera);
        _camera.MakeCurrent();

        var hudLayer = new CanvasLayer();
        AddChild(hudLayer);
        _flash = new ColorRect
        {
            Color = new Color(0.92f, 0.95f, 1f, 0f),
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hudLayer.AddChild(_flash);
        BuildHud(hudLayer, font);
        BuildDeathScreen(hudLayer, font);

        _sim.RunUntilInputNeeded();
        _director.Update(_sim, _playerActor);
        Refresh(snapCamera: true);
        ShowEndScreen();
    }

    public override void _Process(double delta)
    {
        if (_flashAlpha > 0f)
        {
            _flashAlpha = Mathf.Max(0f, _flashAlpha - (float)delta * 1.6f);
            _flash.Color = new Color(0.92f, 0.95f, 1f, _flashAlpha);
        }

        // WASD pans the view. Not while a key mode is waiting for letters (o then s means "stay").
        if (_mode == InputMode.Normal && !_menu.Visible)
        {
            var direction = Vector2.Zero;
            if (Input.IsPhysicalKeyPressed(Key.W))
                direction.Y -= 1f;
            if (Input.IsPhysicalKeyPressed(Key.S))
                direction.Y += 1f;
            if (Input.IsPhysicalKeyPressed(Key.A))
                direction.X -= 1f;
            if (Input.IsPhysicalKeyPressed(Key.D))
                direction.X += 1f;
            if (direction != Vector2.Zero)
            {
                _pan += direction.Normalized() * PanSpeed * (float)delta / _camera.Zoom.X;
                UpdateCamera();
            }
        }

        // draw only what is on screen (plus a margin); the view redraws itself when the camera leaves that
        Vector2 viewSize = GetViewportRect().Size / _camera.Zoom;
        var onScreen = new Rect2(_camera.GetScreenCenterPosition() - viewSize / 2f, viewSize);
        _map.SetView(onScreen);
        _ambient.SetView(onScreen);

        if (_walk is not null)
            StepWalk(delta);
        UpdateHover();
        UpdateEdgeMarkers();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } mouse)
        {
            HandleClick(mouse);
            return;
        }
        if (@event is not InputEventKey { Pressed: true } key)
            return;

        var viewport = GetViewport();

        // Any key (bar modifiers) stops a click-to-walk in progress, and does nothing else.
        if (_walk is not null && !key.Echo && !IsModifier(key.Keycode))
        {
            viewport.SetInputAsHandled();
            StopWalk("You stop.");
            return;
        }

        if (!key.Echo && key.Keycode == Key.F10)
        {
            viewport.SetInputAsHandled();
            GetTree().ChangeSceneToFile(Island.FrontMenu.MenuScene);
            return;
        }
        if (!key.Echo && key.Keycode == Key.F9)
        {
            viewport.SetInputAsHandled();
            _debug.Visible = !_debug.Visible;
            Refresh(snapCamera: false);
            return;
        }
        if (!key.Echo && key.Keycode == Key.F1)
        {
            viewport.SetInputAsHandled();
            _help.Visible = !_help.Visible;
            return;
        }
        if (key.Keycode == Key.Escape && _help.Visible && _mode == InputMode.Normal)
        {
            viewport.SetInputAsHandled();
            _help.Visible = false;
            return;
        }
        if (!key.Echo && _debug.TryHotkey(key.Keycode))
        {
            viewport.SetInputAsHandled();
            return;
        }
        if (TryZoomKey(key.Keycode))
        {
            viewport.SetInputAsHandled();
            return;
        }
        if (key.Keycode == Key.Home)
        {
            viewport.SetInputAsHandled();
            _pan = Vector2.Zero;
            UpdateCamera();
            return;
        }

        if (_playerActor.IsGone)
        {
            if (key.Keycode == Key.R && !key.Echo)
            {
                viewport.SetInputAsHandled();
                GetTree().ReloadCurrentScene();
            }
            return;
        }

        if (_mode != InputMode.Normal)
        {
            viewport.SetInputAsHandled();
            HandleModeKey(key);
            return;
        }

        // W A S D are polled in _Process for panning.
        if (key.Keycode is Key.W or Key.A or Key.S or Key.D)
            return;

        if (!key.Echo && TryStartMode(key.Keycode))
        {
            viewport.SetInputAsHandled();
            Refresh(snapCamera: false);
            return;
        }

        var action = key.Keycode == Key.C ? CloseDoorAction()
            : key.Unicode is '<' or '>' ? LadderAction(up: key.Unicode == '<')
            : ActionFor(key.Keycode, sprint: key.ShiftPressed);
        if (action is null)
            return;

        viewport.SetInputAsHandled();
        Submit(action);
    }

    private static bool IsModifier(Key key) => key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta;

    /// <summary>Takes the player's turn with this action and runs everyone else until it's the player's turn again.</summary>
    private void Submit(ActorAction action)
    {
        _turn++;
        _lastTurnTick = _sim.CurrentTick;
        _player.Pending = action;
        _sim.RunUntilInputNeeded();
        // on the island the map follows the player: near an edge it scrolls and new ground streams in
        _windowShifted = false;
        if (_island is not null && !_playerActor.IsGone)
            _island.Follow(_sim, _playerActor);
        _director.Update(_sim, _playerActor);
        _pan = Vector2.Zero;
        Refresh(snapCamera: _windowShifted);
        ShowEndScreen();
    }

    /// <summary>
    /// Keys that change what the next keys mean, or switch something on: f aims a flare, r readies the
    /// rope, x switches the flashlight (that one takes a turn), o picks the nearest person in earshot
    /// to give an order to.
    /// </summary>
    private bool TryStartMode(Key key)
    {
        var inventory = _playerActor.Inventory;
        switch (key)
        {
            case Key.F:
                if (!inventory.Has(ItemKind.Flare))
                {
                    Say("You have no flares left.");
                    return true;
                }
                _mode = InputMode.Aim;
                var (fx, fy) = _playerActor.Facing.Delta();
                _cursor = _hoverTile ?? Clamp(new TilePos(_playerActor.Position.X + fx * 5, _playerActor.Position.Y + fy * 5));
                SetPrompt("Aim the flare: click a tile, or move the cursor and press Enter. Esc cancels.");
                return true;
            case Key.R:
                if (!inventory.Has(ItemKind.Rope))
                {
                    Say("You have no rope.");
                    return true;
                }
                _mode = InputMode.Rappel;
                SetPrompt("Rope down which way? Press a direction. Esc cancels.");
                return true;
            case Key.X:
                Submit(new ToggleLightAction());
                return true;
            case Key.O:
                _orderTarget = NearestListener();
                if (_orderTarget is null)
                {
                    Say($"Nobody within {Simulation.OrderRange} tiles to hear you.");
                    return true;
                }
                _mode = InputMode.Order;
                SetPrompt($"Tell {_orderTarget.Name} to: f follow you, s stay here, h hide. Esc cancels.");
                return true;
            default:
                return false;
        }
    }

    private void HandleModeKey(InputEventKey key)
    {
        if (key.Keycode == Key.Escape)
        {
            CancelMode();
            return;
        }

        if (_mode == InputMode.Order)
        {
            NpcOrder? order = key.Keycode switch
            {
                Key.F => NpcOrder.Follow,
                Key.S => NpcOrder.Stay,
                Key.H => NpcOrder.Hide,
                _ => null,
            };
            if (order is not { } chosen || _orderTarget is not { } target || key.Echo)
                return;
            EndMode();
            Submit(new OrderAction(target.Id, chosen));
            return;
        }

        var direction = DirectionFor(key.Keycode);
        if (_mode == InputMode.Rappel)
        {
            if (direction is not { } d)
                return;
            EndMode();
            Submit(new RappelAction(d));
            return;
        }

        // Aiming.
        if (direction is { } move)
        {
            var (dx, dy) = move.Delta();
            _cursor = Clamp(new TilePos(_cursor.X + dx, _cursor.Y + dy));
            Refresh(snapCamera: false);
        }
        else if (!key.Echo && key.Keycode is Key.Enter or Key.KpEnter or Key.F)
        {
            EndMode();
            Submit(new ThrowAction(ItemKind.Flare, _cursor));
        }
    }

    /// <summary>Back to normal play without doing anything.</summary>
    private void CancelMode()
    {
        EndMode();
        Refresh(snapCamera: false);
    }

    private void EndMode()
    {
        _mode = InputMode.Normal;
        SetPrompt(null);
    }

    /// <summary>The nearest living person (not you) close enough to hear an order.</summary>
    private Actor? NearestListener()
    {
        var at = _playerActor.Position;
        return _sim.Actors
            .Where(a => a != _playerActor && !a.IsGone && a.Controller is HumanBrain)
            .Select(a => (Actor: a, Distance: Chebyshev(a.Position, at)))
            .Where(x => x.Distance <= Simulation.OrderRange)
            .OrderBy(x => x.Distance)
            .Select(x => x.Actor)
            .FirstOrDefault();
    }

    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private TilePos Clamp(TilePos p) =>
        new(Math.Clamp(p.X, 0, _sim.Map.Width - 1), Math.Clamp(p.Y, 0, _sim.Map.Height - 1));

    private static Direction? DirectionFor(Key key) => key switch
    {
        Key.Up or Key.Kp8 or Key.K => Direction.North,
        Key.Down or Key.Kp2 or Key.J => Direction.South,
        Key.Left or Key.Kp4 or Key.H => Direction.West,
        Key.Right or Key.Kp6 or Key.L => Direction.East,
        Key.Kp7 or Key.Y => Direction.NorthWest,
        Key.Kp9 or Key.U => Direction.NorthEast,
        Key.Kp1 or Key.B => Direction.SouthWest,
        Key.Kp3 or Key.N => Direction.SouthEast,
        _ => null,
    };

    /// <summary>Builds the simulation from the testbed file, or the demo map if that fails.</summary>
    /// <summary>The streamed map scrolled under the simulation: move what this scene remembers by tile.</summary>
    private void OnWindowShifted(int dx, int dy)
    {
        _windowShifted = true;
        _map.MapScrolled(dx, dy);
        _ambient.MapChanged();
        _cursor = Clamp(new TilePos(_cursor.X - dx, _cursor.Y - dy));
        _hoverTile = null;
        _previewStale = true;
    }

    /// <summary>Starts on the generated island where the explorer said, in daylight, alone.</summary>
    private void LoadIsland(IslandWorld world, IslandChunker chunker)
    {
        _island = new IslandWindow(world, chunker, Island.IslandLaunch.X, Island.IslandLaunch.Y, chunksAcross: 3);
        _sim = new Simulation(_island.Map, Seed) { AmbientLightPercent = 100 };
        TilePos start = _island.NearestStandable(_island.ToLocal(Island.IslandLaunch.X, Island.IslandLaunch.Y));
        _sim.AddActor("Survivor", '@', start, Species.Human, _player, loadout: Loadout.TestbedSurvivor);
        Say($"Island {world.Params.Seed}. You are on foot; the map follows you. F10 for the menu.");
    }

    private void LoadSimulation()
    {
        if (Island.IslandLaunch.Walk && Island.IslandLaunch.World is { } world && Island.IslandLaunch.Chunker is { } chunker)
        {
            try
            {
                LoadIsland(world, chunker);
                return;
            }
            catch (Exception e)
            {
                GD.PushError($"Could not start on the island: {e}");
                _island = null;
                _player = new PlayerController();
                Island.IslandLaunch.PlayTestbed();
                Say($"Could not start on the island ({e.Message}); loading the testbed.");
            }
        }

        if (!Godot.FileAccess.FileExists(MapPath))
        {
            UseDemo($"{MapPath} not found; showing the demo map.");
            return;
        }

        try
        {
            var data = MapFile.Parse(Godot.FileAccess.GetFileAsString(MapPath));
            _sim = ScenarioBuilder.Build(data, Seed, _player);
            Say("Night, rain, power out. The Rex is loose. Stay still in the light; move in the dark.");
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
        Say(message);
    }

    /// <summary>A move (Shift sprints) or a wait for this key, or null.</summary>
    private ActorAction? ActionFor(Key key, bool sprint)
    {
        if (DirectionFor(key) is { } direction)
            return new MoveAction(direction, Sprint: sprint);
        return key switch
        {
            Key.Kp5 or Key.Period or Key.Space => new WaitAction(_playerActor.Movement.StepTicks),
            Key.T => new TreatAction(),
            _ => null,
        };
    }

    /// <summary>'&lt;' climbs up the ladder you're standing at, '&gt;' climbs down.</summary>
    private ActorAction? LadderAction(bool up)
    {
        var here = _playerActor.Position;
        if (!_sim.Map.Structures.HasLadder(here))
            Say("There's no ladder here.");
        else if (up == _playerActor.OnDeck)
            Say(up ? "You're already at the top." : "You're already at the bottom.");
        else
            return new ClimbAction();
        return null;
    }

    /// <summary>Closes the first open door next to you (north first, clockwise), or says there isn't one.</summary>
    private ActorAction? CloseDoorAction()
    {
        foreach (var direction in Directions.All)
        {
            var p = _playerActor.Position.Offset(direction);
            if (_sim.Map.InBounds(p) && _sim.Cover[p] == CoverType.Door && _sim.Map.Structures.DoorAt(p) == DoorState.Open)
                return new UseDoorAction(direction);
        }
        Say("There's no open door next to you.");
        return null;
    }

    private bool TryZoomKey(Key key)
    {
        float factor = key switch
        {
            Key.Equal or Key.KpAdd => 1.25f,
            Key.Minus or Key.KpSubtract => 0.8f,
            _ => 0f,
        };
        if (factor == 0f)
            return false;
        Zoom(factor);
        return true;
    }

    private void Zoom(float factor)
    {
        float zoom = Mathf.Clamp(_camera.Zoom.X * factor, 0.4f, 3f);
        _camera.Zoom = new Vector2(zoom, zoom);
        UpdateCamera();
    }

    /// <summary>
    /// Centres the player (plus any WASD pan) in the part of the screen the sidebar doesn't cover.
    /// </summary>
    private void UpdateCamera(bool snap = false)
    {
        float zoom = _camera.Zoom.X;
        _camera.Position = _map.ActorCenter(_playerActor) + _pan
            + new Vector2(Sidebar.PanelWidth / 2f / zoom, 0f);
        if (snap)
            _camera.ResetSmoothing();
    }

    /// <summary>
    /// Debug: the Rex walks straight at you by pathfinding, knowing where you are without perceiving
    /// you. Switched off, it goes back to its own brain, which keeps what it remembered.
    /// </summary>
    private void SetHunt(bool on)
    {
        if (_rex is null || on == (_rexHunt is not null))
            return;

        if (on)
        {
            var player = _playerActor;
            _rexHunt = new GoToController(() => player.Position, tolerance: 3);
            _sim.SetController(_rex, _rexHunt);
            Say("DEBUG: the Rex is coming for you.");
        }
        else
        {
            _rexHunt = null;
            _sim.SetController(_rex, _rexBrain is not null
                ? (IActorController)_rexBrain
                : new RandomWalker(_sim.Rng.Fork($"ai:rex-wander:{_sim.CurrentTick}")));
            Say("DEBUG: the Rex is back on its own senses.");
        }
    }

    /// <summary>Debug: sets one of the weather presets. Not recorded in replays.</summary>
    private void SetWeatherPreset(int index)
    {
        if (index < 0 || index >= WeatherPresets.Length)
            return;
        var (name, settings) = WeatherPresets[index];
        _sim.SetWeather(settings);
        Say($"DEBUG: weather set to {name} ({_sim.Weather.Settings.Describe()}).");
    }

    private int WeatherPresetIndex() => Array.FindIndex(WeatherPresets, p => p.Settings == _sim.Weather.Settings);
}
