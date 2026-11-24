using System.Text.Json;
using System.Text.Json.Nodes;
using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Snapshots;
using BlueHarvest.Sim.World.Generation;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim.Campaigns;

/// <summary>
/// A campaign's save file (contracts §8, P3c): gzipped JSON in format <c>blueharvest.campaign/1</c>, kept as
/// <c>user://campaigns/&lt;slot&gt;/campaign.json.gz</c> (P3d writes the file). It holds the campaign's own records
/// (expeditions, chronicle, intermissions, the wallet requests it caused) and, once an expedition has started, the
/// world snapshot (<see cref="ExpeditionSnapshot"/>) inside it, so the two are always saved together. Never the
/// profile's balance: only the IDs of the requests the profile is asked to apply.
/// </summary>
public static class CampaignSave
{
    /// <summary>The format this build writes and reads.</summary>
    public const string Format = "blueharvest.campaign/1";

    /// <summary>The campaign, and its session's world if it has one, as gzipped JSON. Call it between turns.</summary>
    public static byte[] Save(IslandCampaign campaign) => Pack(Capture(campaign));

    /// <summary>The save as indented JSON text: for reading by eye and for tests.</summary>
    public static string ToJson(IslandCampaign campaign) =>
        Capture(campaign).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static JsonObject Capture(IslandCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        WorldSession? session = campaign.Session;
        if (campaign.Phase != CampaignPhase.New && session is null)
            throw new InvalidOperationException("A campaign under way is saved with its session.");
        return new JsonObject
        {
            ["format"] = Format,
            ["game"] = SimInfo.Version,
            ["slot"] = campaign.Slot,
            ["id"] = campaign.Id,
            ["island"] = JsonSerializer.SerializeToNode(campaign.Island),
            ["phase"] = Name(campaign.Phase),
            ["expeditions"] = new JsonArray(campaign.Expeditions.Select(e => (JsonNode?)WriteExpedition(e)).ToArray()),
            ["chronicle"] = new JsonArray(campaign.Chronicle.Select(c => (JsonNode?)WriteEntry(c)).ToArray()),
            ["intermissions"] = new JsonArray(campaign.Intermissions.Select(i => (JsonNode?)WriteIntermission(i)).ToArray()),
            ["profileRequests"] = new JsonArray(campaign.ProfileRequests.Select(r => (JsonNode?)WriteRequest(r)).ToArray()),
            ["world"] = session is { HasSurface: true } ? ExpeditionSnapshot.Capture(session) : null,
        };
    }

    private static JsonObject WriteExpedition(ExpeditionRecord e) => new()
    {
        ["id"] = e.Id,
        ["number"] = e.Number,
        ["person"] = e.PersonId,
        ["actor"] = e.ActorId,
        ["name"] = e.SurvivorName,
        ["start"] = e.StartTick,
        ["end"] = e.EndTick,
        ["outcome"] = Name(e.Outcome),
        ["detail"] = e.Detail,
        ["space"] = e.EndAt is null ? null : "surface",
        ["endAt"] = e.EndAt is { } at ? Pair(at.X, at.Y) : null,
        ["endDeck"] = e.EndOnDeck,
        ["score"] = e.Score,
        ["currency"] = e.IntermissionCurrency,
        ["scoreRequest"] = e.ScoreRequestId,
    };

    private static JsonObject WriteEntry(ChronicleEntry c) => new()
    {
        ["number"] = c.Number,
        ["tick"] = c.Tick,
        ["kind"] = c.Kind,
        ["expedition"] = c.ExpeditionId,
        ["text"] = c.Text,
        ["space"] = c.At is null ? null : "surface",
        ["at"] = c.At is { } at ? Pair(at.X, at.Y) : null,
    };

