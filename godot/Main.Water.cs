#nullable enable
using System;
using System.Collections.Generic;
using BlueHarvest.Game.Ui;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game;

/// <summary>The tide, the rivers and the surf in the log, the tooltips and the F9 readout.</summary>
public partial class Main
{
    /// <summary>The tide or a river moved over the map: the water animation has to be worked out again.</summary>
    private void OnWaterMoved() => _ambient.MapChanged();

    private void OnWaterChanged(WaterEvent e) => Say(e.Text);

    private void OnSwept(Actor actor, TilePos to)
    {
        if (actor == _playerActor)
            Say("The current takes your feet from under you and carries you downstream.");
        else if (Shown(actor))
            Say($"{actor.Name} is carried off downstream.");
    }

    /// <summary>Tooltip lines about the water on a tile: tide, flood, current and surf.</summary>
    private void AddWaterFacts(List<(string Text, Color? Color)> facts, TilePos tile)
    {
        if (_sim.Waters is null)
            return;
        WaterTile w = _sim.Map.Water[tile];
        CoverType cover = _sim.Cover[tile];
        bool wet = cover is CoverType.ShallowWater or CoverType.DeepWater;
        if (w.Kind == WaterKind.Sea)
        {
            int depth = _sim.SeaLevelDm - w.GroundDm;
            facts.Add(wet
                ? ($"The sea's edge: {Units.FormatMeters(Math.Max(0, depth))} deep at this tide, {(_sim.TideRising ? "rising" : "falling")}", null)
                : ($"Between the tides: {(w.GroundDm < WaterLevels.SpringAmplitudeDm ? "the sea covers it at high water" : "only the biggest tides reach it")}", null));
        }
        else if (w.Kind == WaterKind.River)
        {
            int rise = _sim.RiverRiseDm(w.River);
            string stage = rise >= Simulation.RiverFloodDm ? "in flood" : rise >= Simulation.RiverHighDm ? "running high" : "at its usual level";
            if (wet)
                facts.Add(($"A river {stage}: {Units.FormatMeters(Math.Max(0, rise - w.GroundDm))} deep", rise >= Simulation.RiverHighDm ? (Color?)HudTheme.Danger : null));
            else
                facts.Add(($"A river bank, {Units.FormatMeters(w.GroundDm)} above the water's usual level; the river is {stage}", null));
        }
        WaterFlow current = _sim.CurrentAt(tile);
        if (!current.IsNone)
        {
            int speed = current.SpeedDm;
            facts.Add(($"Current {speed / 10}.{speed % 10} m/s{(speed >= Simulation.SweepSpeedDm ? ": fast enough to sweep you off your feet" : "")}",
                speed >= Simulation.SweepSpeedDm ? (Color?)HudTheme.Danger : null));
        }
        int surf = _sim.SurfPercentAt(tile);
        if (surf >= 30)
            facts.Add(($"Surf {(surf >= 70 ? "pounding" : surf >= 45 ? "heavy" : "breaking")} here ({surf}%): it drowns out sounds along the shore", null));
    }

    /// <summary>The F9 readout's water line.</summary>
    private string WaterReadout()
    {
        if (_sim.Waters is not { } waters)
            return "";
        long minute = _sim.GameMinute(_sim.CurrentTick);
        string tide = $"Tide {(_sim.SeaLevelDm >= 0 ? "+" : "")}{_sim.SeaLevelDm / 10.0:0.0} m, {(_sim.TideRising ? "rising" : "falling")}   swell {waters.SwellPercent(minute)}%";
        WaterTile here = _sim.Map.Water[_playerActor.Position];
        string river = here.Kind == WaterKind.River ? $"   this river +{_sim.RiverRiseDm(here.River) / 10.0:0.0} m" : "";
        return $"{tide}{river}   sound lost to water here {_sim.WaterMaskAt(_playerActor.Position)}\n";
    }
}
