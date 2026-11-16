#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Sim;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Game;

/// <summary>What the simulation's events say in the log.</summary>
public partial class Main
{
    private void SubscribeToEvents()
    {
        _sim.MoveBlocked += OnMoveBlocked;
        _sim.ActorStruck += OnActorStruck;
        _sim.ActorBitten += OnActorBitten;
        _sim.VehicleDamaged += OnVehicleDamaged;
        _sim.VehicleShoved += OnVehicleShoved;
        _sim.PlatformCollapsed += OnPlatformCollapsed;
        _sim.ActorFell += OnActorFell;
        _sim.ActorInjured += OnActorInjured;
        _sim.ActorDied += OnActorDied;
        _sim.ActorBlackedOut += OnActorBlackedOut;
        _sim.ActorTreated += OnActorTreated;
        _sim.ItemThrown += OnItemThrown;
        _sim.Rappelled += OnRappelled;
        _sim.ActorScreamed += OnActorScreamed;
        _sim.OrderGiven += OnOrderGiven;
        _sim.LightningStruck += OnLightningStruck;
        _sim.ActorEscaped += OnActorEscaped;
        _sim.SparksDied += OnSparksDied;
        _sim.WeatherChanged += OnWeatherChanged;
        _sim.PowerChanged += OnPowerChanged;
        _sim.PowerOperated += OnPowerOperated;
        _sim.Shocked += OnShocked;
        _sim.DoorForced += OnDoorForced;
        _sim.RideEnded += OnRideEnded;
        _sim.WaterMoved += OnWaterMoved;
        _sim.WaterChanged += OnWaterChanged;
        _sim.Swept += OnSwept;
        _sim.FireStarted += OnFireStarted;
        _sim.FiresOut += OnFiresOut;
        _sim.Burned += OnBurned;
        _sim.Kindled += OnKindled;
        _sim.TanksBurst += OnTanksBurst;
        _sim.LightningHitGround += OnLightningHitGround;
        SubscribeToItemEvents();
        // what the weather is at the start, without saying so
        _weatherQuiet = true;
        OnWeatherChanged(_sim.Weather.Settings);
        _weatherQuiet = false;
    }

    // The weather as last told to the player: rain in bands (none, drizzle, rain, downpour), in the
    // cloud or not, thunder, where the wind comes from. Bands change with a little slack, so rain
    // hovering at an edge doesn't say "the rain eases" and "the rain gets heavier" by turns.
    private static readonly int[] RainBandUp = { 1, 30, 70 };
    private static readonly int[] RainBandDown = { 0, 1, 25, 60 };
    private const int CloudClosesAt = 35, CloudLiftsBelow = 25;
    private int _rainBand;
    private bool _inCloud, _thunder;
    private Direction _windFrom;
    private bool _weatherQuiet;

    /// <summary>
    /// The log notices the island's weather turning: rain starting, stopping, getting heavier or
    /// easing, cloud closing in or lifting, thunder, the wind swinging round. Quiet for weather set
    /// by hand (debug presets) and while <see cref="_weatherQuiet"/> is set.
    /// </summary>
    private void OnWeatherChanged(WeatherSettings now)
    {
        bool announce = _sim.FollowingWeatherSource && !_weatherQuiet;

        int was = _rainBand, band = _rainBand;
        while (band < 3 && now.RainPercent >= RainBandUp[band])
            band++;
        while (band > 0 && now.RainPercent < RainBandDown[band])
            band--;
        _rainBand = band;
        if (announce && band != was)
        {
            Say(band == 0 ? "The rain stops."
                : was == 0 ? (band == 1 ? "A light drizzle starts." : "It starts to rain.")
                : band > was ? (band == 3 ? "The rain turns to a downpour." : "The rain gets heavier.")
                : "The rain eases.");
        }

        bool inCloud = _inCloud ? now.FogPercent >= CloudLiftsBelow : now.FogPercent >= CloudClosesAt;
        if (announce && inCloud != _inCloud)
            Say(inCloud ? "Cloud closes in around you." : "The cloud lifts.");
        _inCloud = inCloud;

        if (announce && now.Storm != _thunder)
            Say(now.Storm ? "Thunder rumbles somewhere off." : "The thunder moves away.");
        _thunder = now.Storm;

        if (announce && now.WindFrom != _windFrom && now.WindSpeed > 0)
            Say($"The wind swings round; it comes from the {WindName(now.WindFrom)} now.");
        _windFrom = now.WindFrom;
    }

    private static string WindName(Direction from) => from switch
    {
        Direction.North => "north",
        Direction.NorthEast => "north-east",
        Direction.East => "east",
        Direction.SouthEast => "south-east",
        Direction.South => "south",
        Direction.SouthWest => "south-west",
        Direction.West => "west",
        _ => "north-west",
    };

    private void OnLightningStruck(LightningStrike strike)
    {
        _flashAlpha = 0.55f;
        int seconds = strike.ThunderDelayTicks / Ticks.PerSecond;
        Say($"Lightning! Everything stands out for a moment. Thunder in about {Math.Max(1, seconds)} s.");
    }