    private static JsonObject WriteIntermission(IntermissionState i) => new()
    {
        ["number"] = i.Number,
        ["expedition"] = i.ExpeditionId,
        ["outcome"] = Name(i.Outcome),
        ["daysBought"] = i.DaysBought,
        ["start"] = i.StartTick,
        ["hours"] = i.HoursPassed,
        ["strikes"] = i.Strikes,
        ["stormHours"] = i.StormHours,
        ["firesBurntOut"] = i.FiresBurntOut,
        ["powerEvents"] = i.PowerEvents,
        ["done"] = i.Done,
    };

    private static JsonObject WriteRequest(WalletRequest r) => new()
    {
        ["request"] = r.RequestId,
        ["kind"] = Name(r.Kind),
        ["points"] = r.Points,
        ["purpose"] = r.Purpose,
        ["campaign"] = r.CampaignId,
        ["expedition"] = r.ExpeditionId,
        ["unlock"] = r.Unlock,
    };

    // ---------- reading ----------

    /// <summary>
    /// Reads a saved campaign: its records, and its world snapshot ready to be put back
    /// (<see cref="CampaignFile.Restore"/>). Throws <see cref="SnapshotRefusedException"/> for another format or a file
    /// that doesn't hold together.
    /// </summary>
    public static CampaignFile Read(byte[] data)
    {
        JsonObject doc = Unpack(data, "a saved campaign");
        string format = Val<string>(doc, "format");
        if (format != Format)
            throw new SnapshotRefusedException($"This campaign is in format '{format}'; this build reads '{Format}'.");
        int slot = Val<int>(doc, "slot");
        if (slot is < 1 or > IslandCampaign.SlotCount)
            throw Bad($"slot {slot} isn't one of the {IslandCampaign.SlotCount}");
        IslandParams island;
        try
        {
            island = Obj(doc, "island").Deserialize<IslandParams>() ?? throw Bad("the island's parameters are empty");
        }
        catch (JsonException e)
        {
            throw Bad($"the island's parameters don't read ({e.Message})");
        }
        var phase = EnumOf<CampaignPhase>(doc, "phase");
        List<ExpeditionRecord> expeditions = Arr(doc, "expeditions").Select(n => ReadExpedition(Obj(n, "an expedition"))).ToList();
        List<ChronicleEntry> chronicle = Arr(doc, "chronicle").Select(n => ReadEntry(Obj(n, "a chronicle entry"))).ToList();
        List<IntermissionState> intermissions = Arr(doc, "intermissions").Select(n => ReadIntermission(Obj(n, "an intermission"))).ToList();
        List<WalletRequest> requests = Arr(doc, "profileRequests").Select(n => ReadRequest(Obj(n, "a wallet request"))).ToList();
        Check(phase, expeditions, chronicle, intermissions);

        ExpeditionSave? world = null;
        if (doc["world"] is { } node)
        {
            world = new ExpeditionSave(Obj(node, "the world snapshot"));
            if (world.Format == ExpeditionSnapshot.Format && world.Island != island)
                throw Bad("the world snapshot is of another island");
        }
        if (phase != CampaignPhase.New && world is null)
            throw Bad("a campaign under way has no world snapshot");
        var campaign = IslandCampaign.Rebuild(slot, Val<string>(doc, "id"), island, phase, expeditions, chronicle, intermissions, requests);
        return new CampaignFile(campaign, world);
    }

