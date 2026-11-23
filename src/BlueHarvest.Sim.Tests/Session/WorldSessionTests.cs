using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.ContentIntegration;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Demo;
using BlueHarvest.Sim.Events;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Tests.ContentLibrary;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>P3a: the session owns the content, the items and the event ledger; events publish after their request commits.</summary>
public class WorldSessionTests
{
    private static List<SimEvent> Record(WorldSession session, ObserverStage stage = ObserverStage.Projections)
    {
        var seen = new List<SimEvent>();
        session.Observe(stage, seen.Add);
        return seen;
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    [Fact]
    public void EventIds_RiseByOne_AndARequestsEvents_ShareOneRevision()
    {
        var session = TestContent.NewSession();
        var seen = Record(session);
        var sim = DemoWorld.Create(session, 12345);
        sim.RunUntil(Ticks.FromSeconds(60));

        Assert.NotEmpty(seen);
        Assert.Contains(seen, e => e.Type == ActorActedPayload.TypeName);
        for (int i = 0; i < seen.Count; i++)
        {
            Assert.Equal((ulong)(i + 1), seen[i].EventId);
            if (i > 0)
            {
                ulong step = seen[i].WorldRevision - seen[i - 1].WorldRevision;
                Assert.True(step is 0 or 1, $"{seen[i]} after {seen[i - 1]}");
                Assert.True(seen[i].Tick >= seen[i - 1].Tick, $"{seen[i]} after {seen[i - 1]}");
            }
        }
        Assert.Equal(1UL, seen[0].WorldRevision);
        Assert.Equal(seen[^1].WorldRevision, session.WorldRevision);
        Assert.Equal((ulong)seen.Count, session.LastEventId);
        Assert.Equal(seen.Count, session.Events.Count);
        Assert.Same(seen[^1], session.Events.Last);
        Assert.Equal(seen.TakeLast(session.Events.Recent.Count), session.Events.Recent);
        // every turn taken is in the ledger, with who took it
        Assert.All(seen.Where(e => e.Type == ActorActedPayload.TypeName), e => Assert.StartsWith("actor:", e.Actor));
    }

    [Fact]
    public void ABitesInjuries_AreSeenOnceTheWholeBiteHasHappened()
    {
        var session = TestContent.NewSession();
        var sim = new Simulation(session, new WorldMap(30, 30), seed: 1);
        var drive = new PlayerController();
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 10), Species.Rex, drive, Direction.South);
        var pete = sim.AddActor("Pete", 'h', new TilePos(10, 14), Species.Human, new HoldStillController());
        sim.RunUntilInputNeeded();
        var majorWoundsWhenTold = new List<int>();
        sim.ActorInjured += (who, _, _, _) =>
            majorWoundsWhenTold.Add(Enum.GetValues<BodySection>().Count(s => who.Health[s].Severity == Severity.Red));
        var seen = Record(session);

        Do(sim, drive, new BiteAction());

