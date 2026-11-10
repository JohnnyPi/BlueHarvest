#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Game.Rendering;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Navigation;
using BlueHarvest.Sim.World;
using Godot;
using Direction = BlueHarvest.Sim.World.Direction;

namespace BlueHarvest.Game;

/// <summary>
/// The mouse: the tile under it (outlined, with a tooltip and the walk there previewed), left-click to
/// walk, right-click for the actions that make sense on that tile, and the wheel to zoom.
/// </summary>
public partial class Main
{
    /// <summary>Real seconds between steps of a click-to-walk, so you can watch it and stop it.</summary>
    private const double WalkStepSeconds = 0.11;

    /// <summary>Paths further than this (in tiles) aren't previewed on hover; a click still plans them.</summary>
    private const int PreviewRange = 60;

    private const int PathSearchNodes = 20_000;

    private TilePos? _hoverTile;
    private bool _previewStale = true;
    private IReadOnlyList<Direction>? _previewMoves;
    private string? _tooltipText;
    private Walk? _walk;

    /// <summary>A click-to-walk in progress: the moves left, and what it stops for.</summary>
    private sealed class Walk
    {
        public Walk(List<Direction> moves, bool sprint, HashSet<int> seenAtStart, int woundsAtStart, bool spottedAtStart)
        {
            Moves = moves;
            Sprint = sprint;
            SeenAtStart = seenAtStart;
            WoundsAtStart = woundsAtStart;
            SpottedAtStart = spottedAtStart;
        }

        public List<Direction> Moves { get; }
        public bool Sprint { get; }
        public HashSet<int> SeenAtStart { get; }
        public int WoundsAtStart { get; }
        public bool SpottedAtStart { get; }
        public int Next { get; set; }
        public double Wait { get; set; }
    }

    private void HandleClick(InputEventMouseButton mouse)
    {
        var viewport = GetViewport();
        if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            viewport.SetInputAsHandled();
            Zoom(mouse.ButtonIndex == MouseButton.WheelUp ? 1.1f : 1f / 1.1f);
            return;
        }
        if (mouse.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
            return;

        viewport.SetInputAsHandled();
        if (_walk is not null)
        {
            StopWalk("You stop.");
            return;
        }
        if (_playerActor.IsGone || _hoverTile is not { } tile)
            return;

        if (mouse.ButtonIndex == MouseButton.Right)
        {
            if (_mode != InputMode.Normal)
            {
                CancelMode();
                return;
            }
            OpenMenu(tile, viewport.GetMousePosition());
            return;
        }

        switch (_mode)
        {
            case InputMode.Aim:
                EndMode();
                Submit(new ThrowAction(ItemKind.Flare, tile));
                return;
            case InputMode.Normal:
                if (tile == _playerActor.Position)
                    Submit(new WaitAction(_playerActor.Movement.StepTicks));
                else
                    StartWalk(tile, sprint: mouse.ShiftPressed);
                return;
            default:
                return; // rope and orders wait for a key
        }
    }

    /// <summary>Finds the tile under the mouse each frame; when it changes, redoes the preview and tooltip.</summary>
    private void UpdateHover()
    {
        var mouse = GetViewport().GetMousePosition();
        TilePos? tile = null;
        if (!_playerActor.IsGone && MapScreenRect().HasPoint(mouse) && !OverPanel(mouse))
        {
            var local = _map.GetLocalMousePosition();
            var t = new TilePos(Mathf.FloorToInt(local.X / AsciiMapView.CellSize), Mathf.FloorToInt(local.Y / AsciiMapView.CellSize));
            if (_sim.Map.InBounds(t))
                tile = t;
        }

        if (tile != _hoverTile || _previewStale)
        {
            _hoverTile = tile;
            _previewStale = false;
            var o = _map.Overlays;
            o.Hover = tile;
            if (_mode == InputMode.Aim && tile is { } aim)
            {
                _cursor = aim;
                o.Cursor = aim;
            }
            UpdatePathPreview(tile);
            _tooltipText = tile is { } shown ? TooltipText(shown) : null;
            _map.QueueRedraw();
        }

        if (_tooltipText is not null && tile is not null && !_menu.Visible)
            _tooltip.ShowAt(mouse, _tooltipText, MapScreenRect());
        else
            _tooltip.Visible = false;
    }

