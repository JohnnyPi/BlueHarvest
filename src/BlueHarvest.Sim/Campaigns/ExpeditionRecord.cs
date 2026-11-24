namespace BlueHarvest.Sim.Campaigns;

/// <summary>How an expedition stands: still going, or ended by the survivor's death or extraction (the only two ends).</summary>
public enum ExpeditionOutcome
{
    Active,
    Died,
    Extracted,
}

/// <summary>Where a campaign is between its expeditions.</summary>
public enum CampaignPhase
{
    /// <summary>Made, with no expedition yet.</summary>
    New,

    /// <summary>A survivor is on the island.</summary>
    Exploring,

    /// <summary>The survivor has died or left; the intermission hasn't begun.</summary>
    Ended,

    /// <summary>The days between expeditions are passing.</summary>
    Intermission,

    /// <summary>The intermission is over; the island waits for its next survivor (who arrives in P7d).</summary>
    AwaitingArrival,
}

/// <summary>
/// One survivor's expedition on a campaign's island (contracts §8): who they were, when they came and went, how it
/// ended and where, and what it paid. The survivor themselves (place, needs, kit, knowledge) is an actor in the world
/// snapshot; a dead survivor's body stays there, with what they carried, as the actor <see cref="ActorId"/>.
/// </summary>
public sealed class ExpeditionRecord
{
    internal ExpeditionRecord(string id, int number, string personId, int actorId, string survivorName, long startTick)
    {
        Id = id;
        Number = number;
        PersonId = personId;
        ActorId = actorId;
        SurvivorName = survivorName;
        StartTick = startTick;
    }

    /// <summary>The expedition's ID in its campaign: <c>exp:&lt;n&gt;</c>.</summary>
    public string Id { get; }

    /// <summary>1 for the campaign's first expedition, and so on.</summary>
    public int Number { get; }

    /// <summary>The survivor as a person (<c>person:&lt;n&gt;</c>): who they are, as distinct from their body in the sim.</summary>
    public string PersonId { get; }

    /// <summary>The survivor's actor in the world snapshot (alive, gone or a body).</summary>
    public int ActorId { get; }

    public string SurvivorName { get; }

    /// <summary>When they arrived.</summary>
    public long StartTick { get; }

    /// <summary>When they died or left, once they have.</summary>
    public long? EndTick { get; internal set; }

    public ExpeditionOutcome Outcome { get; internal set; }

    /// <summary>What killed them, or the route they left by.</summary>
    public string? Detail { get; internal set; }

    /// <summary>Where it ended, in island metres on the surface: where the body lies, or where they left from.</summary>
    public (long X, long Y)? EndAt { get; internal set; }

    /// <summary>Whether it ended up on a deck rather than on the ground.</summary>
    public bool EndOnDeck { get; internal set; }

    /// <summary>The run's score, paid into the profile's wallet once (scoring arrives in P7b; until then 0).</summary>
    public int Score { get; internal set; }

    /// <summary>An extraction's intermission currency, spent on its intermission's days or lost (P7b; until then 0).</summary>
    public int IntermissionCurrency { get; internal set; }

    /// <summary>The wallet request that pays <see cref="Score"/> into the profile, once it has ended.</summary>
    public string? ScoreRequestId { get; internal set; }
}

/// <summary>
/// One line of a campaign's chronicle: the island's own history, kept for good (P3c keeps the milestones only:
/// arrivals, deaths, extractions and intermissions). Append-only: later entries never remove earlier ones.
/// </summary>
public sealed record ChronicleEntry(int Number, long Tick, string Kind, string? ExpeditionId, string Text, (long X, long Y)? At);

/// <summary>The kinds of chronicle entry.</summary>
public static class ChronicleKinds
{
    public const string ExpeditionStarted = "expedition.started";
    public const string SurvivorDied = "survivor.died";
    public const string SurvivorExtracted = "survivor.extracted";
    public const string IntermissionPassed = "intermission.passed";
}
