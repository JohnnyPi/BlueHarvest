using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Events;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// What an hour of intermission did that wasn't simulated in full (P3c): lightning that would have struck, whether a
/// storm was overhead when the hour began, fires that burnt out, and how many changes the power network went through.
/// </summary>
public readonly record struct IntermissionHour(int Strikes, bool Storm, int FiresBurntOut, int PowerEvents);

public sealed partial class Simulation
{
    /// <summary>How much time one step of an intermission covers: a game hour.</summary>
    public const long IntermissionStepTicks = 60L * WorldClock.TicksPerGameMinute;

    /// <summary>
    /// An actor leaves the island by an extraction route (P3c): they're off the map with everything they carry, as if
    /// they had stepped onto an exit named for the route. Only an actor who is here and alive can leave. True if they
    /// left. The real routes (the docks boat, the radio pickup) arrive in P7b; until then the tests and Godot's debug
    /// panel call this.
    /// </summary>
    public bool Extract(Actor actor, string route)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        if (actor.IsGone || actor.Id < 0 || actor.Id >= _actors.Count || !ReferenceEquals(_actors[actor.Id], actor))
            return false;
        Session.BeginRequest();
        try
        {
            Escape(actor, new ExitPoint(actor.Position, route));
        }
        finally
        {
            Session.EndRequest();
        }
        return true;
    }

    /// <summary>
    /// One hour of an intermission (P3c), or less if <paramref name="until"/> comes first: a request of its own. Nobody
    /// on the map acts; the world goes on at the hour's grain. The power network takes every minute's step, as it does
    /// in play, so plants trip, generators burn fuel and run dry and batteries drain; then the weather, the tide and the
    /// rivers are brought to the hour's end, and the lamps, doors and fences into line with the network. Lightning
    /// isn't struck, only counted; fires already burning burn out where they stand, without spreading. The clocks that
    /// play reads are moved to the hour's end, so nothing replays the minutes skipped.
    /// </summary>
    internal IntermissionHour PassIntermissionHour(long until)
    {
        if (until <= CurrentTick)
            throw new ArgumentOutOfRangeException(nameof(until), "The intermission's end is already past.");
        if (_actors.FirstOrDefault(a => !a.IsGone) is { } here)
            throw new InvalidOperationException($"Time only passes by the hour with nobody about, and {here.Name} is still here.");

        long from = CurrentTick;
        long to = Math.Min(until, from + IntermissionStepTicks);
        bool storm = Weather.Settings.Storm;
        int strikes = Weather.StrikesBetween(from, to).Count();
        int burnt, power = 0;
        Session.BeginRequest();
        try
        {
            burnt = BurnOutForIntermission();
            if (Power is not null)
            {
                // the network's own minute steps, in order, by the weather of each minute at each site
                for (long b = (from / WeatherStepTicks + 1) * WeatherStepTicks; b <= to; b += WeatherStepTicks)
                {
                    long minute = GameMinute(b);
                    foreach (var e in Power.Step(minute, SunPercent(b), PowerWeather(minute)))
                    {
                        Emit(new PowerChangedPayload(e));
                        power++;
                    }
                }
            }

            CurrentTick = to;
            if (FollowingWeatherSource)
                ApplySourceWeather(to);
            if (Power is not null)
                SyncPowerWorld();
            if (Waters is not null)
            {
                SetLevels(GameMinute(to), quiet: true);
                ApplyWater();
                RefreshWaterMask();
            }
            ExpireLights();
            SyncItemLights();
            _weatherTick = to;
            _sparkTick = to;
            _fireTick = to;
            _machineryTick = to;
            _lightGrid = null;
        }
        finally
        {
            Session.EndRequest();
        }
        return new IntermissionHour(strikes, storm, burnt, power);
    }

    /// <summary>
    /// Every fire on the map burns out where it stands, leaving its ground burnt through; spilled fuel dries, flares on
    /// the ground are spent and the smoke clears. Returns how many fires there were.
    /// </summary>
    private int BurnOutForIntermission()
    {
        if (_fires.Count == 0 && _fuel.Count == 0 && _groundFlares.Count == 0 && !_smokeOnMap)
            return 0;
        var burning = _fires.Values.OrderBy(f => f.Position.Y).ThenBy(f => f.Position.X).ToList();
        foreach (Fire fire in burning)
        {
            fire.StepsBurnt = Math.Max(fire.StepsBurnt, 2); // it had all the time it needed to take hold
            BurnOut(fire);
        }
        _fuel.Clear();
        _groundFlares.Clear();
        UpdateSmoke();
        UpdateFireLights();
        if (burning.Count > 0)
            Emit(new FiresOutPayload());
        return burning.Count;
    }
}
