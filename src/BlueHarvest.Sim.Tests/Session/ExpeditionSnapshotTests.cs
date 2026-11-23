using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.ContentIntegration;
using BlueHarvest.Sim.Events;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Snapshots;
using BlueHarvest.Sim.Tests.ContentLibrary;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>P3b: an island expedition saved as a snapshot and put back, and played on exactly as if it had never stopped.</summary>
public class ExpeditionSnapshotTests
{
    private static readonly Lazy<FeatureIndex> Features = new(() => FeatureIndex.Build(TestIslands.World("Default")));

    /// <summary>An expedition on Default, landing just outside the Court's south gate.</summary>
    private static (Simulation Sim, PlayerController Drive, Actor Survivor) Land()
    {
        IslandWorld world = TestIslands.World("Default");
        Site court = world.Works.Sites.First(s => s.Kind == "court");
        var drive = new PlayerController();
        Simulation sim = IslandExpedition.Start(TestContent.NewSession(), world, TestIslands.Chunker("Default"),
            new IslandStart(court.X, court.Y + court.R + 6, RunSeed: 5), drive, Features.Value);
        sim.RunUntilInputNeeded();
        return (sim, drive, sim.Actors.Single());
    }

    private static Simulation PutBack(byte[] saved, PlayerController drive)
    {
        ExpeditionSave save = ExpeditionSnapshot.Read(saved);
        Assert.Equal(TestIslands.World("Default").Params, save.Island);
        return ExpeditionSnapshot.Restore(TestContent.NewSession(), save, TestIslands.World("Default"),
            TestIslands.Chunker("Default"), drive, Features.Value);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    /// <summary>A save, unpacked, changed and packed again: a save from somewhere else.</summary>
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

    /// <summary>
    /// Plays the survivor by a script until a tick (or, once they're gone, lets the world run on to it). The script
    /// depends only on the turn number and where the survivor is, so two games in the same state play the same.
    /// </summary>
    private static void Play(Simulation sim, PlayerController drive, Actor you, long until, Func<int, Actor, ActorAction> script)
    {
        int turn = 0;
        while (sim.CurrentTick < until)
        {
            if (you.IsGone)
            {
                sim.RunUntil(until);
                return;
            }
            drive.Pending = script(turn++, you);
            if (sim.RunUntilInputNeeded() is StepResult.PlayerDied or StepResult.PlayerEscaped)
            {
                sim.RunUntil(until);
                return;
            }
        }
    }

    /// <summary>
    /// The landing with company: a person and a Rex with minds of their own, a raptor walking at random by the west
    /// edge (left behind when the window moves east), and a body. Everyone has needs.
    /// </summary>
    private static (Simulation Sim, PlayerController Drive, Actor Survivor, Actor Npc) Gathered()
    {
        var (sim, drive, you) = Land();
        TilePos at = you.Position;
        Actor npc = sim.AddActor("Marta", 'h', new TilePos(at.X + 2, at.Y), Species.Human,
            new HumanBrain(Traits.Steady, AiProfile.Human, sim.Rng.Fork("ai:marta")), loadout: Loadout.Ranger);
        Actor rex = sim.AddActor("Rex", 'T', new TilePos(at.X, at.Y + 25), Species.Rex,
            new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex")), Direction.North);
        Actor raptor = sim.AddActor("Raptor", 'R', new TilePos(3, at.Y), Species.Raptor, new RandomWalker(sim.Rng.Fork("ai:raptor")));
        Actor body = sim.AddActor("Lost", 'h', new TilePos(at.X - 3, at.Y + 1), Species.Human, new HoldStillController());
        sim.Injure(body, BodySection.Head, Severity.Red, bleeding: true, "a fall");
        sim.Injure(body, BodySection.Head, Severity.Red, bleeding: true, "a fall");
        Assert.True(body.IsDead);
        foreach (Actor a in new[] { you, npc, rex, raptor })
            sim.UseVariedNeeds(a);
        sim.RunUntilInputNeeded();
        return (sim, drive, you, npc);
    }

    /// <summary>Before the save: a light, a thrown flare, a fire, a shout, an order; then the window moves east.</summary>
    private static ActorAction Before(int turn, Actor you, Actor npc) => turn switch
    {
        0 => new ToggleLightAction(),
        1 => new ThrowAction(CoreItems.Flare, new TilePos(you.Position.X + 5, you.Position.Y + 3)),
        2 => new OrderAction(npc.Id, NpcOrder.Follow),
        3 => new IgniteAction(Direction.East),
        4 => new ScreamAction(),
        _ => (turn % 3) switch
        {
            0 => new MoveAction(Direction.South),
            1 => new MoveAction(Direction.East, Sprint: turn % 2 == 0),
            _ => new WaitAction(150),
        },
    };

    /// <summary>After the save: walking about, waiting, a light switched off and on.</summary>
    private static ActorAction After(int turn, Actor you) => (turn % 7) switch
    {
        0 => new MoveAction(Direction.East),
        1 => new MoveAction(Direction.South, Sprint: true),
        2 => new WaitAction(300),
        3 => new MoveAction(Direction.West),
        4 => new ToggleLightAction(),
        5 => new MoveAction(Direction.North),
        _ => new WaitAction(100),
    };

    [Fact]
    public void SavedAndPutBack_TenGameMinutesPlayExactlyAsTenWithoutStopping()
    {
        var (sim, drive, you, npc) = Gathered();
        IslandWindow window = sim.Session.Island!;
        Play(sim, drive, you, sim.CurrentTick + 1500, (t, a) => Before(t, a, npc));
        // the window moves east: the raptor by the west edge is left behind
        (int dx, int dy) = window.Recenter(new TilePos(you.Position.X + IslandChunker.ChunkSize, you.Position.Y));
        sim.ShiftWindow(dx, dy);
        Play(sim, drive, you, sim.CurrentTick + 500, (t, a) => Before(t + 5, a, npc));
        Assert.Contains(sim.Actors, a => a.LeftBehind);
        Assert.Contains(sim.Actors, a => a.IsDead);
        Assert.Contains(sim.Actors, a => a.Controller is CreatureBrain);

        byte[] saved = ExpeditionSnapshot.Save(sim.Session);
        string atSave = ExpeditionSnapshot.ToJson(sim.Session);
        long end = sim.CurrentTick + 10 * WorldClock.TicksPerGameMinute;
        ulong revisionAtSave = sim.Session.WorldRevision;
        Play(sim, drive, you, end, After);

        var replay = new PlayerController();
        Simulation again = PutBack(saved, replay);
        Assert.Equal(atSave, ExpeditionSnapshot.ToJson(again.Session));
        Actor back = again.Actors.Single(a => a.Controller == replay);
        Play(again, replay, back, end, After);

        Assert.Equal(sim.CurrentTick, again.CurrentTick);
        Assert.Equal(sim.ComputeStateHash(), again.ComputeStateHash());
        Assert.Equal(ExpeditionSnapshot.ToJson(sim.Session), ExpeditionSnapshot.ToJson(again.Session));
        Assert.Equal(sim.Session.LastEventId, again.Session.LastEventId);
        // and the ten minutes weren't idle
        Assert.True(sim.Session.WorldRevision > revisionAtSave + 50, "the world barely moved");
        Assert.NotEqual(atSave, ExpeditionSnapshot.ToJson(sim.Session));
    }

    [Fact]
    public void AnExpedition_PutBack_SavesExactlyAsItDid_AndSavingChangesNothing()
    {
        var (sim, drive, survivor) = Land();
        Do(sim, drive, new MoveAction(Direction.South));
        Do(sim, drive, new MoveAction(Direction.South));
        ulong before = sim.ComputeStateHash();
        string json = ExpeditionSnapshot.ToJson(sim.Session);
        byte[] saved = ExpeditionSnapshot.Save(sim.Session);
        Assert.Equal(before, sim.ComputeStateHash());
        Assert.Equal(json, ExpeditionSnapshot.ToJson(sim.Session));

        var again = PutBack(saved, new PlayerController());
        Assert.Equal(json, ExpeditionSnapshot.ToJson(again.Session));
        Assert.Equal(saved, ExpeditionSnapshot.Save(again.Session));
        Actor back = again.Actors.Single();
        Assert.Equal(again.ToIsland(back.Position), sim.ToIsland(survivor.Position));
        Assert.Equal(survivor.Skills, back.Skills);
        Assert.Equal(sim.CurrentTick, again.CurrentTick);
        Assert.Equal(sim.Clock, again.Clock);
        Assert.Equal(sim.Weather.Settings, again.Weather.Settings);
        Assert.Equal(sim.Items.Made, again.Items.Made);
        Assert.Equal(sim.Knowledge(survivor)!.SeenCount, again.Knowledge(back)!.SeenCount);
        Assert.Equal(sim.Session.WorldRevision, again.Session.WorldRevision);
        Assert.Equal(sim.Session.LastEventId, again.Session.LastEventId);
        Assert.Equal(sim.Session.Launch, again.Session.Launch);
    }

    [Fact]
    public void AnItemPutDown_AndTilesChangedOnAndOffTheWindow_ComeBackAsTheyWere_WithNothingTwice()
    {
        var (sim, drive, survivor) = Land();
        IslandWindow window = sim.Session.Island!;
        Item dropped = survivor.Inventory.Carried.First(c => c.Item.Kind != CoreItems.Daypack).Item;
        var droppedAt = sim.ToIsland(survivor.Position);
        Do(sim, drive, new DropAction(dropped.Id));
        Do(sim, drive, new MoveAction(Direction.South));
        Assert.NotNull(sim.Items.Find(dropped.Id));

        // a tile in the west chunk changed, then scrolled off the window; a door locked on a tile that stays
        TilePos west = Enumerable.Range(0, 64).SelectMany(y => Enumerable.Range(0, 64).Select(x => new TilePos(x, y + 64)))
            .First(p => window.Map.Cover[p] is CoverType.Grass or CoverType.Scrub or CoverType.TallGrass or CoverType.Ferns
                or CoverType.SparseJungle or CoverType.DenseJungle or CoverType.Sand or CoverType.BareRock or CoverType.Rock);
        var westOnIsland = sim.ToIsland(west);
        CoverType westWas = window.Map.Cover[west];
        window.Map.Cover[west] = CoverType.Rubble;
        var locked = new TilePos(window.Size - 10, window.Size / 2);
        var lockedOnIsland = sim.ToIsland(locked);
        window.Map.Structures.SetDoor(locked, DoorState.Locked);
        (int dx, int dy) = window.Recenter(new TilePos(survivor.Position.X + IslandChunker.ChunkSize, survivor.Position.Y));
        sim.ShiftWindow(dx, dy);
        Assert.Equal((IslandChunker.ChunkSize, 0), (dx, dy));
        Assert.False(survivor.IsGone);
        Assert.True(window.Changes.ChunkCount > 0); // the west chunk's change is off the window now

        byte[] saved = ExpeditionSnapshot.Save(sim.Session);
        var again = PutBack(saved, new PlayerController());
        IslandWindow back = again.Session.Island!;
        Actor carrier = again.Actors.Single();

        // the item: lying where it was put down, not carried, the same item
        WorldItem lying = Assert.Single(again.Items.All, w => w.Item.Id == dropped.Id);
        Assert.Equal(droppedAt, (lying.X, lying.Y));
        Assert.Equal(dropped.Kind, lying.Item.Kind);
        Assert.DoesNotContain(carrier.Inventory.All, i => i.Id == dropped.Id);
        // every item once, lying or carried, and the same ones
        var ids = again.Items.All.SelectMany(w => w.Item.SelfAndNested()).Concat(carrier.Inventory.All).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var was = sim.Items.All.SelectMany(w => w.Item.SelfAndNested()).Concat(survivor.Inventory.All).Select(i => i.Id).Order();
        Assert.Equal(was, ids.Order());

        // the locked tile, on the window; the changed tile, off it until the window goes back west
        Assert.Equal((window.OriginX, window.OriginY), (back.OriginX, back.OriginY));
        Assert.Equal(DoorState.Locked, back.Map.Structures.DoorAt(again.FromIsland(lockedOnIsland.X, lockedOnIsland.Y)));
        Assert.Equal(window.Changes.Chunks, back.Changes.Chunks);
        (dx, dy) = back.Recenter(new TilePos(carrier.Position.X - IslandChunker.ChunkSize, carrier.Position.Y));
        again.ShiftWindow(dx, dy);
        Assert.Equal(CoverType.Rubble, back.Map.Cover[again.FromIsland(westOnIsland.X, westOnIsland.Y)]);
        Assert.NotEqual(CoverType.Rubble, westWas);
    }

    [Fact]
    public void APutBackGame_SaysNothingAboutBeingPutBack_AndCountsOnFromTheSave()
    {
        var (sim, drive, _) = Land();
        Do(sim, drive, new MoveAction(Direction.South));
        Assert.True(sim.Session.CommitOnce("test:landed", () => sim.SetWeather(WeatherSettings.TestbedStorm)));
        byte[] saved = ExpeditionSnapshot.Save(sim.Session);

        var replay = new PlayerController();
        var again = PutBack(saved, replay);
        Assert.Equal(0, again.Session.Events.Count);
        Assert.Equal(sim.Session.LastEventId, again.Session.LastEventId);
        Assert.True(again.Session.IsProcessed("test:landed"));
        Assert.False(again.Session.CommitOnce("test:landed", () => again.SetWeather(WeatherSettings.Clear)));
        Assert.Equal(WeatherSettings.TestbedStorm.Clamped(), again.Weather.Settings);
        Assert.False(again.FollowingWeatherSource); // set by hand, it stays so

        again.RunUntilInputNeeded();
        Do(again, replay, new MoveAction(Direction.South));
        SimEvent first = again.Session.Events.Recent.First();
        Assert.Equal(sim.Session.LastEventId + 1, first.EventId);
        Assert.Equal(sim.Session.WorldRevision + 1, first.WorldRevision);
    }

    [Fact]
    public void ASaveFromOtherPacks_AnotherGenerator_OrAnotherIsland_IsRefused()
    {
        var (sim, _, _) = Land();
        byte[] saved = ExpeditionSnapshot.Save(sim.Session);
        Assert.Null(ExpeditionSnapshot.Refusal(ExpeditionSnapshot.Read(saved), TestContent.Registry));

        // another generator
        ExpeditionSave older = ExpeditionSnapshot.Read(Repack(saved, d => d["generator"] = IslandGen.Version - 1));
        Assert.Contains("generator", ExpeditionSnapshot.Refusal(older, TestContent.Registry));
        Assert.Throws<SnapshotRefusedException>(() => ExpeditionSnapshot.Restore(TestContent.NewSession(), older,
            TestIslands.World("Default"), TestIslands.Chunker("Default"), new PlayerController()));

        // other packs: a pack at another version, and a pack that isn't here
        ExpeditionSave other = ExpeditionSnapshot.Read(Repack(saved, d =>
        {
            d["fingerprint"] = "sha256:0000";
            d["packs"] = new JsonArray(new JsonObject { ["id"] = "core", ["version"] = "9.9.9" },
                new JsonObject { ["id"] = "raptor-pack", ["version"] = "1.0.0" });
        }));
        string why = ExpeditionSnapshot.Refusal(other, TestContent.Registry)!;
        Assert.Contains("core is", why);
        Assert.Contains("raptor-pack 1.0.0 isn't here", why);

        // a session on other content refuses the real save
        ContentLoadResult tiny = TestPacks.Load(TestPacks.Core(("items/pebble.yaml", TestPacks.Item("pebble"))),
            SimContent.Options(ContentMode.Authoring));
        TestPacks.AssertLoads(tiny);
        Assert.NotNull(ExpeditionSnapshot.Refusal(ExpeditionSnapshot.Read(saved), tiny.Registry!));

        // another island
        Assert.Throws<SnapshotRefusedException>(() => ExpeditionSnapshot.Restore(TestContent.NewSession(),
            ExpeditionSnapshot.Read(saved), TestIslands.World("Seed12"), TestIslands.Chunker("Seed12"), new PlayerController()));

        // not a save at all, and a damaged one
        Assert.Throws<SnapshotRefusedException>(() => ExpeditionSnapshot.Read(Encoding.UTF8.GetBytes("not a save")));
        ExpeditionSave damaged = ExpeditionSnapshot.Read(Repack(saved, d => ((JsonObject)d["items"]!)["lying"] = "nothing"));
        Assert.Throws<SnapshotRefusedException>(() => ExpeditionSnapshot.Restore(TestContent.NewSession(), damaged,
            TestIslands.World("Default"), TestIslands.Chunker("Default"), new PlayerController()));
    }

    [Fact]
    public void OnlyIslandExpeditions_AreSaved()
    {
        var session = TestContent.NewSession();
        Demo.DemoWorld.Create(session, 1);
        Assert.Throws<InvalidOperationException>(() => ExpeditionSnapshot.Save(session));
        Assert.Throws<InvalidOperationException>(() => ExpeditionSnapshot.Save(TestContent.NewSession()));
    }
}
