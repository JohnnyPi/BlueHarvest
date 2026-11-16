using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Step 20a: items as things, carried in hands, pockets and bags, lying about, and in containers.</summary>
public class ItemTests
{
    private static (Simulation Sim, PlayerController Drive, Actor You) Person(Loadout? loadout = null, TilePos? at = null)
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 7);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', at ?? new TilePos(5, 5), Species.Human, drive, Direction.East, loadout: loadout);
        sim.RunUntilInputNeeded();
        return (sim, drive, you);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    private static List<string> Refusals(Simulation sim)
    {
        var reasons = new List<string>();
        sim.MoveBlocked += (_, why) => reasons.Add(why);
        return reasons;
    }

    // ---------- the catalog ----------

    [Fact]
    public void EveryKind_HasOneDef_WithAUniqueId()
    {
        var kinds = Enum.GetValues<ItemKind>();
        Assert.Equal(kinds.Length, ItemCatalog.All.Count);
        foreach (var kind in kinds)
            Assert.Equal(kind, ItemCatalog.Of(kind).Kind);
        Assert.Equal(kinds.Length, ItemCatalog.All.Select(d => d.Id).Distinct().Count());
        Assert.All(ItemCatalog.All, d => Assert.Same(d, ItemCatalog.Find(d.Id.ToUpperInvariant())));
    }

    [Fact]
    public void Containers_SayWhatTheyHold_AndBagsAreWorn()
    {
        Assert.True(ItemCatalog.Of(ItemKind.Daypack).Is(ItemCategory.Wearable | ItemCategory.Container));
        Assert.Equal(WearSlot.Back, ItemCatalog.Of(ItemKind.Daypack).Wear);
        Assert.True(ItemCatalog.Of(ItemKind.Locker).Holds!.Closes);
        Assert.Equal(ItemSize.Fixed, ItemCatalog.Of(ItemKind.Locker).Size);
        Assert.True(ItemCatalog.Of(ItemKind.WaterBottle).Is(ItemCategory.Potable));
        Assert.True(ItemCatalog.Of(ItemKind.EnergyBar).Is(ItemCategory.Edible));
    }

    [Fact]
    public void ASpec_RoundTrips()
    {
        const string text = "locker locked; >energy-bar x3; >water-bottle amount=400; crowbar";
        var specs = ItemCatalog.ParseSpec(text);

        Assert.Equal(4, specs.Count);
        Assert.Equal(new ItemSpec(ItemKind.Locker, Locked: true), specs[0]);
        Assert.Equal(new ItemSpec(ItemKind.EnergyBar, Count: 3, Inside: true), specs[1]);
        Assert.Equal(new ItemSpec(ItemKind.WaterBottle, Amount: 400, Inside: true), specs[2]);
        Assert.Equal(text, ItemCatalog.FormatSpec(specs));
    }

    [Theory]
    [InlineData("toaster")]
    [InlineData("flare x0")]
    [InlineData("water-bottle amount=5000")]
    [InlineData(">flare")]
    [InlineData("flare shiny")]
    public void BadSpecs_AreRefused(string text) => Assert.Throws<FormatException>(() => ItemCatalog.ParseSpec(text));

    // ---------- carrying ----------

    [Fact]
    public void TheTestbedSurvivor_WearsADaypack_WithTheKitAndRopeInIt_HandsFree()
    {
        var (_, _, you) = Person(Loadout.TestbedSurvivor);
        var inventory = you.Inventory;

        var pack = inventory.WornOn(WearSlot.Back);
        Assert.NotNull(pack);
        Assert.Equal(ItemKind.Daypack, pack.Kind);
        Assert.Equal(new[] { ItemKind.FirstAidKit, ItemKind.Rope }, pack.Contents.Select(i => i.Kind));
        Assert.Equal(2, inventory.FreeHands);
        Assert.Equal(new[] { ItemKind.Flare, ItemKind.Flashlight },
            inventory.Carried.Where(c => c.Slot == CarrySlot.Pocket).Select(c => c.Item.Kind));
        Assert.Equal(1000 + 600 + 3 * 200 + (400 + 2 * 140) + 2500, inventory.Grams); // the flashlight has its two D batteries
        Assert.Equal(Load.Light, inventory.Load);
        // the count API still sees everything
        Assert.Equal(3, inventory.Count(ItemKind.Flare));
        Assert.True(inventory.Has(ItemKind.Rope));
        Assert.Equal(1, inventory.Count(ItemKind.FirstAidKit));
    }

    [Fact]
    public void Pockets_FillUp_ThenAHandTakesTheRest()
    {
        var (sim, _, you) = Person();
        for (int i = 0; i < 5; i++)
            Assert.True(sim.GiveItem(you, sim.CreateItem(ItemKind.CannedFood)));

        // four cans (1.8 L) fit the pockets as one stack; the fifth won't, and goes in a hand
        var pocket = Assert.Single(you.Inventory.Carried, c => c.Slot == CarrySlot.Pocket);
        Assert.Equal(4, pocket.Item.Amount);
        Assert.Single(you.Inventory.Carried, c => c.Slot == CarrySlot.RightHand);
        Assert.Equal(5, you.Inventory.Count(ItemKind.CannedFood));
        Assert.Equal(1, you.Inventory.FreeHands);
    }

    [Fact]
    public void ACrate_TakesBothHands()
    {
        var (sim, _, you) = Person();
        Assert.True(sim.GiveItem(you, sim.CreateItem(ItemKind.Crowbar))); // one hand busy
        var crate = sim.CreateItem(ItemKind.Crate);

        Assert.False(you.Inventory.PlaceFor(crate).Ok);
        Assert.Contains("both hands", you.Inventory.PlaceFor(crate).Refused);
    }

    [Fact]
    public void Nobody_CarriesMoreThan35Kilos()
    {
        var (sim, _, you) = Person();
        var crate = sim.CreateItem(ItemKind.Crate);
        for (int i = 0; i < 4; i++)
            sim.StowItem(crate, sim.CreateItem(ItemKind.CannedFood, 6)); // 8 kg + 24 × 450 g = 18.8 kg
        Assert.True(sim.GiveItem(you, crate));
        Assert.Equal(Load.Burdened, you.Inventory.Load);

        var locker = sim.CreateItem(ItemKind.Locker);
        Assert.Contains("doesn't move", you.Inventory.PlaceFor(locker).Refused);
        var heavy = sim.CreateItem(ItemKind.Crate);
        for (int i = 0; i < 6; i++)
            sim.StowItem(heavy, sim.CreateItem(ItemKind.CannedFood, 6)); // 24.2 kg on its own, 43 with the first
        Assert.Contains("too much to carry", you.Inventory.PlaceFor(heavy).Refused);
    }

    [Fact]
    public void HeavilyLaden_YouCantSprint_AndWalkSlower()
    {
        long StepTicks(bool laden, bool sprint)
        {
            var (sim, drive, you) = Person();
            if (laden)
            {
                var crate = sim.CreateItem(ItemKind.Crate);
                for (int i = 0; i < 5; i++)
                    sim.StowItem(crate, sim.CreateItem(ItemKind.CannedFood, 6)); // 8 + 13.5 kg
                Assert.True(sim.GiveItem(you, crate));
                Assert.Equal(Load.Heavy, you.Inventory.Load);
            }
            long start = sim.CurrentTick;
            Do(sim, drive, new MoveAction(Direction.East, Sprint: sprint));
            return sim.CurrentTick - start;
        }

        long walk = StepTicks(false, false), sprint = StepTicks(false, true);
        long ladenWalk = StepTicks(true, false), ladenSprint = StepTicks(true, true);
        Assert.True(sprint < walk);
        Assert.Equal(walk * ItemRules.HeavyWalkPercent / 100, ladenWalk);
        Assert.Equal(ladenWalk, ladenSprint); // no sprinting
    }

    // ---------- in the world ----------

    [Fact]
    public void YouPickUp_WhatsNextToYou_ButNotFarther()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var near = sim.CreateItem(ItemKind.Knife);
        var far = sim.CreateItem(ItemKind.Notebook);
        sim.PlaceItem(near, new TilePos(6, 6));
        sim.PlaceItem(far, new TilePos(7, 5));
        var picked = new List<Item>();
        sim.ItemPickedUp += (_, item) => picked.Add(item);

        Do(sim, drive, new PickUpAction(far.Id));
        Assert.Contains("can't reach", Assert.Single(refusals));
        Do(sim, drive, new PickUpAction(near.Id));

        Assert.Equal(near, Assert.Single(picked));
        Assert.Empty(sim.ItemsAt(new TilePos(6, 6)));
        Assert.Equal(CarrySlot.Pocket, you.Inventory.SlotOf(near));
        Assert.Single(sim.ItemsAt(new TilePos(7, 5)));
    }

    [Fact]
    public void PuttingSomethingDown_LeavesItWhereYouStand()
    {
        var (sim, drive, you) = Person(Loadout.TestbedSurvivor);
        var rope = you.Inventory.All.First(i => i.Kind == ItemKind.Rope);

        Do(sim, drive, new DropAction(rope.Id));

        Assert.False(you.Inventory.Has(ItemKind.Rope));
        var lying = Assert.Single(sim.ItemsAt(you.Position));
        Assert.Same(rope, lying.Item);
        Assert.Equal(new TilePos(5, 5), sim.WhereIs(rope));
    }

    [Fact]
    public void DroppingYourOnlyFlashlight_PutsItsLightOut()
    {
        var (sim, drive, you) = Person(Loadout.TestbedSurvivor);
        Do(sim, drive, new ToggleLightAction());
        Assert.True(sim.Lights[you.CarriedLight!.Value].On);

        Do(sim, drive, new DropAction(you.Inventory.All.First(i => i.Kind == ItemKind.Flashlight).Id));

        Assert.False(sim.Lights[you.CarriedLight!.Value].On);
    }

    [Fact]
    public void ALocker_HidesWhatsInIt_UntilOpened_AndALockedOneWontOpen()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var placed = sim.PlaceItems(ItemCatalog.ParseSpec("locker; >energy-bar x3; >water-bottle"), new TilePos(6, 5));
        var locker = placed[0];
        var bars = placed[1];
        Assert.DoesNotContain(sim.ItemsWithinReach(you), r => r.Item == bars);

        Do(sim, drive, new PickUpAction(bars.Id));
        Assert.Contains("can't reach", refusals[^1]);
        Do(sim, drive, new OpenAction(locker.Id));
        Assert.True(locker.IsOpen);
        Assert.Contains(sim.ItemsWithinReach(you), r => r.Item == bars && r.In == locker);
        Do(sim, drive, new PickUpAction(bars.Id));
        Assert.Equal(3, you.Inventory.Count(ItemKind.EnergyBar));
        Assert.Single(locker.Contents);

        var shut = sim.PlaceItems(ItemCatalog.ParseSpec("cabinet locked; >knife"), new TilePos(5, 6))[0];
        Do(sim, drive, new OpenAction(shut.Id));
        Assert.False(shut.IsOpen);
        Assert.Contains("locked", refusals[^1]);
    }

    [Fact]
    public void YouCanPutThingsInAnOpenContainer_ButNotAShutOne()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var knife = sim.CreateItem(ItemKind.Knife);
        Assert.True(sim.GiveItem(you, knife));
        var crate = sim.PlaceItems(new[] { new ItemSpec(ItemKind.Crate) }, new TilePos(6, 5))[0];

        Do(sim, drive, new PutAction(knife.Id, crate.Id));
        Assert.Contains("shut", refusals[^1]);
        Do(sim, drive, new OpenAction(crate.Id));
        Do(sim, drive, new PutAction(knife.Id, crate.Id));

        Assert.Same(knife, Assert.Single(crate.Contents));
        Assert.False(you.Inventory.Has(ItemKind.Knife));
    }

    [Fact]
    public void ABag_CantGoInsideWhatsInsideIt()
    {
        var (sim, _, _) = Person();
        var pack = sim.CreateItem(ItemKind.FieldPack);
        var pouch = sim.CreateItem(ItemKind.BeltPouch);
        sim.StowItem(pack, pouch);
        Assert.NotNull(pouch.RefusesToHold(pack));
        Assert.NotNull(pack.RefusesToHold(pack));
    }

    [Fact]
    public void TheDead_CanBeSearched()
    {
        var (sim, drive, you) = Person();
        var pete = sim.AddActor("Pete", 'h', new TilePos(6, 5), Species.Human, new ScriptedController(Array.Empty<ActorAction>()),
            loadout: Loadout.Doctor);
        sim.Injure(pete, BodySection.Torso, Severity.Red, bleeding: false, "a test");
        sim.Injure(pete, BodySection.Torso, Severity.Red, bleeding: false, "a test");
        Assert.True(pete.IsDead);
        var kits = pete.Inventory.All.Single(i => i.Kind == ItemKind.FirstAidKit);
        Assert.Contains(sim.ItemsWithinReach(you), r => r.Item == kits && r.On == pete);

        Do(sim, drive, new PickUpAction(kits.Id));

        Assert.Equal(2, you.Inventory.Count(ItemKind.FirstAidKit));
        Assert.False(pete.Inventory.Has(ItemKind.FirstAidKit));
    }

    // ---------- ids, the hash and map files ----------

    [Fact]
    public void ItemIds_AreTheSame_EveryRunOfASeed()
    {
        static int[] Ids()
        {
            var (sim, _, you) = Person(Loadout.TestbedSurvivor);
            sim.PlaceItems(ItemCatalog.ParseSpec("desk; >notebook; >painkillers"), new TilePos(2, 2));
            return you.Inventory.All.Concat(sim.Items.All.SelectMany(l => l.Item.SelfAndNested())).Select(i => i.Id).ToArray();
        }

        Assert.Equal(Ids(), Ids());
        // the survivor's 7 (pack, kit, flares, flashlight and its 2 batteries, rope), then the desk and its 2
        Assert.Equal(Enumerable.Range(1, 10), Ids().Order());
    }

    [Fact]
    public void PickingSomethingUp_ChangesTheStateHash()
    {
        var (sim, drive, _) = Person();
        var knife = sim.CreateItem(ItemKind.Knife);
        sim.PlaceItem(knife, new TilePos(5, 5));
        ulong before = sim.ComputeStateHash();
        Do(sim, drive, new PickUpAction(knife.Id));
        Assert.NotEqual(before, sim.ComputeStateHash());
    }

    [Fact]
    public void ItemMarkers_LayOutTheirItems_AndRoundTrip()
    {
        const string text =
            "bhmap 2\n" +
            "size 4 3\n" +
            "heights\n" +
            "0 0 0 0\n" +
            "0 0 0 0\n" +
            "0 0 0 0\n" +
            "cover\n" +
            "....\n" +
            "....\n" +
            "....\n" +
            "markers\n" +
            "player 0 0 You\n" +
            "item 2 1 fridge open; >canned-food x2; >water-bottle amount=250\n" +
            "end\n";
        var data = MapFile.Parse(text);
        Assert.Equal(text, MapFile.Serialize(data));

        var sim = ScenarioBuilder.Build(data, seed: 1);
        var fridge = Assert.Single(sim.ItemsAt(new TilePos(2, 1))).Item;
        Assert.Equal(ItemKind.Fridge, fridge.Kind);
        Assert.True(fridge.IsOpen);
        Assert.Equal(new[] { ItemKind.CannedFood, ItemKind.WaterBottle }, fridge.Contents.Select(i => i.Kind));
        Assert.Equal(250, fridge.Contents[1].Amount);
    }

    [Fact]
    public void AnItemMarker_WithAnUnknownItem_IsRefused()
    {
        const string text = "bhmap 2\nsize 1 1\nheights\n0\ncover\n.\nmarkers\nplayer 0 0\nitem 0 0 toaster\nend\n";
        Assert.Throws<FormatException>(() => MapFile.Parse(text));
    }
}
