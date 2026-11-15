#nullable enable
using System;
using System.Collections.Generic;
using BlueHarvest.Game.Rendering;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.World;
using Godot;
using Direction = BlueHarvest.Sim.World.Direction;

namespace BlueHarvest.Game;

/// <summary>Fire in the log, the menu, the tooltips and the F9 readout.</summary>
public partial class Main
{
    /// <summary>How far away (tiles) a fire starting is worth a line in the log when you can't see it.</summary>
    private const int FireHeardRange = 60;

    private void OnFireStarted(TilePos tile, string cause)
    {
        string what = Covers.Get(_sim.Cover[tile]).Name;
        if (TileInView(tile))
            Say(cause == _playerActor.Name ? $"The {what} catches. Fire!" : $"Fire! {Capitalize(cause)} sets the {what} alight.");
        else if (Chebyshev(_playerActor.Position, tile) <= FireHeardRange)
            Say("You smell smoke on the air.");
    }

    private void OnFiresOut() => Say("The last of the fire dies down to smoking ash.");

    private void OnBurned(Actor actor, Severity severity)
    {
        if (actor == _playerActor)
            Say(severity == Severity.Red ? "The flames sear you: a bad burn." : "The heat bites: you're burnt.");
        else if (Shown(actor))
            Say($"{actor.Name} is caught in the flames.");
    }

    private void OnKindled(Actor actor, TilePos tile, bool caught)
    {
        if (actor == _playerActor && !caught)
            Say(_sim.BurnableAt(tile) > 0
                ? "You hold the flare to it. It smoulders and won't catch; maybe the flare will do it yet."
                : "You drop the flare. Nothing there will burn.");
    }

    private void OnTanksBurst(TilePos tile)
    {
        if (TileInView(tile) || Chebyshev(_playerActor.Position, tile) <= FireHeardRange * 2)
            Say("With a roar the fuel tanks go up, throwing burning fuel across the yard!");
    }

    private void OnLightningHitGround(TilePos tile)
    {
        if (Chebyshev(_playerActor.Position, tile) <= 40)
            Say("Lightning strikes the ground close by: the air stinks of it.");
    }

    private bool TileInView(TilePos tile) => _map.FogAt(tile) == FogState.Visible;

    /// <summary>The menu entry for setting a tile alight with a flare, on your own tile or next to it.</summary>
    private void AddFireChoices(List<MenuChoice> choices, TilePos tile, int reach)
    {
        if (reach > 1 || !_playerActor.Inventory.Has(ItemKind.Flare) || _sim.FireAt(tile) is not null)
            return;
        int burnable = _sim.BurnableAt(tile);
        if (burnable <= 0 && !_sim.FuelAt(tile))
            return;
        Direction? way = reach == 0 ? null : Directions.FromDelta(tile.X - _playerActor.Position.X, tile.Y - _playerActor.Position.Y);
        string hint = burnable >= 60 ? "tinder dry: it will go up" : burnable >= 40 ? "it should catch" : "damp: it may not take";
        choices.Add(new MenuChoice("Set it alight with a flare", () => Submit(new IgniteAction(way)), hint));
    }

    /// <summary>Tooltip lines about fire on a tile: burning, how readily it would, fuel and smoke.</summary>
    private void AddFireFacts(List<(string Text, Color? Color)> facts, TilePos tile)
    {
        if (_sim.FireAt(tile) is { } fire)
            facts.Add(($"On fire ({fire.Intensity}%): it burns whoever stands in it", HudTheme.Danger));
        else
        {
            int burnable = _sim.BurnableAt(tile);
            if (_sim.FuelAt(tile))
                facts.Add(("Fuel spilled here: a spark would set it off", HudTheme.Danger));
            else if (burnable >= 60)
                facts.Add(("Tinder dry: it would burn readily", null));
            else if (burnable > 0 && FireRules.Of(_sim.Cover[tile]).Burnable > 0 && burnable < 25)
                facts.Add(("Too damp to burn much now", null));
        }
        int smoke = _sim.Map.Smoke[tile];
        if (smoke >= 20)
            facts.Add(($"{(smoke >= 50 ? "Thick" : "Drifting")} smoke: it hides what's beyond and drowns scent", null));
    }

    /// <summary>The F9 readout's fire line.</summary>
    private string FireReadout()
    {
        if (_sim.Fires.Count == 0 && _sim.TilesBurnt == 0)
            return $"Fire: none   recent rain {_sim.RecentRainPercent}%   here burns at {_sim.BurnableAt(_playerActor.Position)}\n";
        return $"Fire: {_sim.Fires.Count} tiles burning, {_sim.TilesBurnt} burnt out   recent rain {_sim.RecentRainPercent}%\n";
    }
}