    /// <summary>The part of the screen showing the map: everything left of the sidebar.</summary>
    private Rect2 MapScreenRect()
    {
        var size = GetViewportRect().Size;
        return new Rect2(0f, 0f, Mathf.Max(0f, size.X - Sidebar.PanelWidth), size.Y);
    }

    /// <summary>Whether a panel floating over the map (log, debug, help, prompt, menu) is under this point.</summary>
    private bool OverPanel(Vector2 point)
    {
        bool Hit(Control? c) => c is not null && c.Visible && c.GetGlobalRect().HasPoint(point);
        return _menu.Visible || Hit(_log) || Hit(_debug) || Hit(_help) || Hit(_promptBox);
    }

    private void UpdatePathPreview(TilePos? tile)
    {
        var o = _map.Overlays;
        o.PathPreview = Array.Empty<TilePos>();
        o.PathLabel = null;
        _previewMoves = null;
        if (tile is not { } goal || _mode != InputMode.Normal || _walk is not null || _playerActor.IsGone)
            return;
        if (goal == _playerActor.Position || Chebyshev(goal, _playerActor.Position) > PreviewRange)
            return;

        var moves = Pathfinder.FindPath(_sim, _playerActor, goal, 0, PathSearchNodes);
        if (moves is null || moves.Count == 0)
            return;
        _previewMoves = moves;
        o.PathPreview = Pathfinder.Trace(_playerActor, moves).Select(p => p.Anchor).ToList();
        o.PathLabel = $"{moves.Count} steps · {WalkSeconds(moves, sprint: false):0.0} s";
    }

    /// <summary>
    /// Roughly how long a walk takes, in game seconds: each step's cost on its ground, slope and wounds,
    /// sprinting at the sprint rate, plus opening closed doors. The simulation decides the real cost.
    /// </summary>
    private double WalkSeconds(IReadOnlyList<Direction> moves, bool sprint)
    {
        long ticks = 0;
        var at = _playerActor.Position;
        var movement = _playerActor.Movement;
        foreach (var move in moves)
        {
            var to = at.Offset(move);
            int cost = movement.CostTicks(_sim.Terrain[to] - _sim.Terrain[at], move.IsDiagonal())
                * _sim.Map.CoverAt(to).MoveCostPercent / 100
                * _playerActor.Health.StepCostPercent / 100;
            if (sprint)
                cost = cost * GearRules.SprintCostPercent / 100;
            if (_sim.IsClosedDoor(to))
                cost += Simulation.DoorTicks;
            ticks += Math.Max(1, cost);
            at = to;
        }
        return ticks / (double)Ticks.PerSecond;
    }

    private void StartWalk(TilePos goal, bool sprint, IReadOnlyList<Direction>? moves = null)
    {
        moves ??= goal == _hoverTile && _previewMoves is not null
            ? _previewMoves
            : Pathfinder.FindPath(_sim, _playerActor, goal, 0, PathSearchNodes);
        if (moves is null)
        {
            Say("There's no way there on foot.");
            return;
        }
        if (moves.Count == 0)
            return;

        _walk = new Walk(moves.ToList(), sprint, LargeActorsInView(), WoundScore(), _rexSeesYou);
        UpdatePathPreview(null);
        _map.QueueRedraw();
        SetPrompt(sprint ? "Sprinting there. Any key or click stops." : "Walking there. Any key or click stops.");
    }

