namespace BlueHarvest.Sim.Actors;

/// <summary>What one person can tell another to do. Whether they do it is up to them.</summary>
public enum NpcOrder : byte
{
    /// <summary>Keep close to whoever gave the order.</summary>
    Follow,

    /// <summary>Stay put and keep still.</summary>
    Stay,

    /// <summary>Get to cover and keep still there.</summary>
    Hide,
}

/// <summary>The last order someone was given: what, by whom, when.</summary>
public sealed record StandingOrder(NpcOrder Order, int FromActorId, long Tick);

/// <summary>
/// Who a person is under pressure, 0-100. Courage makes them help others and hold their nerve;
/// selfishness makes them ignore orders and leave others behind; fear past the panic threshold
/// makes them run and scream. Knowing that the Rex hunts by motion makes them freeze instead of run.
/// </summary>
public sealed record Traits(int Courage, int Selfishness, int PanicThreshold, bool KnowsMotionVision)
{
    public static readonly Traits Steady = new(Courage: 50, Selfishness: 30, PanicThreshold: 60, KnowsMotionVision: false);
}