        // raised inline, the first wound's listener would have seen one major wound; after the commit, both
        Assert.Equal(new[] { 2, 2 }, majorWoundsWhenTold);
        var bitten = Assert.Single(seen, e => e.Type == ActorBittenPayload.TypeName);
        var bite = seen.Where(e => e.WorldRevision == bitten.WorldRevision).ToList(); // the Rex's turn, as one request
        Assert.Equal(
            new[] { ActorActedPayload.TypeName, ActorBittenPayload.TypeName, ActorInjuredPayload.TypeName, ActorInjuredPayload.TypeName, BitPayload.TypeName },
            bite.Select(e => e.Type).Where(t => t is ActorActedPayload.TypeName or ActorBittenPayload.TypeName
                or ActorInjuredPayload.TypeName or BitPayload.TypeName));
        Assert.Equal($"actor:{rex.Id}", Assert.Single(bite, e => e.Type == ActorActedPayload.TypeName).Actor);
        Assert.Equal($"actor:{rex.Id}", bitten.Actor);
        Assert.Equal(new[] { $"actor:{pete.Id}" }, bitten.Subjects);
    }

    [Fact]
    public void Observers_SeeARequestStageByStage_AndWhatTheyAskForComesAfter()
    {
        var session = TestContent.NewSession();
        var sim = new Simulation(session, new WorldMap(20, 20), seed: 7);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive, Direction.East);
        sim.RunUntilInputNeeded();
        var knife = sim.CreateItem(CoreItems.Knife);
        sim.PlaceItem(knife, new TilePos(5, 5));

        string[] types = [ActorActedPayload.TypeName, ItemPickedUpPayload.TypeName, WeatherChangedPayload.TypeName];
        var log = new List<string>();
        var events = new List<SimEvent>();
        bool carriedWhenSeen = false;
        foreach (ObserverStage stage in Enum.GetValues<ObserverStage>())
        {
            session.Observe(stage, e =>
            {
                if (types.Contains(e.Type))
                    log.Add($"{stage}:{e.Type}");
            });
        }
        session.Observe(ObserverStage.Missions, e =>
        {
            events.Add(e);
            if (e.Payload is ItemPickedUpPayload picked)
            {
                carriedWhenSeen = you.Inventory.All.Contains(picked.Item);
                session.Submit(() => sim.SetWeather(WeatherSettings.TestbedStorm)); // a change: a request of its own
            }
        });
        sim.ItemPickedUp += (_, _) => log.Add("event:" + ItemPickedUpPayload.TypeName);
        sim.WeatherChanged += _ => log.Add("event:" + WeatherChangedPayload.TypeName);

        Do(sim, drive, new PickUpAction(knife.Id));

        Assert.True(carriedWhenSeen);
        Assert.Equal(new[]
        {
            "Projections:actor.acted", "Projections:item.picked-up",
            "Missions:actor.acted", "Missions:item.picked-up",
            "Scripts:actor.acted", "Scripts:item.picked-up",
            "Presentation:actor.acted", "event:item.picked-up", "Presentation:item.picked-up",
            "Projections:weather.changed", "Missions:weather.changed", "Scripts:weather.changed",
            "event:weather.changed", "Presentation:weather.changed",
        }, log);
        SimEvent pickedUp = Assert.Single(events, e => e.Type == ItemPickedUpPayload.TypeName);
        SimEvent weather = Assert.Single(events, e => e.Type == WeatherChangedPayload.TypeName);
        Assert.Equal(pickedUp.WorldRevision + 1, weather.WorldRevision);
        Assert.Equal($"event:{pickedUp.EventId}", weather.Cause);
        Assert.Null(pickedUp.Cause);
        Assert.Equal($"actor:{you.Id}", pickedUp.Actor);
        Assert.Equal(new[] { $"item:{knife.Id}" }, pickedUp.Subjects);
    }

    [Fact]
    public void ARequestWithAnId_RunsOnce_AndOutsideARequest_EachEventCommitsAtOnce()
    {
        var session = TestContent.NewSession();
        var sim = new Simulation(session, new WorldMap(4, 4), seed: 1);
        int told = 0;
        sim.WeatherChanged += _ => told++;
        var seen = Record(session);

        sim.SetWeather(WeatherSettings.Clear);
        Assert.Equal(1, told);
        Assert.Equal(1UL, session.WorldRevision);
        Assert.Null(seen[^1].Cause);

        Assert.True(session.CommitOnce("test:storm", () => sim.SetWeather(WeatherSettings.TestbedStorm)));
        Assert.False(session.CommitOnce("test:storm", () => sim.SetWeather(WeatherSettings.Clear)));
        Assert.Equal(2, told);
        Assert.Equal(WeatherSettings.TestbedStorm.WindSpeed, sim.Weather.Settings.WindSpeed);
        Assert.True(session.IsProcessed("test:storm"));
        Assert.False(session.IsProcessed("test:calm"));
        Assert.Equal("test:storm", seen[^1].Cause);
        Assert.Equal(2UL, seen[^1].WorldRevision);

        // one request, two changes: one revision
        Assert.True(session.CommitOnce("test:calm", () =>
        {
            sim.SetWeather(WeatherSettings.Clear);
            sim.SetWeather(WeatherSettings.TestbedStorm);
        }));
        Assert.Equal(4, told);
        Assert.Equal(new[] { 3UL, 3UL }, seen.TakeLast(2).Select(e => e.WorldRevision));
        Assert.All(seen.TakeLast(2), e => Assert.Equal("test:calm", e.Cause));
        Assert.Equal(new[] { "test:storm", "test:calm" }, session.ProcessedRequests);

        // an observer that stops sees nothing more
        int watched = 0;
        var watch = session.Observe(ObserverStage.Scripts, _ => watched++);
        sim.SetWeather(WeatherSettings.Clear);
        watch.Dispose();
        sim.SetWeather(WeatherSettings.TestbedStorm);
        Assert.Equal(1, watched);
        Assert.Equal(6, told);
    }

    [Fact]
    public void TwoSessions_WithDifferentPacks_RunSideBySide()
    {
        ContentLoadResult tinyLoad = TestPacks.Load(TestPacks.Core(("items/pebble.yaml", TestPacks.Item("pebble"))),
            SimContent.Options(ContentMode.Authoring));
        TestPacks.AssertLoads(tinyLoad);
        var tiny = new WorldSession(tinyLoad.Registry!);
        var repo = TestContent.NewSession();
        var small = new Simulation(tiny, new WorldMap(4, 4), seed: 1);
        var full = new Simulation(repo, new WorldMap(4, 4), seed: 1);
        var pebble = new ContentId("core:pebble");

        Assert.NotSame(tiny.Catalog, repo.Catalog);
        Assert.NotEqual(tiny.Fingerprint, repo.Fingerprint);
        Assert.Equal(new[] { pebble }, tiny.Catalog.All.Select(d => d.Id));
        Assert.Same(small, tiny.Surface);
        Assert.Same(full, repo.Surface);
        Assert.Equal("Item pebble", small.CreateItem(pebble).Def.Name);
        Assert.Equal(CoreItems.Flashlight, full.CreateItem(CoreItems.Flashlight).Kind);
        Assert.Throws<KeyNotFoundException>(() => small.CreateItem(CoreItems.Flashlight));
        Assert.Throws<KeyNotFoundException>(() => full.CreateItem(pebble));
        // a ledger belongs to its session's items, and a session to one simulation
        Assert.Throws<ArgumentException>(() => full.UseItems(new ItemLedger(tiny.Catalog)));
        Assert.Throws<InvalidOperationException>(() => new Simulation(tiny, new WorldMap(4, 4), seed: 2));
    }
}
