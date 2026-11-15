#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Power;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game;

/// <summary>
/// The island's power in the front end: what you can do with the equipment where you stand (the
/// right-click menu), what a tile's equipment and facility are doing (the tooltip), news from
/// facilities near you (the log), and a summary line in the F9 readout. Everything done goes
/// through OperateAction, so the simulation decides what happens.
/// </summary>
public partial class Main
{
    /// <summary>Power news reaches the log from facilities this close, in metres beyond their compound.</summary>
    private const int PowerNewsRange = 150;

    /// <summary>
    /// The menu for a tile: on your own tile, everything you can work from where you stand; on a tile
    /// of some equipment, that equipment (or a note to go there first).
    /// </summary>
    private void AddPowerChoices(List<MenuChoice> choices, TilePos tile)
    {
        if (_sim.Power is not { } grid)
            return;
        var reachable = _sim.DevicesInReach(_playerActor).ToList();
        IEnumerable<PowerDevice> shown = tile == _playerActor.Position ? reachable : _sim.DevicesAt(tile).ToList();
        foreach (PowerDevice device in shown)
        {
            if (!reachable.Contains(device))
            {
                choices.Add(new MenuChoice($"{Capitalize(device.Name)}: go there to work it", () => { }, null, false));
                continue;
            }
            int id = device.Id;
            foreach (PowerChoice choice in grid.Choices(id))
                choices.Add(new MenuChoice(choice.Text, () => Submit(new OperateAction(id, choice.Op, choice.Arg)), choice.Hint, choice.Enabled));
        }
    }

    /// <summary>Tooltip lines: a live fence, a powered door, the equipment on this tile, and how the facility around it is doing.</summary>
    private void AddPowerFacts(List<(string Text, Color? Color)> facts, TilePos tile)
    {
        if (_sim.Power is not { } grid)
            return;
        if (_sim.IsLiveFence(tile))
            facts.Add(("A live fence: it holds, and it bites whatever touches it", HudTheme.Danger));
        if (_sim.PowerDoorAt(tile) is { } door)
        {
            string doorSite = grid.Sites[door.SiteId].Name;
            facts.Add(door.Kind == DoorKind.SecurityLock
                ? (grid.DoorLocked(door)
                    ? ($"Security door ({door.Room}): locked while the {doorSite} has power; release it at the switch panel", HudTheme.Muted)
                    : ($"Security door ({door.Room}): its lock is off", HudTheme.Ink))
                : (grid.DoorPowered(door)
                    ? ("Motorised door: it slides open", HudTheme.Ink)
                    : ("Motorised door, no power: it can be forced (20 s, loud)", HudTheme.Muted)));
        }
        foreach (PowerDevice device in _sim.DevicesAt(tile))
            facts.Add((grid.Describe(device.Id), HudTheme.Ink));
        if (_sim.SiteAt(tile) is { } site)
            facts.Add(($"{site.Name}: {grid.DescribeSite(site)}", site.Powered ? HudTheme.Safe : HudTheme.Muted));
    }

    /// <summary>The network changing by itself: told only for facilities near you.</summary>
    private void OnPowerChanged(PowerEvent e)
    {
        if (_sim.Power is not { } grid || e.SiteId < 0 || e.SiteId >= grid.Sites.Count)
            return;
        if (NearSite(grid.Sites[e.SiteId]))
            Say(e.Text);
    }

    private void OnPowerOperated(Actor actor, PowerDevice device, PowerResult result)
    {
        if (actor == _playerActor)
            Say(result.Message);
    }

    private void OnShocked(Actor actor, TilePos fence)
    {
        if (actor == _playerActor)
            Say("The fence bites back: a jolt throws you off it, your arm numb.");
        else if (actor == _rex && Shown(actor))
            Say("The Rex flinches back from the live fence with a crack of sparks.");
        else if (Shown(actor))
            Say($"{actor.Name} touches the live fence and is thrown back.");
        else if (Chebyshev(_playerActor.Position, fence) <= 40)
            Say("Somewhere close, a fence cracks and spits.");
    }

    private void OnDoorForced(Actor actor, TilePos door)
    {
        if (actor == _playerActor)
            Say("The door has no power. You haul it along its track by hand, metal screaming.");
        else if (Shown(actor))
            Say($"{actor.Name} forces a dead door open.");
    }

    private void OnRideEnded(Actor actor)
    {
        if (actor == _playerActor)
            Say("The doors open. You step out.");
    }

    /// <summary>
    /// After a turn: if the player has just ridden an elevator or a cable car, put them at the far end,
    /// moving the window there first if the far end is off the map.
    /// </summary>
    private void ArriveFromRide()
    {
        if (_island is null || _playerActor.PendingRide is null)
            return;
        TilePos there = _sim.CompleteRide(_playerActor);
        if (!_playerActor.LeftBehind)
            return;
        (int dx, int dy) = _island.Recenter(there);
        _sim.ShiftWindow(dx, dy); // the player is waiting there, parked, and comes back onto the map
    }

    private bool NearSite(PowerSite site)
    {
        var (x, y) = _sim.ToIsland(_playerActor.Position);
        return Math.Max(Math.Abs(x - site.X), Math.Abs(y - site.Y)) <= site.Radius + PowerNewsRange;
    }

    /// <summary>The F9 readout's power line: how much of the island is lit, what's broken, the plants, and where you are.</summary>
    private string PowerReadout()
    {
        if (_sim.Power is not { } grid)
            return "";
        int lit = grid.Sites.Count(s => s.Powered);
        int down = grid.Feeders.Count(f => f.Down);
        int open = grid.Feeders.Count(f => f.Breaker != BreakerState.Closed);
        string plants = string.Join(", ", grid.Sites
            .Where(s => grid.PlantOf(s) is not null)
            .Select(s => $"{s.Name} {grid.PlantOf(s)!.State.ToString().ToLowerInvariant()}"));
        string here = _sim.SiteAt(_playerActor.Position) is { } site ? $"\n  here: {site.Name}, {grid.DescribeSite(site)}" : "";
        return $"Power: {lit} of {grid.Sites.Count} sites lit, {down} lines down, {open} breakers open or tripped; {plants}{here}\n";
    }
}