    /// <summary>The records must agree with the phase: one expedition going on while exploring, and so on.</summary>
    private static void Check(CampaignPhase phase, List<ExpeditionRecord> expeditions, List<ChronicleEntry> chronicle,
        List<IntermissionState> intermissions)
    {
        for (int i = 0; i < expeditions.Count; i++)
        {
            if (expeditions[i].Number != i + 1)
                throw Bad($"expedition {expeditions[i].Id} is out of order");
            if (i < expeditions.Count - 1 && expeditions[i].Outcome == ExpeditionOutcome.Active)
                throw Bad($"expedition {expeditions[i].Id} never ended, but a later one began");
        }
        for (int i = 0; i < chronicle.Count; i++)
        {
            if (chronicle[i].Number != i + 1 || (i > 0 && chronicle[i].Tick < chronicle[i - 1].Tick))
                throw Bad($"chronicle entry {chronicle[i].Number} is out of order");
        }
        for (int i = 0; i < intermissions.Count; i++)
        {
            if (intermissions[i].Number != i + 1 || (i < intermissions.Count - 1 && !intermissions[i].Done))
                throw Bad($"intermission {intermissions[i].Number} is out of order");
        }
        ExpeditionOutcome? last = expeditions.Count > 0 ? expeditions[^1].Outcome : null;
        bool fits = phase switch
        {
            CampaignPhase.New => expeditions.Count == 0,
            CampaignPhase.Exploring => last == ExpeditionOutcome.Active,
            CampaignPhase.Ended => last is ExpeditionOutcome.Died or ExpeditionOutcome.Extracted
                && intermissions.Count < expeditions.Count,
            CampaignPhase.Intermission => intermissions.Count > 0 && expeditions.Count > 0 && !intermissions[^1].Done
                && intermissions[^1].ExpeditionId == expeditions[^1].Id,
            CampaignPhase.AwaitingArrival => intermissions.Count > 0 && expeditions.Count > 0 && intermissions[^1].Done
                && intermissions[^1].ExpeditionId == expeditions[^1].Id,
            _ => false,
        };
        if (!fits)
            throw Bad($"the campaign says it's {Name(phase)}, but its records don't agree");
    }

    private static ExpeditionRecord ReadExpedition(JsonObject o)
    {
        var e = new ExpeditionRecord(Val<string>(o, "id"), Val<int>(o, "number"), Val<string>(o, "person"),
            Val<int>(o, "actor"), Val<string>(o, "name"), Val<long>(o, "start"))
        {
            EndTick = o["end"] is null ? null : Val<long>(o, "end"),
            Outcome = EnumOf<ExpeditionOutcome>(o, "outcome"),
            Detail = OptString(o, "detail"),
            EndOnDeck = Val<bool>(o, "endDeck"),
            Score = Val<int>(o, "score"),
            IntermissionCurrency = Val<int>(o, "currency"),
            ScoreRequestId = OptString(o, "scoreRequest"),
        };
        if (o["endAt"] is not null)
        {
            Surface(o, $"expedition {e.Id}'s end");
            e.EndAt = ReadPair(o, "endAt");
        }
        if ((e.Outcome == ExpeditionOutcome.Active) != (e.EndTick is null))
            throw Bad($"expedition {e.Id} is {Name(e.Outcome)}, but its end tick says otherwise");
        return e;
    }

    private static ChronicleEntry ReadEntry(JsonObject o)
    {
        (long X, long Y)? at = null;
        if (o["at"] is not null)
        {
            Surface(o, "a chronicle entry");
            at = ReadPair(o, "at");
        }
        return new ChronicleEntry(Val<int>(o, "number"), Val<long>(o, "tick"), Val<string>(o, "kind"),
            OptString(o, "expedition"), Val<string>(o, "text"), at);
    }

    private static IntermissionState ReadIntermission(JsonObject o)
    {
        var outcome = EnumOf<ExpeditionOutcome>(o, "outcome");
        if (outcome == ExpeditionOutcome.Active)
            throw Bad("an intermission follows an expedition that never ended");
        int bought = Val<int>(o, "daysBought");
        if (bought < 0 || bought > IntermissionRules.BaseDays(outcome) - IntermissionRules.FloorDays
            || (outcome == ExpeditionOutcome.Died && bought != 0))
            throw Bad($"an intermission has {bought} days bought off, which it can't");
        return new IntermissionState(Val<int>(o, "number"), Val<string>(o, "expedition"), outcome, bought, Val<long>(o, "start"))
        {
            HoursPassed = Val<int>(o, "hours"),
            Strikes = Val<int>(o, "strikes"),
            StormHours = Val<int>(o, "stormHours"),
            FiresBurntOut = Val<int>(o, "firesBurntOut"),
            PowerEvents = Val<int>(o, "powerEvents"),
            Done = Val<bool>(o, "done"),
        };
    }

