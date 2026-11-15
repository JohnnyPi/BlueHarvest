namespace BlueHarvest.Sim.World;

/// <summary>
/// Weather that differs by place and changes with the time: what it is like at a spot (in metres on
/// the island) at a game minute. It must give the same answer every time for the same spot and
/// minute, so a simulation that follows it replays exactly. See Simulation.FollowWeather.
/// </summary>
public interface IWeatherSource
{
    WeatherSettings At(long x, long y, long gameMinute);
}
