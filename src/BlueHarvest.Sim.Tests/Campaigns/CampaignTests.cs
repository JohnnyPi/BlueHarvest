using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Campaigns;
using BlueHarvest.Sim.Events;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Snapshots;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>
/// P3c: campaigns. A survivor's death and an extraction are each recorded once, the 28 and 14 days that follow pass
/// hour by hour and come out the same every time (resumed from a save or not), and three slots keep three islands
/// that don't leak into each other.
/// </summary>
public class CampaignTests
{
    /// <summary>A campaign's first expedition, landing just outside its island's Court.</summary>
    private static (IslandCampaign Campaign, Simulation Sim, Actor Survivor) Begin(string island = "Default", int slot = 1,
        string id = "campaign:1")
    {
        IslandWorld world = TestIslands.World(island);
        Site court = world.Works.Sites.First(s => s.Kind == "court");
        var campaign = IslandCampaign.New(slot, id, world.Params);
        Simulation sim = campaign.StartExpedition(TestContent.NewSession(), world, TestIslands.Chunker(island),
            new IslandStart(court.X, court.Y + court.R + 6, RunSeed: 5), new PlayerController());
        sim.RunUntilInputNeeded();
        return (campaign, sim, sim.Actors[campaign.Current!.ActorId]);
    }

    private const string Fall = "a fall from the sea wall";

    private static void Kill(Simulation sim, Actor you)
    {
        sim.Injure(you, BodySection.Head, Severity.Red, bleeding: true, Fall);
        sim.Injure(you, BodySection.Head, Severity.Red, bleeding: true, Fall);
        Assert.True(you.IsDead);
    }

    private static (IslandCampaign Campaign, Simulation Sim) Reload(byte[] saved, string island = "Default")
    {
        CampaignFile file = CampaignSave.Read(saved);
        WorldSession session = TestContent.NewSession();
        Assert.Null(file.Refusal(session.Content));
        Simulation sim = file.Restore(session, TestIslands.World(island), TestIslands.Chunker(island), new PlayerController());
        return (file.Campaign, sim);
    }

    private static int Ended(Simulation sim) => sim.Session.Events.Recent.Count(e => e.Payload is ExpeditionEndedPayload);

