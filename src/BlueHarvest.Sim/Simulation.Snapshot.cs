using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim;

/// <summary>
/// What a snapshot (P3b, <see cref="Snapshots.ExpeditionSnapshot"/>) reads from the simulation and puts back into
/// it, beyond what the public surface already shows. Only the snapshot code calls these, while the session is quiet.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>Puts the clock back: the tick, the time of day, and how far the window has scrolled since the start.</summary>
    internal void RestoreClock(long tick, long? clockStartMinute, TilePos windowOrigin, int windowShifts)
    {
        if (_actors.Count > 0)
            throw new InvalidOperationException("The clock is put back before anyone is added.");
        CurrentTick = tick;
        Clock = clockStartMinute is { } start ? new WorldClock(start) : null;
        WindowOrigin = windowOrigin;
        WindowShifts = windowShifts;
        _lightGrid = null;
    }

    /// <summary>The weather's scent clock (see <see cref="World.Weather.Wash"/>), for a snapshot.</summary>
    internal (long Base, long Since) WeatherWash => Weather.WashState;

    /// <summary>Puts the weather back exactly, and whether it follows the island's source (it stops when set by hand).</summary>
    internal void RestoreWeather(WeatherSettings settings, long washBase, long washSince, bool follow)
    {
        Weather.Restore(settings, washBase, washSince);
        _followWeather = follow && WeatherSource is not null;
        _lightGrid = null;
    }

    /// <summary>Where an actor last looked over the island from (island metres) and how far, or null.</summary>
    internal (long X, long Y, double Range)? LastSight(Actor actor) =>
        _lastSight.TryGetValue(actor.Id, out var last) ? last : null;

    /// <summary>Puts back where an actor last looked from, so the next look is decided as it would have been.</summary>
    internal void RestoreLastSight(Actor actor, (long X, long Y, double Range)? last)
    {
        if (last is { } l)
            _lastSight[actor.Id] = l;
        else
            _lastSight.Remove(actor.Id);
    }
}
