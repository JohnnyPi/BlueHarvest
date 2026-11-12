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
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Godot;
using Direction = BlueHarvest.Sim.World.Direction;
using Sky = BlueHarvest.Sim.World.Sky; // Godot has a Sky of its own

namespace BlueHarvest.Game;

/// <summary>The sidebar, the log, the prompt line, the debug readout and the end screens.</summary>
public partial class Main
{
    private Sidebar _sidebar = null!;
    private LogPanel? _log;
    private DebugPanel _debug = null!;
    private HelpPanel _help = null!;
    private TileTooltip _tooltip = null!;
    private ActionMenu _menu = null!;
    private PanelContainer _promptBox = null!;
    private Label _prompt = null!;
    private Control _deathScreen = null!;
    private Label _deathText = null!;
    private EdgeMarkers _edges = null!;

    /// <summary>Lines said before the log existed (loading the map).</summary>
    private readonly List<string> _pendingLog = new();

    /// <summary>What the player perceived on the last refresh; click-to-walk compares against it.</summary>
    private Percepts? _playerPercepts;

    /// <summary>Whether the Rex had the player spotted on the last refresh.</summary>
    private bool _rexSeesYou;

    private void BuildHud(CanvasLayer layer, Font font)
    {
        var theme = HudTheme.Build(font);
        var root = new Control
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Theme = theme,
        };
        layer.AddChild(root);

        // Off-screen arrows sit under every panel.
        _edges = new EdgeMarkers
        {
            ToScreen = p => _map.GetGlobalTransformWithCanvas() * _map.CenterOf(p),
        };
        root.AddChild(_edges);

        _sidebar = new Sidebar();
        root.AddChild(_sidebar);

        _log = new LogPanel();
        _log.SetCorner(UiSettings.LoadLogCorner());
        _log.MoveRequested += ToggleLogCorner;
        root.AddChild(_log);

