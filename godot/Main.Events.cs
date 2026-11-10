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
    }

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
