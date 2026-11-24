using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Campaigns;

namespace BlueHarvest.Sim.Events;

/// <summary>
/// <c>expedition.ended</c> (P3c): the campaign has recorded an expedition's end, once, after the survivor's death or
/// extraction committed. Its cause is the request that recorded it. It has no C# event on <see cref="Simulation"/>.
/// </summary>
public sealed record ExpeditionEndedPayload(Actor Survivor, string ExpeditionId, ExpeditionOutcome Outcome, string Detail)
    : SimEventPayload
{
    public const string TypeName = "expedition.ended";

    public override string Type => TypeName;

    public override Actor? Who => Survivor;
}

/// <summary><c>intermission.begun</c> (P3c): the days between two expeditions start to pass: how many, and how many were bought off.</summary>
public sealed record IntermissionBegunPayload(int Number, int Days, int DaysBought) : SimEventPayload
{
    public const string TypeName = "intermission.begun";

    public override string Type => TypeName;
}

/// <summary><c>intermission.ended</c> (P3c): the intermission's days have all passed; the island waits for its next arrival.</summary>
public sealed record IntermissionEndedPayload(int Number, int Days) : SimEventPayload
{
    public const string TypeName = "intermission.ended";

    public override string Type => TypeName;
}