        // One line at the top of the map while a key mode waits ("Aim the flare: ...").
        _promptBox = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -280f - Sidebar.PanelWidth / 2f,
            OffsetRight = 280f - Sidebar.PanelWidth / 2f,
            OffsetTop = 10f,
            OffsetBottom = 10f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _promptBox.AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.Amber, 4, 8));
        _prompt = HudTheme.Text("", HudTheme.TextSize, HudTheme.Amber);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _promptBox.AddChild(_prompt);
        root.AddChild(_promptBox);

        _debug = new DebugPanel { OffsetTop = 60f, OffsetBottom = 60f };
        _debug.Bind(_map.Overlays, WeatherPresets.Select(p => p.Name).ToList(), TimePresets.Select(p => p.Name).ToList());
        _debug.OverlaysChanged += () => Refresh(snapCamera: false);
        _debug.HuntToggled += on =>
        {
            SetHunt(on);
            Refresh(snapCamera: false);
        };
        _debug.WeatherChosen += index =>
        {
            SetWeatherPreset(index);
            Refresh(snapCamera: false);
        };
        _debug.TimeChosen += index =>
        {
            SkipToTime(index);
            Refresh(snapCamera: false);
        };
        root.AddChild(_debug);

        _help = new HelpPanel();
        root.AddChild(_help);

        _tooltip = new TileTooltip();
        root.AddChild(_tooltip);

        _menu = new ActionMenu { Theme = theme };
        root.AddChild(_menu);

        foreach (string line in _pendingLog)
            Say(line);
        _pendingLog.Clear();
    }

    /// <summary>Adds a line to the log, stamped with the game clock.</summary>
    private void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (_log is null)
        {
            _pendingLog.Add(text);
            return;
        }
        _log.Add(Clock(_sim.CurrentTick), text, _turn);
    }

    /// <summary>The line at the top of the map while a key mode waits; null hides it.</summary>
    private void SetPrompt(string? text)
    {
        _prompt.Text = text ?? "";
        _promptBox.Visible = !string.IsNullOrEmpty(text);
    }

    private void ToggleLogCorner()
    {
        if (_log is null)
            return;
        var corner = _log.Corner == LogCorner.LowerLeft ? LogCorner.UpperLeft : LogCorner.LowerLeft;
        _log.SetCorner(corner);
        UiSettings.SaveLogCorner(corner);
    }

    /// <summary>Keeps the off-screen arrows inside the map area and clear of the log; redrawn every frame as the camera moves.</summary>
    private void UpdateEdgeMarkers()
    {
        _edges.Visible = _map.Overlays.EdgeMarkers && !_playerActor.IsGone;
        if (!_edges.Visible)
            return;
        _edges.Bounds = MapScreenRect();
        _edges.Avoid.Clear();
        if (_log is { Visible: true } log)
            _edges.Avoid.Add(log.GetGlobalRect());
        _edges.QueueRedraw();
    }

    /// <summary>
    /// What the edge arrows point at: every exit, and the Rex if you can see it; otherwise its tread if
    /// you feel it, otherwise where you last saw it.
    /// </summary>
    private IReadOnlyList<EdgeTarget> EdgeTargets(Percepts mine)
    {
        var here = _playerActor.Position;
        var targets = new List<EdgeTarget>();
        foreach (var exit in _sim.Exits)
        {
            var (distance, _) = DistanceAndCompass(here, exit.Position);
            targets.Add(new EdgeTarget(exit.Position, $"{exit.Name} {distance} m", HudTheme.Safe));
        }
        if (_rex is null || _rex.IsDead)
            return targets;

        if (mine.Detects(_rex))
        {
            var (distance, _) = DistanceAndCompass(here, _rex.Position);
            targets.Add(new EdgeTarget(_rex.Position, $"Rex {distance} m", HudTheme.Danger));
            return targets;
        }
        var felt = mine.Felt
            .Where(f => f.Vibration.ActorId == _rex.Id)
            .OrderByDescending(f => f.Perceived)
            .FirstOrDefault();
        if (felt is not null)
        {
            string words = felt.Perceived >= 25 ? "the ground shudders" : felt.Perceived >= 15 ? "heavy footfalls" : "a faint tremor";
            targets.Add(new EdgeTarget(felt.Vibration.Position, words, HudTheme.Earth));
        }
        else if (_map.Overlays.Fog && _map.Fog is { } fog && fog.Ghosts.TryGetValue(_rex.Id, out var ghost))
        {
            targets.Add(new EdgeTarget(ghost.Tip, $"Rex, last seen {Clock(ghost.Tick)}", HudTheme.Muted));
        }
        return targets;
    }

    /// <summary>m:ss on the game clock.</summary>
    private static string Clock(long ticks)
    {
        long seconds = ticks / Ticks.PerSecond;
        return $"{seconds / 60}:{seconds % 60:D2}";
    }

    private void Refresh(bool snapCamera)
    {
        _playerPercepts = PerceptionSystem.Perceive(_sim, _playerActor, _lastTurnTick);
        var rexPercepts = _rex is null ? null : PerceptionSystem.Perceive(_sim, _rex, _lastTurnTick);
        _rexSeesYou = rexPercepts?.Detects(_playerActor) ?? false;
        _map.Fog?.Update(_sim, _playerActor, _playerPercepts);

        // the colour of the light goes with the hour, where there is a clock
        _map.Overlays.SkyTint = _sim.Clock is { } skyClock
            ? SkyLook.Tint(skyClock.MinuteOfDay(_sim.CurrentTick))
            : new Color(1f, 1f, 1f);
        UpdateOverlays(_playerPercepts);
        _map.QueueRedraw();
        UpdateCamera(snapCamera);

        _sidebar.Display(BuildFacts(_playerPercepts, rexPercepts));
        _edges.Targets = EdgeTargets(_playerPercepts);
        if (_debug.Visible)
        {
            _debug.Sync(_rexHunt is not null, WeatherPresetIndex());
            _debug.ShowReadout(DebugReadout(rexPercepts));
        }
        _previewStale = true;
    }

    private HudFacts BuildFacts(Percepts mine, Percepts? rex)
    {
        long now = _sim.CurrentTick;
        var here = _playerActor.Position;
        string muted = HudTheme.Hex(HudTheme.Muted);

        // Clock and weather.
        var weather = _sim.Weather;
        string badge = weather.Flash(now) ? "LIGHTNING"
            : weather.Thunder(now) ? "THUNDER"
            : weather.Settings.Storm ? "storm"
            : weather.Settings.RainPercent > 0 ? "rain"
            : weather.Settings.FogPercent > 0 ? "fog"
            : "";
        // with a clock: the part of the day, and the moon when it matters; without: night if it's dark
        string moon = "";
        if (_sim.Clock is { } clock)
        {
            int minute = clock.MinuteOfDay(now);
            string part = Sky.PartOf(minute) switch
            {
                PartOfDay.Night => "night",
                PartOfDay.Dawn => "dawn",
                PartOfDay.Dusk => "dusk",
                _ => "",
            };
            if (part.Length > 0)
                badge = badge.Length == 0 ? part : $"{part} · {badge}";
            if (Sky.PartOf(minute) != PartOfDay.Day)
                moon = $" {Capitalize(Sky.MoonName(_sim.MoonPhase))}, {(Sky.MoonUp(_sim.MoonPhase, minute) ? "up" : "not up")}.";
        }
        else if (_sim.AmbientLightPercent <= 20)
        {
            badge = badge.Length == 0 ? "night" : $"night · {badge}";
        }
        int mask = weather.HearingMask(now);
        string weatherText = Capitalize(weather.Settings.Describe())
            + (weather.Gust(now) >= 70 ? ", gusting" : "") + "."
            + (mask > 0 ? $" Sounds −{mask}." : "")
            + (weather.SightRangePercent < 100 ? $" Sight {weather.SightRangePercent}%." : "")
            + moon;

        // How close the Rex is to spotting you.
        int light = _sim.Light.LevelAt(here);
        bool moving = _playerActor.MovedRecently(now, PerceptionSystem.MotionWindowTicks);
        int threshold = _rex?.Species.Senses.DetectThreshold ?? 10;
        var contact = rex?.ContactWith(_playerActor);
        int score = contact?.Score ?? 0;
        var (verdict, alarm) = _rex is null ? ("no Rex here", Alarm.Calm)
            : _rex.IsDead ? ("dead", Alarm.Calm)
            : contact is { Detected: true } ? ("sees you", Alarm.Spotted)
            : contact is not null ? ("can't make you out", score >= threshold * 6 / 10 ? Alarm.Close : Alarm.Calm)
            : ("no view of you", Alarm.Calm);
        var detail = new List<string> { $"[color={muted}]light {light}% · {(moving ? "moving" : "still")}[/color]" };
        if (ShelterText(here) is { } shelter)
            detail.Add(shelter);
        if (rex is not null && rex.Hears(_playerActor))
            detail.Add($"[color={HudTheme.Hex(HudTheme.Amber)}]It heard you.[/color]");
        var trail = rex?.Smelled.FirstOrDefault(t => t.ActorId == _playerActor.Id);
        if (trail is not null)
            detail.Add(trail.Blood
                ? $"[color={HudTheme.Hex(HudTheme.Danger)}]It smells your blood.[/color]"
                : $"[color={HudTheme.Hex(HudTheme.Amber)}]It smells your trail.[/color]");

        // Senses.
        string see;
        if (_rex is not null && !_rex.IsDead && mine.Detects(_rex))
        {
            var (distance, compass) = DistanceAndCompass(here, _rex.Position);
            see = $"[color={HudTheme.Hex(HudTheme.Danger)}]the Rex[/color], {distance} m {compass}";
        }
        else
        {
            see = $"[color={muted}]no sign of the Rex[/color]";
        }

        return new HudFacts(
            Clock: _sim.Clock is { } worldClock ? worldClock.Describe(now) : Clock(now),
            WeatherBadge: badge,
            Weather: weatherText,
            SightScore: score,
            SightThreshold: threshold,
            SightVerdict: verdict,
            SightAlarm: alarm,
            SightDetail: string.Join("\n", detail),
            See: see,
            Hear: HearingText(mine),
            Feel: FeelText(mine),
            Health: _playerActor.Health,
            BodyNotes: BodyNotes(now),
            Exits: ExitFacts(here),
            Gear: GearText(),
            People: PeopleText());
    }

    private string? ShelterText(TilePos here)
    {
        if (_playerActor.OnDeck)
        {
            int up = _sim.StandHeight(_playerActor) - _sim.Terrain[here];
            return $"On a platform {Units.FormatMeters(up)} up, above the Rex's reach until it brings it down.";
        }
        if (_sim.VehicleAt(here) is { } vehicle)
            return vehicle.Breached
                ? $"[color={HudTheme.Hex(HudTheme.Danger)}]In {HudTheme.Escape(vehicle.Name)}: BREACHED, no protection.[/color]"
                : $"In {HudTheme.Escape(vehicle.Name)} ({vehicle.Integrity}%): the cab hides you and stops bites.";
        return _sim.Map.IsIndoor(here) ? "Indoors, under a roof the Rex can't reach under." : null;
    }

    private (int Distance, string Compass) DistanceAndCompass(TilePos from, TilePos to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        return ((int)Math.Round(Math.Sqrt(dx * dx + dy * dy)), Directions.FromDelta(dx, dy).Compass());
    }

    private string HearingText(Percepts percepts)
    {
        var sounds = percepts.Heard
            .Where(h => h.Noise.ActorId is not null && h.Noise.ActorId != _playerActor.Id)
            .GroupBy(h => h.Noise.ActorId!.Value)
            .Select(g => g.OrderByDescending(h => h.Perceived).First())
            .OrderByDescending(h => h.Perceived)
            .Select(Describe)
            .ToList();
        if (sounds.Count > 0)
            return string.Join("; ", sounds);
        string quiet = _sim.Weather.Settings.RainPercent > 0 ? "only the rain" : "nothing close";
        return $"[color={HudTheme.Hex(HudTheme.Muted)}]{quiet}[/color]";
    }

    private string Describe(HeardNoise heard)
    {
        var noise = heard.Noise;
        string who = noise.ActorId is { } id ? _sim.Actors[id].Name : "something";
        var here = _playerActor.Position;
        string where = Directions.FromDelta(noise.Position.X - here.X, noise.Position.Y - here.Y).Compass();
        string loudness = heard.Perceived >= 20 ? "loud " : heard.Perceived <= 6 ? "faint " : "";
        return HudTheme.Escape($"{loudness}{noise.Kind} ({who}, {where})");
    }

    /// <summary>What shakes the ground under you: the Rex's tread long before you see it.</summary>
    private string FeelText(Percepts percepts)
    {
        var shakes = percepts.Felt
            .GroupBy(f => f.Vibration.ActorId ?? -1)
            .Select(g => g.OrderByDescending(f => f.Perceived).First())
            .OrderByDescending(f => f.Perceived)
            .Select(f =>
            {
                var v = f.Vibration;
                string who = v.ActorId is { } id ? _sim.Actors[id].Name : "something";
                var here = _playerActor.Position;
                string where = Directions.FromDelta(v.Position.X - here.X, v.Position.Y - here.Y).Compass();
                string how = f.Perceived >= 25 ? "the ground shudders" : f.Perceived >= 15 ? "heavy footfalls" : "a faint tremor";
                return HudTheme.Escape($"{how} ({who}, {where})");
            })
            .ToList();
        return shakes.Count == 0
            ? $"[color={HudTheme.Hex(HudTheme.Muted)}]the ground is still[/color]"
            : $"[color={HudTheme.Hex(HudTheme.Earth)}]{string.Join("; ", shakes)}[/color]";
    }

    private string BodyNotes(long now)
    {
        var health = _playerActor.Health;
        var lines = new List<string>();
        foreach (var section in Enum.GetValues<BodySection>())
        {
            var wound = health[section];
            if (wound.Severity == Severity.Green)
                continue;
            string how = wound.Severity == Severity.Red ? "badly hurt" : "hurt";
            string bleeding = wound.Bleeding ? ", bleeding" : "";
            lines.Add($"[color={HudTheme.Hex(HudTheme.SeverityColor(wound.Severity))}]{Capitalize(InjuryRules.Name(section))} {how}{bleeding}[/color]");
        }
        if (lines.Count == 0)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Safe)}]No wounds[/color]");
        if (health.BleedTicksLeft(now) is { } left)
            lines.Add($"[color={HudTheme.Hex(HudTheme.Danger)}]Bleeding out: {left / Ticks.PerSecond} s[/color]");
        string muted = HudTheme.Hex(HudTheme.Muted);
        lines.Add($"[color={muted}]{(health.CanSprint ? "Can sprint" : "Can't sprint")}[/color]");
        return string.Join("\n", lines);
    }

    private IReadOnlyList<ExitFact> ExitFacts(TilePos here) =>
        _sim.Exits
            .Select(e =>
            {
                var (distance, compass) = DistanceAndCompass(here, e.Position);
                return new ExitFact(e.Name, distance, distance == 0 ? "here" : compass, distance == 0 ? "•" : Arrow(compass));
            })
            .OrderBy(e => e.Distance)
            .ToList();

    private static string Arrow(string compass) => compass switch
    {
        "N" => "↑",
        "NE" => "↗",
        "E" => "→",
        "SE" => "↘",
        "S" => "↓",
        "SW" => "↙",
        "W" => "←",
        "NW" => "↖",
        _ => "•",
    };

    private string GearText()
    {
        var inventory = _playerActor.Inventory;
        string muted = HudTheme.Hex(HudTheme.Muted);
        string KeyCap(string k) => $"[color={muted}][lb]{k}[rb][/color]";
        bool lightOn = _playerActor.CarriedLight is { } light && _sim.Lights[light].On;
        var lines = new List<string>
        {
            $"{KeyCap("T")} first-aid kits: {inventory.Count(ItemKind.FirstAidKit)}",
            $"{KeyCap("F")} flares: {inventory.Count(ItemKind.Flare)}",
        };
        if (inventory.Has(ItemKind.Flashlight))
            lines.Add($"{KeyCap("X")} flashlight: {(lightOn ? $"[color={HudTheme.Hex(HudTheme.Amber)}]on[/color]" : "off")}");
        lines.Add(inventory.Has(ItemKind.Rope) ? $"{KeyCap("R")} rope: {Units.FormatMeters(GearRules.RopeLengthDm)}" : $"[color={muted}]rope: used[/color]");
        lines.Add($"{KeyCap("Shift")} sprint while moving");
        return string.Join("\n", lines);
    }

    private string PeopleText()
    {
        string muted = HudTheme.Hex(HudTheme.Muted);
        var people = _sim.Actors
            .Where(a => a != _playerActor && a.Controller is HumanBrain)
            .Select(a =>
            {
                string state = a.IsDead ? $"[color={HudTheme.Hex(HudTheme.Danger)}]dead[/color]"
                    : a.HasEscaped ? $"[color={HudTheme.Hex(HudTheme.Safe)}]got out by the {HudTheme.Escape(a.EscapedVia!.Name)}[/color]"
                    : $"[color={muted}]{HudTheme.Escape(((HumanBrain)a.Controller).Describe())}[/color]";
                return $"{HudTheme.Escape(a.Name)}  {state}";
            })
            .ToList();
        return people.Count == 0 ? $"[color={muted}]Nobody else here.[/color]" : string.Join("\n", people);
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>The numbers behind the HUD, for the debug panel.</summary>
    private string DebugReadout(Percepts? rex)
    {
        var here = _playerActor.Position;
        bool moving = _playerActor.MovedRecently(_sim.CurrentTick, PerceptionSystem.MotionWindowTicks);
        string text =
            $"Blue Harvest {SimInfo.Version}   time {Ticks.Format(_sim.CurrentTick)}   seed {Seed}   turn {_turn}   drawing {_map.DrawnTiles} tiles, {_ambient.DrawnTiles} moving\n" +
            $"You at {here}   ground {Units.FormatMeters(_sim.Terrain[here])}   light {_sim.Light.LevelAt(here)}%   " +
            $"{(moving ? "moving" : "still")}\n" +
            (_sim.Clock is { } readoutClock
                ? $"{readoutClock.Describe(_sim.CurrentTick, 1)}   sky light {_sim.AmbientLightPercent}%   " +
                  $"{Sky.MoonName(_sim.MoonPhase)} ({Sky.MoonLitPercent(_sim.MoonPhase)}% lit, " +
                  $"{(Sky.MoonUp(_sim.MoonPhase, readoutClock.MinuteOfDay(_sim.CurrentTick)) ? "up" : "down")})\n"
                : "") +
            (_island is { } island
                ? $"Away from here: {island.Changes.TileCount} changed tiles kept in {island.Changes.ChunkCount} chunks, " +
                  $"{_sim.Actors.Count(a => a.LeftBehind)} left behind\n"
                : "") +
            RexLine(rex) + "\n" +
            MindLine();
        return HudTheme.Escape(text);
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
        var trail = rex.Smelled.FirstOrDefault(t => t.ActorId == _playerActor.Id);
        string smell = trail is null ? "" : trail.Blood ? ", SMELLS your blood" : ", SMELLS your trail";
        return $"{sight}{hearing}{smell}.   It sees {rex.Lights.Count} light(s).";
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

    private void UpdateOverlays(Percepts playerPercepts)
    {
        var o = _map.Overlays;
        int width = _sim.Map.Width, height = _sim.Map.Height;
        o.Noises = _sim.RecentNoises.Where(n => n.Tick >= _lastTurnTick).ToList();
        o.Vibrations = _sim.RecentVibrations.Where(v => v.Tick >= _lastTurnTick).ToList();
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
        o.Cursor = _mode == InputMode.Aim ? _cursor : null;
        o.CursorRange = GearRules.ThrowRange(_playerActor.Skills.Throwing, _playerActor.Health);
        o.CursorFrom = _playerActor.Position;
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

    /// <summary>Shown when the run ends: how you died or got out, how long you lasted, where your body lies.</summary>
    private void BuildDeathScreen(CanvasLayer layer, Font font)
    {
        _deathScreen = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.75f),
            AnchorRight = 1f,
            AnchorBottom = 1f,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _deathText = new Label
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _deathText.AddThemeFontOverride("font", font);
        _deathText.AddThemeFontSizeOverride("font_size", 24);
        _deathText.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.8f));
        _deathScreen.AddChild(_deathText);
        layer.AddChild(_deathScreen);
    }

    /// <summary>When the run is over, by death or by escape, shows how it ended.</summary>
    private void ShowEndScreen()
    {
        if (!_playerActor.IsGone || _deathScreen.Visible)
            return;
        StopWalk(null);
        StopRest(null);
        _tooltip.Visible = false;
        if (_playerActor.HasEscaped)
        {
            ShowEscapeScreen();
            return;
        }
        var health = _playerActor.Health;
        _deathText.Text =
            "YOU DIED\n\n" +
            $"{health.DeathCause}\n" +
            $"after {Ticks.Format(health.DiedTick)} on the island\n" +
            $"your body lies at {_playerActor.Position}\n\n" +
            WoundSummary(health) +
            "Press R to try again";
        _deathScreen.Visible = true;
    }

    private void ShowEscapeScreen()
    {
        var health = _playerActor.Health;
        var others = _sim.Actors.Where(a => a != _playerActor && a.Controller is HumanBrain).ToList();
        string people = others.Count == 0 ? "" :
            string.Join("   ", others.Select(a => a.IsDead ? $"{a.Name}: dead" : a.HasEscaped ? $"{a.Name}: got out" : $"{a.Name}: still out there")) + "\n\n";
        _deathText.AddThemeColorOverride("font_color", new Color(0.75f, 1f, 0.8f));
        _deathText.Text =
            "YOU ESCAPED\n\n" +
            $"through the {_playerActor.EscapedVia!.Name}\n" +
            $"after {Ticks.Format(_playerActor.EscapedTick ?? _sim.CurrentTick)} on the island\n\n" +
            (WoundSummary(health) is { Length: > 0 } wounds ? wounds : "no wounds\n\n") +
            people +
            "Press R to play again";
        _deathScreen.Visible = true;
    }

    private static string WoundSummary(Health health)
    {
        var wounds = Enum.GetValues<BodySection>()
            .Where(s => health[s].Severity != Severity.Green)
            .Select(s => $"{InjuryRules.Name(s)}: {health[s].Severity.ToString().ToLowerInvariant()}")
            .ToList();
        return wounds.Count > 0 ? string.Join("   ", wounds) + "\n\n" : "";
    }
}