    /// <summary>One step of a click-to-walk, then the checks that stop it.</summary>
    private void StepWalk(double delta)
    {
        var walk = _walk!;
        walk.Wait -= delta;
        if (walk.Wait > 0)
            return;
        walk.Wait = WalkStepSeconds;

        if (_playerActor.IsGone || _mode != InputMode.Normal || walk.Next >= walk.Moves.Count)
        {
            StopWalk(null);
            return;
        }

        var move = walk.Moves[walk.Next];
        var to = _playerActor.Position.Offset(move);
        bool doorWasShut = _sim.IsClosedDoor(to);
        Submit(new MoveAction(move, Sprint: walk.Sprint));
        if (_walk != walk)
            return; // the turn ended the walk (the run is over)

        if (_playerActor.Position == to)
            walk.Next++;
        else if (!(doorWasShut && !_sim.IsClosedDoor(to)))
        {
            StopWalk(null); // blocked: the log already says why
            return;
        }
        // (A door that just swung open is walked through on the next step.)

        var newlySeen = LargeActorsInView().Except(walk.SeenAtStart).ToList();
        if (newlySeen.Count > 0)
            StopWalk($"You stop: {(_sim.Actors[newlySeen[0]] == _rex ? "the Rex" : _sim.Actors[newlySeen[0]].Name)} is in view.");
        else if (_rexSeesYou && !walk.SpottedAtStart)
            StopWalk("You stop: the Rex has seen you.");
        else if (WoundScore() > walk.WoundsAtStart)
            StopWalk("You stop: you're hurt.");
        else if (walk.Next >= walk.Moves.Count)
            StopWalk(null);
    }

    private void StopWalk(string? why)
    {
        if (_walk is null)
            return;
        _walk = null;
        SetPrompt(null);
        if (why is not null)
            Say(why);
        _previewStale = true;
    }

    /// <summary>Ids of large creatures the player can make out right now.</summary>
    private HashSet<int> LargeActorsInView() =>
        _playerPercepts?.Seen.Where(c => c.Detected && c.Target.IsLarge && !c.Target.IsDead).Select(c => c.Target.Id).ToHashSet()
        ?? new HashSet<int>();

    /// <summary>Goes up with every new or worse wound and every new bleed.</summary>
    private int WoundScore()
    {
        var health = _playerActor.Health;
        return Enum.GetValues<BodySection>().Sum(s => (int)health[s].Severity + (health[s].Bleeding ? 1 : 0));
    }

