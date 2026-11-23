namespace BlueHarvest.Sim.Events;

/// <summary>
/// Who sees a committed request's events, in this order (contracts §4): every event of the request goes through one
/// stage before any goes through the next.
/// </summary>
public enum ObserverStage
{
    /// <summary>The sim's own projections: knowledge, facts, custody.</summary>
    Projections,

    /// <summary>Mission observers (P6f, P14).</summary>
    Missions,

    /// <summary>Scripts at the post-commit boundary (P9).</summary>
    Scripts,

    /// <summary>Presentation: the front end. The simulation's C# events are raised here, before these observers.</summary>
    Presentation,
}