    private static WalletRequest ReadRequest(JsonObject o) => new(Val<string>(o, "request"), EnumOf<WalletKind>(o, "kind"),
        Val<int>(o, "points"), Val<string>(o, "purpose"), OptString(o, "campaign"), OptString(o, "expedition"), OptString(o, "unlock"));

    /// <summary>Every saved place is on the surface until interiors arrive (P10).</summary>
    private static void Surface(JsonObject o, string what)
    {
        if (OptString(o, "space") != "surface")
            throw Bad($"{what} is in space '{OptString(o, "space")}', and this build has only the surface");
    }
}

/// <summary>A campaign read back from its save, with its world snapshot ready to be put back.</summary>
public sealed class CampaignFile
{
    internal CampaignFile(IslandCampaign campaign, ExpeditionSave? world)
    {
        Campaign = campaign;
        World = world;
    }

    /// <summary>The campaign's records, not yet watching any session.</summary>
    public IslandCampaign Campaign { get; }

    /// <summary>The world as it was saved, or null for a campaign with no expedition yet.</summary>
    public ExpeditionSave? World { get; }

    /// <summary>What the slot picker shows.</summary>
    public CampaignHeader Header => new(Campaign.Slot, Campaign.Id, Campaign.Island.Seed, Campaign.Island.Scale, Campaign.Phase,
        Campaign.Expeditions.Count, World?.Tick ?? 0);

    /// <summary>Why the world can't be put back with this content and this build, or null if it can.</summary>
    public string? Refusal(ContentRegistry content) => World is null ? null : ExpeditionSnapshot.Refusal(World, content);

    /// <summary>
    /// Puts the world back in a new session (the island regenerated from <see cref="IslandCampaign.Island"/>) and sets
    /// the campaign watching it. Then offer the profile the campaign's requests (<see cref="IslandCampaign.SettleWith"/>).
    /// </summary>
    public Simulation Restore(WorldSession session, IslandWorld world, IslandChunker chunker, IActorController player,
        FeatureIndex? features = null)
    {
        if (World is null)
            throw new InvalidOperationException("This campaign has no expedition to put back yet.");
        Simulation sim = ExpeditionSnapshot.Restore(session, World, world, chunker, player, features);
        Campaign.Attach(session);
        return sim;
    }
}

/// <summary>What a saved campaign shows in the slot picker: its slot, ID, island, phase, expeditions and game time.</summary>
public sealed record CampaignHeader(int Slot, string Id, uint Seed, double Scale, CampaignPhase Phase, int Expeditions, long Tick);

/// <summary>The three save slots (P3c): where each campaign is kept, and which islands can go in which slot.</summary>
public static class CampaignSlots
{
    /// <summary>Where the profile is kept.</summary>
    public const string ProfilePath = "user://profile.json.gz";

    /// <summary>Where the campaign in a slot is kept.</summary>
    public static string PathOf(int slot)
    {
        if (slot is < 1 or > IslandCampaign.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), $"Campaign slots are 1 to {IslandCampaign.SlotCount}.");
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"user://campaigns/{slot}/campaign.json.gz");
    }

    /// <summary>
    /// Why a new campaign on this island can't go in this slot, or null if it can: the slot must be one of the three,
    /// and each slot keeps a different island seed (P3.2). The slot's own old campaign (being replaced) doesn't count.
    /// </summary>
    public static string? WhyNot(int slot, IslandParams island, IEnumerable<CampaignHeader> kept)
    {
        ArgumentNullException.ThrowIfNull(island);
        ArgumentNullException.ThrowIfNull(kept);
        if (slot is < 1 or > IslandCampaign.SlotCount)
            return $"There are only {IslandCampaign.SlotCount} slots.";
        if (kept.FirstOrDefault(h => h.Slot != slot && h.Seed == island.Seed) is { } other)
            return $"Slot {other.Slot} already keeps an island with seed {island.Seed}: each slot has its own island.";
        return null;
    }
}