    /// <summary>The right-click menu: only what makes sense on that tile, from where you stand.</summary>
    private void OpenMenu(TilePos tile, Vector2 at)
    {
        var me = _playerActor;
        var here = me.Position;
        var inventory = me.Inventory;
        bool self = tile == here;
        int reach = Chebyshev(here, tile);
        var choices = new List<MenuChoice>();

        if (self)
        {
            choices.Add(new MenuChoice("Wait a moment", () => Submit(new WaitAction(me.Movement.StepTicks)),
                $"{me.Movement.StepTicks / (double)Ticks.PerSecond:0.0} s"));
            if (me.Health.WorstUntreated() is { } wound)
                choices.Add(new MenuChoice($"First aid on your {InjuryRules.Name(wound)}", () => Submit(new TreatAction()),
                    $"{inventory.Count(ItemKind.FirstAidKit)} kit(s)", inventory.Has(ItemKind.FirstAidKit) && me.Health.CanTreat));
            if (_sim.Map.Structures.HasLadder(here))
                choices.Add(new MenuChoice(me.OnDeck ? "Climb down the ladder" : "Climb up the ladder", () => Submit(new ClimbAction())));
            if (inventory.Has(ItemKind.Flashlight))
            {
                bool on = me.CarriedLight is { } light && _sim.Lights[light].On;
                choices.Add(new MenuChoice(on ? "Switch the flashlight off" : "Switch the flashlight on", () => Submit(new ToggleLightAction())));
            }
        }
        else
        {
            var moves = Pathfinder.FindPath(_sim, me, tile, 0, PathSearchNodes);
            if (moves is { Count: > 0 })
            {
                choices.Add(new MenuChoice("Walk here", () => StartWalk(tile, false, moves),
                    $"{moves.Count} steps · {WalkSeconds(moves, sprint: false):0.0} s"));
                if (me.Health.CanSprint)
                    choices.Add(new MenuChoice("Sprint here", () => StartWalk(tile, true, moves),
                        $"{WalkSeconds(moves, sprint: true):0.0} s · loud"));
            }
            else if (moves is null)
            {
                choices.Add(new MenuChoice("No way there on foot", () => { }, null, false));
            }
        }

        if (reach == 1)
        {
            var direction = Directions.FromDelta(tile.X - here.X, tile.Y - here.Y);
            if (_sim.Cover[tile] == CoverType.Door)
            {
                switch (_sim.Map.Structures.DoorAt(tile))
                {
                    case DoorState.Open:
                        choices.Add(new MenuChoice("Close this door", () => Submit(new UseDoorAction(direction))));
                        break;
                    case DoorState.Closed:
                        choices.Add(new MenuChoice("Open this door", () => Submit(new UseDoorAction(direction))));
                        break;
                    default:
                        choices.Add(new MenuChoice("The door is locked", () => { }, null, false));
                        break;
                }
            }
            int drop = _sim.StandHeight(me) - _sim.Terrain[tile];
            if (drop > me.Movement.MaxDropDm && inventory.Has(ItemKind.Rope))
                choices.Add(new MenuChoice("Rope down here", () => Submit(new RappelAction(direction)),
                    drop <= GearRules.RopeLengthDm ? $"{Units.FormatMeters(drop)} drop" : "the rope won't reach",
                    drop <= GearRules.RopeLengthDm));
        }

        if (PersonShownAt(tile) is { } person)
        {
            if (reach <= Simulation.OrderRange)
            {
                choices.Add(new MenuChoice($"Tell {person.Name}: follow me", () => Submit(new OrderAction(person.Id, NpcOrder.Follow))));
                choices.Add(new MenuChoice($"Tell {person.Name}: stay here", () => Submit(new OrderAction(person.Id, NpcOrder.Stay))));
                choices.Add(new MenuChoice($"Tell {person.Name}: hide", () => Submit(new OrderAction(person.Id, NpcOrder.Hide))));
            }
            if (reach == 1 && person.Health.WorstUntreated() is not null)
                choices.Add(new MenuChoice($"Patch up {person.Name}", () => Submit(new TreatAction(person.Id)),
                    null, inventory.Has(ItemKind.FirstAidKit) && me.Health.CanTreat));
        }

        if (!self && inventory.Has(ItemKind.Flare))
        {
            int range = GearRules.ThrowRange(me.Skills.Throwing, me.Health);
            int dx = tile.X - here.X, dy = tile.Y - here.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            choices.Add(new MenuChoice("Throw a flare here", () => Submit(new ThrowAction(ItemKind.Flare, tile)),
                distance <= range ? $"{distance:0} m" : $"{distance:0} m, lands short", range > 0));
        }

        choices.Add(new MenuChoice("Look", () => Say(LookText(tile))));
        _tooltip.Visible = false;
        _menu.Open(at, GetViewportRect().Size, $"{TileTitle(tile)}  ({tile.X}, {tile.Y})", choices);
    }

    /// <summary>A living person (not you) on this tile whom the map is showing.</summary>
    private Actor? PersonShownAt(TilePos tile) =>
        _sim.Actors.FirstOrDefault(a => a != _playerActor && !a.IsGone && a.Controller is HumanBrain
            && a.Position == tile && Shown(a));

    /// <summary>Whether the map draws this actor (with "your view" on, only those you've spotted).</summary>
    private bool Shown(Actor actor)
    {
        var o = _map.Overlays;
        return actor == _playerActor || !o.PlayerView || o.PlayerSpottedActors.Contains(actor.Id);
    }

