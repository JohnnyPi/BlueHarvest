#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using Sky = BlueHarvest.Sim.World.Sky; // Godot has a Sky of its own

namespace BlueHarvest.Game;

/// <summary>
/// Waiting for the light to change: Z sits the player down until the next dawn or dusk, a game minute
/// at a time, and gets them up early for the same things that stop a click-to-walk. It only ever
/// submits ordinary waits, so the simulation knows nothing about it.
/// </summary>
public partial class Main
{
    /// <summary>How long each wait is: one game minute (six action seconds). One is taken per frame.</summary>
    private const int RestStepTicks = WorldClock.TicksPerGameMinute;

    private Resting? _rest;

    /// <summary>A wait in progress: when it ends, and what gets you up early.</summary>
    private sealed class Resting
    {
        public Resting(long untilTick, string goal, HashSet<int> seenAtStart, int woundsAtStart, bool spottedAtStart)
        {
            UntilTick = untilTick;
            Goal = goal;
            SeenAtStart = seenAtStart;
            WoundsAtStart = woundsAtStart;
            SpottedAtStart = spottedAtStart;
        }

        public long UntilTick { get; }
        public string Goal { get; }
        public HashSet<int> SeenAtStart { get; }
        public int WoundsAtStart { get; }
        public bool SpottedAtStart { get; }
    }

    /// <summary>Starts waiting for dawn (06:00) or dusk (18:00), whichever comes first.</summary>
    private void StartRest()
    {
        if (_sim.Clock is not { } clock)
        {
            Say("There's no telling the time here: nothing to wait for.");
            return;
        }
        long now = _sim.CurrentTick;
        long toDawn = clock.TicksUntil(now, Sky.Sunrise / 60), toDusk = clock.TicksUntil(now, Sky.Sunset / 60);
        string goal = toDawn < toDusk ? "dawn" : "dusk";
        _rest = new Resting(now + Math.Min(toDawn, toDusk), goal, LargeActorsInView(), WoundScore(), _rexSeesYou);
        Say($"You settle down to wait for {goal}.");
        SetPrompt($"Waiting for {goal}. Any key or click stops.");
    }

    /// <summary>One more game minute of waiting, then the checks that end it.</summary>
    private void StepRest()
    {
        var rest = _rest!;
        long left = rest.UntilTick - _sim.CurrentTick;
        if (_playerActor.IsGone || _mode != InputMode.Normal || left <= 0)
        {
            StopRest(null);
            return;
        }

        Submit(new WaitAction((int)Math.Min(left, RestStepTicks)));
        if (_rest != rest)
            return; // the turn ended it (the run is over)

        var newlySeen = LargeActorsInView().Except(rest.SeenAtStart).ToList();
        if (newlySeen.Count > 0)
            StopRest($"You get up: {(_sim.Actors[newlySeen[0]] == _rex ? "the Rex" : _sim.Actors[newlySeen[0]].Name)} is in view.");
        else if (_rexSeesYou && !rest.SpottedAtStart)
            StopRest("You get up: the Rex has seen you.");
        else if (WoundScore() > rest.WoundsAtStart)
            StopRest("You get up: you're hurt.");
        else if (_sim.CurrentTick >= rest.UntilTick)
            StopRest($"It's {rest.Goal}.");
    }

    private void StopRest(string? why)
    {
        if (_rest is null)
            return;
        _rest = null;
        SetPrompt(null);
        if (why is not null)
            Say(why);
    }
}
