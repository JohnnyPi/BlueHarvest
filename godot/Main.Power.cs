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

    /// <summary>Tooltip lines: the equipment on this tile, and how the facility around it is doing.</summary>
    private void AddPowerFacts(List<(string Text, Color? Color)> facts, TilePos tile)
    {
        if (_sim.Power is not { } grid)
            return;
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