    private void OnActorStruck(Actor striker, Actor victim)
    {
        Say(victim == _playerActor
            ? $"The {striker.Name}'s tail slams into you!"
            : $"The {striker.Name}'s tail hits {victim.Name}.");
    }

    private void OnActorBitten(Actor biter, Actor victim)
    {
        Say(victim == _playerActor
            ? $"The {biter.Name}'s jaws close on you!"
            : $"The {biter.Name} bites {victim.Name}!");
    }

    private void OnVehicleDamaged(Vehicle vehicle, Actor by, int amount)
    {
        bool inside = _sim.VehicleAt(_playerActor.Position) == vehicle;
        string who = inside ? "your vehicle" : vehicle.Name;
        Say(vehicle.Breached
            ? $"The {by.Name} tears {who} open!"
            : $"The {by.Name} crunches into {who} ({vehicle.Integrity}% left).");
    }

    private void OnVehicleShoved(Vehicle vehicle, Actor by)
    {
        bool inside = _sim.VehicleAt(_playerActor.Position) == vehicle;
        Say(inside ? $"The {by.Name} shoves your vehicle along with you in it!" : $"The {by.Name} shoves {vehicle.Name}.");
    }

    private void OnPlatformCollapsed(Platform platform, Actor? by, IReadOnlyList<Actor> fell)
    {
        string who = by is null ? "It" : $"The {by.Name}";
        Say(fell.Contains(_playerActor)
            ? $"{who} brings the platform down - you fall!"
            : $"{who} brings a platform crashing down" + (fell.Count > 0 ? $"; {string.Join(", ", fell.Select(a => a.Name))} fall." : "."));
    }

    private void OnActorFell(Actor actor, int dropDm)
    {
        if (actor == _playerActor)
            Say($"You drop {Units.FormatMeters(dropDm)}.");
    }

    private void OnActorInjured(Actor actor, BodySection section, Severity severity, string cause)
    {
        if (actor != _playerActor)
            return;
        string how = severity == Severity.Red ? "badly hurt" : "hurt";
        string bleeding = actor.Health[section].Bleeding ? ", bleeding" : "";
        Say($"Your {InjuryRules.Name(section)} is {how}{bleeding} ({cause}).");
    }

    private void OnActorDied(Actor actor, string cause)
    {
        if (actor != _playerActor)
            Say($"{actor.Name} is dead ({cause}).");
    }

    private void OnActorBlackedOut(Actor actor)
    {
        if (actor == _playerActor)
            Say("Everything goes dark for a moment...");
    }

    private void OnActorTreated(Actor treater, Actor patient, BodySection section, bool worked)
    {
        string part = InjuryRules.Name(section);
        if (treater == _playerActor && patient == _playerActor)
            Say(worked
                ? $"You patch up your {part}. It still hurts, but the bleeding has stopped."
                : $"Your hands shake; the dressing on your {part} won't hold.");
        else if (treater == _playerActor)
            Say(worked
                ? $"You patch up {patient.Name}'s {part}."
                : $"Your hands shake; the dressing on {patient.Name}'s {part} won't hold.");
        else if (patient == _playerActor)
            Say(worked
                ? $"{treater.Name} patches up your {part}. The bleeding stops."
                : $"{treater.Name} tries to dress your {part}, but it won't hold.");
        else if (worked)
            Say($"{treater.Name} patches up {patient.Name}.");
    }

    private void OnActorScreamed(Actor actor)
    {
        if (actor != _playerActor)
            Say($"{actor.Name} screams!");
    }

    private void OnOrderGiven(Actor by, Actor target, NpcOrder order)
    {
        if (by != _playerActor)
            return;
        Say(order switch
        {
            NpcOrder.Follow => $"\"{target.Name}, stay with me.\"",
            NpcOrder.Stay => $"\"{target.Name}, stay here. Don't move.\"",
            _ => $"\"{target.Name}, find cover and hide!\"",
        });
    }

    private void OnItemThrown(Actor actor, ItemKind item, TilePos landed)
    {
        if (actor == _playerActor)
            Say($"The flare lands at {landed}, hissing and spitting red light.");
    }

    private void OnRappelled(Actor actor, bool slipped)
    {
        if (actor == _playerActor)
            Say(slipped
                ? "Your grip goes and you drop the rest of the way!"
                : "You lower yourself down the rope. It stays tied up there.");
    }

    private void OnMoveBlocked(Actor actor, string reason)
    {
        if (actor == _playerActor)
            Say($"Blocked: {reason}.");
    }

    private void OnActorEscaped(Actor actor, ExitPoint exit)
    {
        if (actor != _playerActor)
            Say($"{actor.Name} got out through the {exit.Name}.");
    }

    private void OnSparksDied(SparkingFence sparks)
    {
        Say("The sagging fence snaps with a shower of sparks. Something is coming through.");
    }
}