    /// <summary>A save, unpacked, changed and packed again.</summary>
    private static byte[] Repack(byte[] saved, Action<JsonObject> change)
    {
        using var input = new GZipStream(new MemoryStream(saved), CompressionMode.Decompress);
        using var text = new MemoryStream();
        input.CopyTo(text);
        var doc = (JsonObject)JsonNode.Parse(text.ToArray())!;
        change(doc);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(doc.ToJsonString()));
        return output.ToArray();
    }

    [Fact]
    public void ADeath_IsRecordedOnce_AndTheBodyKeepsItsKitThroughASaveAndALoad()
    {
        var (campaign, sim, you) = Begin();
        int carried = you.Inventory.All.Count();
        Assert.True(carried > 0);
        Kill(sim, you);

        ExpeditionRecord record = Assert.Single(campaign.Expeditions);
        Assert.Equal(CampaignPhase.Ended, campaign.Phase);
        Assert.Equal(ExpeditionOutcome.Died, record.Outcome);
        Assert.Equal(you.Health.DeathCause, record.Detail);
        Assert.Equal((long?)you.Health.DiedTick, record.EndTick);
        Assert.Equal((sim.ToIsland(you.Position)), record.EndAt!.Value);
        Assert.Equal("person:1", record.PersonId);
        Assert.Single(campaign.Bodies);
        Assert.Equal(new[] { ChronicleKinds.ExpeditionStarted, ChronicleKinds.SurvivorDied }, campaign.Chronicle.Select(c => c.Kind));
        Assert.Equal($"Survivor died: {you.Health.DeathCause}.", campaign.Chronicle[^1].Text);
        Assert.StartsWith(Fall, record.Detail);
        WalletRequest score = Assert.Single(campaign.ProfileRequests);
        Assert.Equal("campaign:1/exp:1/score", score.RequestId);
        Assert.Equal(score.RequestId, record.ScoreRequestId);
        Assert.Equal(1, Ended(sim));
        Assert.True(sim.Session.IsProcessed("campaign:1/exp:1/ended"));

        // nothing more ends it: time going on, or the next expedition started like the first
        sim.RunUntil(sim.CurrentTick + 600);
        Assert.Equal(1, Ended(sim));
        Assert.Throws<InvalidOperationException>(() => campaign.StartExpedition(TestContent.NewSession(),
            TestIslands.World("Default"), TestIslands.Chunker("Default"), new IslandStart(0, 0, 1), new PlayerController()));

        // saved and put back: the same records, the body where it fell with everything it carried, still ended once
        byte[] saved = CampaignSave.Save(campaign);
        var (back, sim2) = Reload(saved);
        Assert.Equal(CampaignSave.ToJson(campaign), CampaignSave.ToJson(back));
        Actor body = sim2.Actors[record.ActorId];
        Assert.True(body.IsDead);
        Assert.Equal(carried, body.Inventory.All.Count());
        Assert.Equal(record.EndAt!.Value, sim2.ToIsland(body.Position));
        Assert.True(sim2.Session.IsProcessed("campaign:1/exp:1/ended"));
        sim2.RunUntil(sim2.CurrentTick + 600);
        Assert.Single(back.ProfileRequests);
        Assert.Equal(CampaignPhase.Ended, back.Phase);
    }

    [Fact]
    public void AnExtraction_IsRecordedOnce_AndTheSurvivorLeavesWithWhatTheyCarry()
    {
        var (campaign, sim, you) = Begin();
        TilePos from = you.Position;
        int carried = you.Inventory.All.Count();
        Assert.True(sim.Extract(you, "debug extraction"));

        Assert.True(you.HasEscaped);
        Assert.Null(sim.ActorAt(from));
        Assert.Equal(carried, you.Inventory.All.Count()); // what they carry leaves with them
        ExpeditionRecord record = Assert.Single(campaign.Expeditions);
        Assert.Equal(ExpeditionOutcome.Extracted, record.Outcome);
        Assert.Equal("debug extraction", record.Detail);
        Assert.Equal((long?)sim.CurrentTick, record.EndTick);
        Assert.Empty(campaign.Bodies);
        Assert.Equal("Survivor left the island by the debug extraction.", campaign.Chronicle[^1].Text);
        Assert.Equal(1, Ended(sim));

        // gone is gone: they can't leave twice
        Assert.False(sim.Extract(you, "debug extraction"));
        Assert.Equal(1, Ended(sim));
        Assert.Single(campaign.ProfileRequests);
    }

    [Fact]
    public void AfterADeath_28DaysPassHourByHour_TheSameEveryTime()
    {
        (IslandCampaign Campaign, Simulation Sim) Run()
        {
            var (campaign, sim, you) = Begin();
            Kill(sim, you);
            Assert.Throws<ArgumentOutOfRangeException>(() => campaign.BeginIntermission(daysBought: 1)); // a death buys no days
            IntermissionState state = campaign.BeginIntermission();
            Assert.Equal(28, state.Days);
            Assert.True(campaign.AdvanceIntermission());
            return (campaign, sim);
        }

        var (a, simA) = Run();
        IntermissionState done = Assert.Single(a.Intermissions);
        Assert.True(done.Done);
        Assert.Equal(28 * 24, done.HoursPassed);
        Assert.Equal(done.StartTick + 28L * 24 * 60 * WorldClock.TicksPerGameMinute, simA.CurrentTick);
        Assert.Equal(CampaignPhase.AwaitingArrival, a.Phase);
        Assert.Equal("28 days passed.", a.Chronicle[^1].Text);
        Assert.Equal(1, simA.Session.Events.Recent.Count(e => e.Payload is IntermissionEndedPayload));
        Assert.Throws<InvalidOperationException>(() => a.AdvanceIntermission()); // it's over

        var (b, simB) = Run();
        Assert.Equal(simA.ComputeStateHash(), simB.ComputeStateHash());
        Assert.Equal(CampaignSave.ToJson(a), CampaignSave.ToJson(b));
    }

    [Fact]
    public void AfterAnExtraction_14DaysPass_AndAnIntermissionSavedPartWayComesOutTheSame()
    {
        var (whole, simWhole, you) = Begin();
        Assert.True(simWhole.Extract(you, "debug extraction"));
        IntermissionState state = whole.BeginIntermission();
        Assert.Equal(14, state.Days);
        Assert.True(whole.AdvanceIntermission());
        Assert.Equal(14 * 24, state.HoursPassed);

        var (part, simPart, you2) = Begin();
        Assert.True(simPart.Extract(you2, "debug extraction"));
        part.BeginIntermission();
        Assert.False(part.AdvanceIntermission(maxHours: 100));
        Assert.Equal(100, part.Intermission!.HoursPassed);
        var (resumed, simResumed) = Reload(CampaignSave.Save(part));
        Assert.Equal(CampaignPhase.Intermission, resumed.Phase);
        Assert.True(resumed.AdvanceIntermission());

        Assert.Equal(simWhole.CurrentTick, simResumed.CurrentTick);
        Assert.Equal(simWhole.ComputeStateHash(), simResumed.ComputeStateHash());
        Assert.Equal(CampaignSave.ToJson(whole), CampaignSave.ToJson(resumed));
    }

    [Fact]
    public void IntermissionDays_AreBoughtOnlyWithAnExtractionsCurrency_DownToThreeDays()
    {
        Assert.Equal(28, IntermissionRules.BaseDays(ExpeditionOutcome.Died));
        Assert.Equal(14, IntermissionRules.BaseDays(ExpeditionOutcome.Extracted));
        Assert.Throws<ArgumentOutOfRangeException>(() => IntermissionRules.BaseDays(ExpeditionOutcome.Active));
        Assert.Equal(0, IntermissionRules.MostDaysBought(ExpeditionOutcome.Died, 50));
        Assert.Equal(0, IntermissionRules.MostDaysBought(ExpeditionOutcome.Extracted, 0));
        Assert.Equal(5, IntermissionRules.MostDaysBought(ExpeditionOutcome.Extracted, 5));
        Assert.Equal(11, IntermissionRules.MostDaysBought(ExpeditionOutcome.Extracted, 50)); // never below 3 days
    }

    [Fact]
    public void ThreeSlots_KeepThreeIslands_ThatDontLeakIntoEachOther()
    {
        var profile = new PlayerProfile();
        string[] islands = ["Default", "Seed12", "CoolWet"];
        var saves = new byte[3][];
        for (int i = 0; i < 3; i++)
        {
            var (campaign, sim, you) = Begin(islands[i], slot: i + 1, id: profile.NewCampaignId());
            if (i == 0)
                Kill(sim, you);
            else if (i == 1)
                Assert.True(sim.Extract(you, "debug extraction"));
            campaign.SettleWith(profile);
            saves[i] = CampaignSave.Save(campaign);
        }

        // the profile was paid once by each campaign that ended, and by nothing else
        Assert.Equal(new[] { "campaign:1/exp:1/score", "campaign:2/exp:1/score" }, profile.Ledger.Select(e => e.RequestId));
        Assert.Equal(new[] { "campaign:1", "campaign:2" }, profile.Ledger.Select(e => e.CampaignId ?? ""));

        var headers = new List<CampaignHeader>();
        for (int i = 0; i < 3; i++)
        {
            var (back, sim) = Reload(saves[i], islands[i]);
            CampaignHeader header = CampaignSave.Read(saves[i]).Header;
            headers.Add(header);
            Assert.Equal(i + 1, header.Slot);
            Assert.Equal($"campaign:{i + 1}", back.Id);
            Assert.Equal(TestIslands.World(islands[i]).Params, back.Island);
            Assert.Equal(back.Island, sim.Session.Island!.World.Params);
            Assert.Equal(i switch { 0 => CampaignPhase.Ended, 1 => CampaignPhase.Ended, _ => CampaignPhase.Exploring }, back.Phase);
            // each campaign's survivor is its own, and only it is in its world
            Assert.Single(sim.Actors);
            Assert.Equal(i == 2, !sim.Actors[0].IsGone);
            back.SettleWith(profile); // settling again after the load pays nothing twice
        }
        Assert.Equal(2, profile.Ledger.Count);
        Assert.Equal(3, headers.Select(h => h.Seed).Distinct().Count());

        // a campaign can't be put back on another slot's island, and a slot can't take another slot's seed
        Assert.Throws<SnapshotRefusedException>(() => Reload(saves[1], "Default"));
        Assert.NotNull(CampaignSlots.WhyNot(1, TestIslands.World("Seed12").Params, headers));
        Assert.Null(CampaignSlots.WhyNot(2, TestIslands.World("Seed12").Params, headers)); // its own slot, being replaced
        Assert.Null(CampaignSlots.WhyNot(3, TestIslands.World("SmallNoCliffs").Params, headers));
        Assert.NotNull(CampaignSlots.WhyNot(4, TestIslands.World("SmallNoCliffs").Params, headers));
        Assert.Equal("user://campaigns/2/campaign.json.gz", CampaignSlots.PathOf(2));
    }

    [Fact]
    public void AProfileSavedBeforeTheRunEnded_IsPaidOnceWhenTheCampaignIsLoaded()
    {
        var profile = new PlayerProfile();
        profile.Apply(new WalletRequest("earlier", WalletKind.Earn, 7, "test"));
        byte[] profileBefore = profile.Save();
        var (campaign, sim, you) = Begin(id: profile.NewCampaignId());
        Kill(sim, you);
        byte[] campaignAfter = CampaignSave.Save(campaign);
        // the game stops here, before the profile is paid and saved: both are loaded again
        PlayerProfile loaded = PlayerProfile.Read(profileBefore);
        var (back, _) = Reload(campaignAfter);
        Assert.Equal(WalletOutcome.Applied, Assert.Single(back.SettleWith(loaded)).Outcome);
        Assert.Equal(WalletOutcome.AlreadyApplied, Assert.Single(back.SettleWith(loaded)).Outcome);
        PlayerProfile again = PlayerProfile.Read(loaded.Save());
        Assert.Equal(new[] { "earlier", "campaign:1/exp:1/score" }, again.Ledger.Select(e => e.RequestId));
        Assert.Equal(7, again.Balance);
    }

    [Fact]
    public void ACampaignFileThatDoesntHoldTogether_IsRefused()
    {
        // a new campaign saves without a world, and reads back as it was
        var fresh = IslandCampaign.New(2, "campaign:9", TestIslands.World("Seed12").Params);
        byte[] saved = CampaignSave.Save(fresh);
        CampaignFile file = CampaignSave.Read(saved);
        Assert.Null(file.World);
        Assert.Equal(new CampaignHeader(2, "campaign:9", 12, 75, CampaignPhase.New, 0, 0), file.Header);
        Assert.Throws<InvalidOperationException>(() => file.Restore(TestContent.NewSession(), TestIslands.World("Seed12"),
            TestIslands.Chunker("Seed12"), new PlayerController()));

        Assert.Throws<SnapshotRefusedException>(() => CampaignSave.Read(Repack(saved, d => d["format"] = "blueharvest.campaign/0")));
        Assert.Throws<SnapshotRefusedException>(() => CampaignSave.Read(Repack(saved, d => d["phase"] = "Exploring")));
        Assert.Throws<SnapshotRefusedException>(() => CampaignSave.Read(Repack(saved, d => d["slot"] = 4)));
        Assert.Throws<SnapshotRefusedException>(() => CampaignSave.Read(Encoding.UTF8.GetBytes("not a save")));

        // a saved place that isn't on the surface (interiors come in P10)
        var (campaign, sim, you) = Begin();
        Kill(sim, you);
        byte[] ended = CampaignSave.Save(campaign);
        Assert.Throws<SnapshotRefusedException>(() => CampaignSave.Read(Repack(ended, d =>
            d["expeditions"]![0]!["space"] = "space:adm-b1")));
        Assert.Throws<SnapshotRefusedException>(() => Reload(Repack(ended, d =>
            d["world"]!["state"]!["actors"]![0]!["space"] = "space:adm-b1")));
    }
}
