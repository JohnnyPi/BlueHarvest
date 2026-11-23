using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using BlueHarvest.Sim.World.Generation;
using Xunit;
using BlueHarvest.Content;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests;

/// <summary>Step 20b: the catalog, combining, using things up, keys and crowbars, the flashlight's batteries, and loot.</summary>
public class ItemUseTests
{
    private static (Simulation Sim, PlayerController Drive, Actor You) Person(Loadout? loadout = null)
    {
        var sim = new Simulation(TestContent.NewSession(), new WorldMap(20, 20), seed: 11);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive, Direction.East, loadout: loadout);
        sim.RunUntilInputNeeded();
        return (sim, drive, you);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    private static Item Give(Simulation sim, Actor you, ContentId kind, int? amount = null, bool fitted = true)
    {
        var item = sim.CreateItem(kind, amount, fitted: fitted);
        Assert.True(sim.GiveItem(you, item), $"no room for {item}");
        return item;
    }

    private static List<string> Refusals(Simulation sim)
    {
        var reasons = new List<string>();
        sim.MoveBlocked += (_, why) => reasons.Add(why);
        return reasons;
    }

    // ---------- the catalog ----------

    [Fact]
    public void TheCatalog_HasEveryKindOfThing()
    {
        Assert.True(TestContent.Catalog.All.Count >= 90);
        foreach (var category in new[] { ItemCategory.Edible, ItemCategory.Potable, ItemCategory.Medical, ItemCategory.Light, ItemCategory.Power,
                     ItemCategory.Tool, ItemCategory.Weapon, ItemCategory.Ammo, ItemCategory.Wearable, ItemCategory.Document, ItemCategory.Key,
                     ItemCategory.Part, ItemCategory.Fuel, ItemCategory.Container, ItemCategory.Fixture })
            Assert.Contains(TestContent.Catalog.All, d => d.Is(category));
        // what fills a thing stacks, and what a thing comes with fits it
        foreach (var def in TestContent.Catalog.All)
        {
            if (def.Fills is { } fill)
                Assert.True(TestContent.Catalog.Of(fill).Stacks, $"{def.Id} fills with {fill}, which should stack");
            if (def.Comes is { } comes)
                Assert.All(comes, c => Assert.Contains(c.Kind, def.Holds!.Only!));
        }
    }

    [Fact]
    public void ANewFlashlight_ComesWithItsBatteries_UnlessMadeEmpty()
    {
        var (sim, _, _) = Person();
        var fitted = sim.CreateItem(CoreItems.Flashlight);
        Assert.Equal(new[] { CoreItems.BatteryD, CoreItems.BatteryD }, fitted.Contents.Select(i => i.Kind));
        Assert.True(fitted.Charged);
        Assert.Equal("a flashlight (2 D batteries, 100%)", fitted.Describe());

        var empty = sim.PlaceItems(TestContent.Catalog.ParseSpec("core:flashlight empty"), new TilePos(1, 1))[0];
        Assert.Empty(empty.Contents);
        Assert.False(empty.Charged);
    }

    // ---------- combining ----------

    [Fact]
    public void Rounds_LoadIntoAMagazine_TheMagazineIntoAPistol_AndBackOut()
    {
        var (sim, drive, you) = Person();
        var rounds = Give(sim, you, CoreItems.Rounds9mm, 20);
        var magazine = Give(sim, you, CoreItems.Magazine9mm);
        var pistol = Give(sim, you, CoreItems.Pistol);
        Assert.Equal(0, magazine.Amount);

        Do(sim, drive, new CombineAction(rounds.Id, magazine.Id));
        Assert.Equal(15, magazine.Amount);
        Assert.Equal(5, rounds.Amount);
        Do(sim, drive, new CombineAction(magazine.Id, pistol.Id));
        Assert.Same(magazine, Assert.Single(pistol.Contents));
        Assert.Equal("a 9 mm pistol (9 mm magazine, 15 rounds)", pistol.Describe());

        Do(sim, drive, new UnloadAction(pistol.Id)); // the magazine comes out
        Assert.Empty(pistol.Contents);
        Do(sim, drive, new UnloadAction(magazine.Id)); // and the rounds out of it, back with the others
        Assert.Equal(0, magazine.Amount);
        Assert.Equal(20, you.Inventory.Count(CoreItems.Rounds9mm));
    }

    [Fact]
    public void AFullMagazine_TakesNoMore()
    {
        var (sim, drive, _) = Person();
        var you = sim.Actors[0];
        var refusals = Refusals(sim);
        var magazine = Give(sim, you, CoreItems.Magazine9mm, 15);
        var rounds = Give(sim, you, CoreItems.Rounds9mm, 5);

        Do(sim, drive, new CombineAction(magazine.Id, rounds.Id));

        Assert.Contains("full", refusals[^1]);
        Assert.Equal(5, rounds.Amount);
    }