    private string TileTitle(TilePos tile)
    {
        if (_sim.ExitAt(tile) is { } exit)
            return $"Way out: the {exit.Name}";
        if (_sim.VehicleAt(tile) is { } vehicle)
            return $"{Capitalize(vehicle.Name)} ({(vehicle.Breached ? "breached" : $"{vehicle.Integrity}%")})";
        if (_sim.Map.Structures.HasDeck(tile))
            return "A platform";
        if (_sim.Cover[tile] == CoverType.Door)
            return _sim.Map.Structures.DoorAt(tile) switch
            {
                DoorState.Open => "An open door",
                DoorState.Closed => "A closed door",
                _ => "A locked door",
            };
        return Capitalize(_sim.Map.CoverAt(tile).Name);
    }

    /// <summary>What the tooltip and "Look" say about a tile, as (text, colour) lines.</summary>
    private List<(string Text, Color? Color)> TileFacts(TilePos tile)
    {
        var facts = new List<(string, Color?)>();
        var me = _playerActor;

        if (tile == me.Position)
            facts.Add(("You", HudTheme.Ink));
        else if (_rex is not null && !_rex.IsDead && Shown(_rex) && _rex.Body.Any(b => b.Position == tile))
            facts.Add(("The Rex", HudTheme.Danger));
        else if (_sim.Actors.FirstOrDefault(a => a != me && a.Position == tile && !a.HasEscaped && !a.IsLarge && Shown(a)) is { } other)
            facts.Add((other.IsDead ? $"{other.Name}, dead"
                : other.Controller is HumanBrain brain ? $"{other.Name}, {brain.Describe()}"
                : other.Name, other.IsDead ? HudTheme.Danger : HudTheme.Ink));

        int ground = _sim.Terrain[tile];
        int relative = ground - _sim.StandHeight(me);
        facts.Add(($"Ground {Units.FormatMeters(ground)}" +
            (Math.Abs(relative) >= 1 ? $" · {Units.FormatMeters(Math.Abs(relative))} {(relative > 0 ? "above" : "below")} you" : ""), null));
        facts.Add(($"Light {_sim.Light.LevelAt(tile)}%", null));

        var cover = _sim.Map.CoverAt(tile);
        if (_sim.VehicleAt(tile) is null && !cover.BlocksMovement && cover.StopsLargeBody(Species.Rex.Force))
            facts.Add(("The Rex can't push through", HudTheme.Safe));
        else if (cover.IsSmashedBy(Species.Rex.Force) && !cover.BlocksMovement)
            facts.Add(("The Rex smashes through", HudTheme.Muted));
        if (_sim.Map.IsIndoor(tile))
            facts.Add(("Under a roof: out of the Rex's reach", HudTheme.Safe));
        if (_sim.ExitAt(tile) is not null)
            facts.Add(("Reach it to get away", HudTheme.Safe));
        if (_sim.Map.Structures.HasLadder(tile))
            facts.Add(("A ladder: < climbs up, > climbs down", null));
        if (Chebyshev(me.Position, tile) == 1
            && _sim.MoveBlockReason(me, Directions.FromDelta(tile.X - me.Position.X, tile.Y - me.Position.Y)) is { } why)
            facts.Add(($"Can't step here: {why}", HudTheme.Danger));
        return facts;
    }

    private string TooltipText(TilePos tile)
    {
        var lines = new List<string> { $"[color={HudTheme.Hex(HudTheme.Amber)}]{HudTheme.Escape(TileTitle(tile))}[/color]" };
        foreach (var (text, color) in TileFacts(tile))
            lines.Add(color is { } c ? $"[color={HudTheme.Hex(c)}]{HudTheme.Escape(text)}[/color]" : HudTheme.Escape(text));
        lines.Add($"[color={HudTheme.Hex(HudTheme.Muted)}]({tile.X}, {tile.Y})[/color]");
        return string.Join("\n", lines);
    }

    private string LookText(TilePos tile) =>
        $"{TileTitle(tile)}: {string.Join("; ", TileFacts(tile).Select(f => f.Text))}.";
}