    [Fact]
    public void AStickAndDuctTape_MakeASplint()
    {
        var (sim, drive, you) = Person();
        var stick = Give(sim, you, CoreItems.Stick);
        var tape = Give(sim, you, CoreItems.DuctTape);
        Item? made = null;
        sim.ItemsCombined += (_, _, _, m) => made = m;

        Do(sim, drive, new CombineAction(stick.Id, tape.Id));

        Assert.Equal(CoreItems.Splint, made?.Kind);
        Assert.True(you.Inventory.Has(CoreItems.Splint));
        Assert.False(you.Inventory.Has(CoreItems.Stick));
        Assert.Equal(18, tape.Amount); // a splint takes two lengths of tape
    }

    [Fact]
    public void ThingsThatDontGoTogether_Dont()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var knife = Give(sim, you, CoreItems.Knife);
        var bar = Give(sim, you, CoreItems.EnergyBar);
        Do(sim, drive, new CombineAction(knife.Id, bar.Id));
        Assert.Contains("don't go together", refusals[^1]);
    }

    // ---------- using things up ----------

    [Fact]
    public void Eating_AndDrinking_UseThingsUp()
    {
        var (sim, drive, you) = Person();
        var bars = Give(sim, you, CoreItems.EnergyBar, 2);
        var water = Give(sim, you, CoreItems.WaterBottle);
        var uses = new List<(ItemUse, int)>();
        sim.ItemUsed += (_, _, use, n) => uses.Add((use, n));

        Do(sim, drive, new UseItemAction(bars.Id));
        Do(sim, drive, new UseItemAction(water.Id));

        Assert.Equal(1, you.Inventory.Count(CoreItems.EnergyBar));
        Assert.Equal(750, water.Amount);
        Assert.Equal(new[] { (ItemUse.Eat, 1), (ItemUse.Drink, 250) }, uses);
    }

    [Fact]
    public void ABandage_StopsABleed()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var bandage = Give(sim, you, CoreItems.Bandage, 2);
        Do(sim, drive, new UseItemAction(bandage.Id));
        Assert.Contains("nothing is bleeding", refusals[^1]);

        // only a major (red) wound bleeds
        sim.Injure(you, BodySection.LeftLeg, Severity.Red, bleeding: true, "a test");
        Assert.Equal(1, you.Health.BleedingWounds);
        Do(sim, drive, new UseItemAction(bandage.Id));

        Assert.Equal(0, you.Health.BleedingWounds);
        // the bleeding stops, but the wound is still red: it needs a first-aid kit
        Assert.Equal(Severity.Red, you.Health[BodySection.LeftLeg].Severity);
        Assert.Equal(BodySection.LeftLeg, you.Health.WorstUntreated());
        Assert.Equal(1, you.Inventory.Count(CoreItems.Bandage));
    }

    // ---------- the flashlight's batteries ----------

    [Fact]
    public void AFlashlight_WithoutCharge_WontLight_AndRunsDownWhenLit()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var flashlight = Give(sim, you, CoreItems.Flashlight, fitted: false);
        Do(sim, drive, new ToggleLightAction());
        Assert.Contains("no batteries", refusals[^1]);

        var a = Give(sim, you, CoreItems.BatteryD, 1);
        var b = Give(sim, you, CoreItems.BatteryD, 1);
        Do(sim, drive, new CombineAction(a.Id, flashlight.Id));
        Do(sim, drive, new CombineAction(b.Id, flashlight.Id));
        Assert.True(flashlight.Charged);
        bool died = false;
        sim.LightDied += _ => died = true;

        Do(sim, drive, new ToggleLightAction());
        Assert.True(sim.Lights[you.CarriedLight!.Value].On);
        Do(sim, drive, new WaitAction(ItemRules.LightTicksPerPercent + 100)); // 1% left in each: gone
        Do(sim, drive, new WaitAction(10));

        Assert.True(died);
        Assert.False(sim.Lights[you.CarriedLight!.Value].On);
        Assert.All(flashlight.Contents, c => Assert.Equal(0, c.Amount));
        Do(sim, drive, new ToggleLightAction());
        Assert.Contains("flat", refusals[^1]);
    }

    // ---------- locks ----------

    [Fact]
    public void AKeycard_OpensItsOwnLocks_AtItsLevelOrBelow()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var cabinet = sim.PlaceItems(TestContent.Catalog.ParseSpec("core:cabinet locked tag=lab level=2; >core:research-file"), new TilePos(6, 5))[0];
        var low = sim.CreateItem(CoreItems.Keycard, tag: "lab", level: 1);
        Assert.True(sim.GiveItem(you, low));

        Do(sim, drive, new OpenAction(cabinet.Id));
        Assert.Contains("locked", refusals[^1]);
        Assert.True(cabinet.Locked);

        var other = sim.CreateItem(CoreItems.Keycard, tag: "somewhere else", level: 3);
        Assert.True(sim.GiveItem(you, other));
        Do(sim, drive, new OpenAction(cabinet.Id));
        Assert.True(cabinet.Locked);

        var right = sim.CreateItem(CoreItems.Keycard, tag: "lab", level: 2);
        Assert.True(sim.GiveItem(you, right));
        Do(sim, drive, new OpenAction(cabinet.Id));
        Assert.False(cabinet.Locked);
        Assert.True(cabinet.IsOpen);
    }

    [Fact]
    public void ACrowbar_ForcesALock_Loudly()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var crate = sim.PlaceItems(TestContent.Catalog.ParseSpec("core:crate locked; >core:lantern"), new TilePos(6, 5))[0];

        Do(sim, drive, new PryAction(crate.Id));
        Assert.Contains("crowbar", refusals[^1]);
        Give(sim, you, CoreItems.Crowbar);
        long before = sim.CurrentTick;
        Do(sim, drive, new PryAction(crate.Id));

        Assert.False(crate.Locked);
        Assert.True(crate.IsOpen);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Prying && n.Loudness == ItemRules.PryLoudness);
        Assert.Equal(ItemRules.PryTicks, sim.CurrentTick - before);
    }

    // ---------- loot ----------

    private const string Island = "SmallNoCliffs";

    [Fact]
    public void TheLoot_IsTheSameEveryRunOfASeed_AndDiffersBetweenSeeds()
    {
        IslandWorld world = TestIslands.World(Island);
        static List<(ContentId, long, long, int, int)> Of(ItemLedger ledger) =>
            ledger.All.SelectMany(l => l.Item.SelfAndNested().Select(i => (i.Kind, l.X, l.Y, i.Amount, i.Id))).ToList();

        var first = Of(ItemPlan.Build(world, runSeed: 1, items: TestContent.Catalog));
        Assert.True(first.Count > 100, $"only {first.Count} items on the island");
        Assert.Equal(first, Of(ItemPlan.Build(world, runSeed: 1, items: TestContent.Catalog)));
        Assert.NotEqual(first, Of(ItemPlan.Build(world, runSeed: 2, items: TestContent.Catalog)));
    }

    [Fact]
    public void TheLoot_LiesOnFloor_NeverOnWallsOrEquipment()
    {
        IslandWorld world = TestIslands.World(Island);
        Site genetics = world.Works.Sites.First(t => t.Kind == "genetics");
        var window = new IslandWindow(world, TestIslands.Chunker(Island), genetics.X, genetics.Y, 6);
        var ledger = ItemPlan.Build(world, runSeed: 1, items: TestContent.Catalog);

        int checkedHere = 0;
        foreach (var lying in ledger.All)
        {
            var tile = new TilePos((int)(lying.X - window.OriginX), (int)(lying.Y - window.OriginY));
            if (!window.Map.InBounds(tile))
                continue;
            checkedHere++;
            Assert.True(window.Map.CoverAt(tile).Type == CoverType.Floor, $"{lying.Item} lies on {window.Map.CoverAt(tile).Name} at {tile}");
        }
        Assert.True(checkedHere > 20, $"only {checkedHere} items in the genetics window");
    }

    [Fact]
    public void LocksAndKeycards_BelongToTheirFacility()
    {
        IslandWorld world = TestIslands.World(Island);
        var siteNames = world.Works.Sites.Select(s => s.Name).ToHashSet();
        var everything = ItemPlan.Build(world, runSeed: 1, items: TestContent.Catalog).All.SelectMany(l => l.Item.SelfAndNested()).ToList();

        var cards = everything.Where(i => i.Kind == CoreItems.Keycard).ToList();
        Assert.NotEmpty(cards);
        Assert.All(cards, c => Assert.True(c.Tag == "monorail" || c.Tag is not null && c.Level is >= 1 and <= 3, $"{c} has no facility"));
        var locked = everything.Where(i => i.Locked).ToList();
        Assert.NotEmpty(locked);
        Assert.All(locked, l => Assert.NotNull(l.Tag));
        // every armoury locker is locked at level 3
        Assert.Contains(locked, l => l.Kind == CoreItems.Locker && l.Level == 3);
        // the monorail key lies somewhere if the island has the objective
        if (world.Works.Objectives.Any(o => o.Key == "monorail-key" && o.Room != "the main building"))
            Assert.Contains(cards, c => c.Tag == "monorail");
        Assert.All(cards.Where(c => c.Tag != "monorail"), c => Assert.True(siteNames.Contains(c.Tag!) || c.Tag!.Length > 0));
    }

    [Fact]
    public void TheIsland_TakesItsLoot_BeforeAnyoneArrives()
    {
        IslandWorld world = TestIslands.World(Island);
        var sim = new Simulation(TestContent.NewSession(), new WorldMap(10, 10), seed: 1);
        var ledger = ItemPlan.Build(world, runSeed: 1, items: TestContent.Catalog);
        sim.UseItems(ledger);
        Assert.Same(ledger, sim.Items);
        sim.AddActor("You", '@', new TilePos(1, 1), Species.Human, new PlayerController(), loadout: Loadout.TestbedSurvivor);
        Assert.Throws<InvalidOperationException>(() => sim.UseItems(new ItemLedger(TestContent.Catalog)));
        // the survivor's kit took fresh ids after the loot's
        Assert.All(sim.Actors[0].Inventory.All, i => Assert.True(i.Id > ledger.All.Max(l => l.Item.Id)));
    }
}
