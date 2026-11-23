using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Tests.ContentLibrary;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Scenarios;
using BlueHarvest.Sim.World;
using Xunit;
using BlueHarvest.Content;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests;

/// <summary>Step 20a: items as things, carried in hands, pockets and bags, lying about, and in containers.</summary>
public class ItemTests
{
    /// <summary>
    /// On the map an item must never pass for something else: no door (+ /), exit or ladder (&gt; &lt; H),
    /// lamp (*), player (@) or body (%), and no letter or digit, which creatures and people use. And
    /// every item of a family shares its family's glyph, so the families stay readable.
    /// </summary>
    [Fact]
    public void ItemGlyphs_NeverLookLikeSomethingElseOnTheMap_AndGoByFamily()
    {
        foreach (ItemDef def in TestContent.Catalog.All)
        {
            Assert.False("+/<>H*@%".Contains(def.Glyph), $"{def.Id} uses '{def.Glyph}', which means something else on the map");
            Assert.False(char.IsAsciiLetterOrDigit(def.Glyph), $"{def.Id} uses '{def.Glyph}', a letter or digit (creatures and people)");
        }
        char GlyphOf(ContentId kind) => TestContent.Catalog.Of(kind).Glyph;
        Assert.All(TestContent.Catalog.All.Where(d => d.Is(ItemCategory.Medical) && !d.Is(ItemCategory.Fixture)), d => Assert.Equal('✚', d.Glyph));
        Assert.All(TestContent.Catalog.All.Where(d => d.Is(ItemCategory.Edible) && !d.Is(ItemCategory.Medical)), d => Assert.Equal('♨', d.Glyph)); // pills are medical
        Assert.Equal('!', GlyphOf(CoreItems.Flashlight));
        Assert.Equal('!', GlyphOf(CoreItems.Flare));
        Assert.Equal(')', GlyphOf(CoreItems.Rifle));
        Assert.Equal('▣', GlyphOf(CoreItems.Locker));
        Assert.Equal('?', GlyphOf(CoreItems.ParkMap));
    }

    private static (Simulation Sim, PlayerController Drive, Actor You) Person(Loadout? loadout = null, TilePos? at = null)
    {
        var sim = new Simulation(TestContent.NewSession(), new WorldMap(20, 20), seed: 7);
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
    public void TheCatalog_HasEveryItemInTheRepoPacks_ByItsContentId()
    {
        var items = RepoContent.Registry.All<ItemDefinition>().ToList();
        Assert.Equal(items.Count, TestContent.Catalog.All.Count);
        foreach (var item in items)
            Assert.Equal(item.Id, TestContent.Catalog.Of(item.Id).Id);
        Assert.Equal(items.Count, TestContent.Catalog.All.Select(d => d.Id).Distinct().Count());
        Assert.All(TestContent.Catalog.All, d => Assert.Same(d, TestContent.Catalog.Find(d.Id.Value)));
        // IDs are case-sensitive, and text must be qualified unless a pack is given
        Assert.Null(TestContent.Catalog.Find("CORE:FLASHLIGHT"));
        Assert.Null(TestContent.Catalog.Find("flashlight"));
        Assert.Same(TestContent.Catalog.Of(CoreItems.Flashlight), TestContent.Catalog.Find("flashlight", "core"));
        Assert.Throws<KeyNotFoundException>(() => TestContent.Catalog.Of(new ContentId("core:toaster")));
    }

    [Fact]
    public void Containers_SayWhatTheyHold_AndBagsAreWorn()
    {
        Assert.True(TestContent.Catalog.Of(CoreItems.Daypack).Is(ItemCategory.Wearable | ItemCategory.Container));
        Assert.Equal(WearSlot.Back, TestContent.Catalog.Of(CoreItems.Daypack).Wear);
        Assert.True(TestContent.Catalog.Of(CoreItems.Locker).Holds!.Closes);
        Assert.Equal(ItemSize.Fixed, TestContent.Catalog.Of(CoreItems.Locker).Size);
        Assert.True(TestContent.Catalog.Of(CoreItems.WaterBottle).Is(ItemCategory.Potable));
        Assert.True(TestContent.Catalog.Of(CoreItems.EnergyBar).Is(ItemCategory.Edible));
    }

    [Fact]
    public void ASpec_RoundTrips()
    {
        const string text = "core:locker locked; >core:energy-bar x3; >core:water-bottle amount=400; core:crowbar";
        var specs = TestContent.Catalog.ParseSpec(text);

        Assert.Equal(4, specs.Count);
        Assert.Equal(new ItemSpec(CoreItems.Locker, Locked: true), specs[0]);
        Assert.Equal(new ItemSpec(CoreItems.EnergyBar, Count: 3, Inside: true), specs[1]);
        Assert.Equal(new ItemSpec(CoreItems.WaterBottle, Amount: 400, Inside: true), specs[2]);
        Assert.Equal(text, ItemCatalog.FormatSpec(specs));
    }

    [Theory]
    [InlineData("core:toaster")]
    [InlineData("core:flare x0")]
    [InlineData("core:water-bottle amount=5000")]
    [InlineData(">core:flare")]
    [InlineData("core:flare shiny")]
    [InlineData("flare")]          // bare IDs need a pack to resolve in
    [InlineData("core:Flare")]     // IDs are case-sensitive
    public void BadSpecs_AreRefused(string text) => Assert.Throws<FormatException>(() => TestContent.Catalog.ParseSpec(text));

    [Fact]
    public void ABareId_SaysToQualifyIt()
    {
        var e = Assert.Throws<FormatException>(() => TestContent.Catalog.ParseSpec("locker; >flare"));
        Assert.Contains("'core:locker'", e.Message);
    }

    [Fact]
    public void APacksOwnTable_CanUseBareIds()
    {
        var specs = TestContent.Catalog.ParseSpec("locker locked; >flare x3", "core");
        Assert.Equal(new ItemSpec(CoreItems.Locker, Locked: true), specs[0]);
        Assert.Equal(new ItemSpec(CoreItems.Flare, Count: 3, Inside: true), specs[1]);
        Assert.Equal("core:locker locked; >core:flare x3", ItemCatalog.FormatSpec(specs));
    }

    // ---------- carrying ----------

    [Fact]
    public void TheTestbedSurvivor_WearsADaypack_WithTheKitAndRopeInIt_HandsFree()
    {
        var (_, _, you) = Person(Loadout.TestbedSurvivor);
        var inventory = you.Inventory;

        var pack = inventory.WornOn(WearSlot.Back);
        Assert.NotNull(pack);
        Assert.Equal(CoreItems.Daypack, pack.Kind);
        Assert.Equal(new[] { CoreItems.FirstAidKit, CoreItems.Rope }, pack.Contents.Select(i => i.Kind));
        Assert.Equal(2, inventory.FreeHands);
        Assert.Equal(new[] { CoreItems.Flare, CoreItems.Flashlight },
            inventory.Carried.Where(c => c.Slot == CarrySlot.Pocket).Select(c => c.Item.Kind));
        Assert.Equal(1000 + 600 + 3 * 200 + (400 + 2 * 140) + 2500, inventory.Grams); // the flashlight has its two D batteries
        Assert.Equal(Load.Light, inventory.Load);
        // the count API still sees everything
        Assert.Equal(3, inventory.Count(CoreItems.Flare));
        Assert.True(inventory.Has(CoreItems.Rope));
        Assert.Equal(1, inventory.Count(CoreItems.FirstAidKit));
    }

    [Fact]
    public void Pockets_FillUp_ThenAHandTakesTheRest()
    {
        var (sim, _, you) = Person();
        for (int i = 0; i < 5; i++)
            Assert.True(sim.GiveItem(you, sim.CreateItem(CoreItems.CannedFood)));

        // four cans (1.8 L) fit the pockets as one stack; the fifth won't, and goes in a hand
        var pocket = Assert.Single(you.Inventory.Carried, c => c.Slot == CarrySlot.Pocket);
        Assert.Equal(4, pocket.Item.Amount);
        Assert.Single(you.Inventory.Carried, c => c.Slot == CarrySlot.RightHand);
        Assert.Equal(5, you.Inventory.Count(CoreItems.CannedFood));
        Assert.Equal(1, you.Inventory.FreeHands);
    }

    [Fact]
    public void ACrate_TakesBothHands()
    {
        var (sim, _, you) = Person();
        Assert.True(sim.GiveItem(you, sim.CreateItem(CoreItems.Crowbar))); // one hand busy
        var crate = sim.CreateItem(CoreItems.Crate);

        Assert.False(you.Inventory.PlaceFor(crate).Ok);
        Assert.Contains("both hands", you.Inventory.PlaceFor(crate).Refused);
    }

    [Fact]
    public void Nobody_CarriesMoreThan35Kilos()
    {
        var (sim, _, you) = Person();
        var crate = sim.CreateItem(CoreItems.Crate);
        for (int i = 0; i < 4; i++)
            sim.StowItem(crate, sim.CreateItem(CoreItems.CannedFood, 6)); // 8 kg + 24 × 450 g = 18.8 kg
        Assert.True(sim.GiveItem(you, crate));
        Assert.Equal(Load.Burdened, you.Inventory.Load);

        var locker = sim.CreateItem(CoreItems.Locker);
        Assert.Contains("doesn't move", you.Inventory.PlaceFor(locker).Refused);
        var heavy = sim.CreateItem(CoreItems.Crate);
        for (int i = 0; i < 6; i++)
            sim.StowItem(heavy, sim.CreateItem(CoreItems.CannedFood, 6)); // 24.2 kg on its own, 43 with the first
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
                var crate = sim.CreateItem(CoreItems.Crate);
                for (int i = 0; i < 5; i++)
                    sim.StowItem(crate, sim.CreateItem(CoreItems.CannedFood, 6)); // 8 + 13.5 kg
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
        var near = sim.CreateItem(CoreItems.Knife);
        var far = sim.CreateItem(CoreItems.Notebook);
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
        var rope = you.Inventory.All.First(i => i.Kind == CoreItems.Rope);

        Do(sim, drive, new DropAction(rope.Id));

        Assert.False(you.Inventory.Has(CoreItems.Rope));
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

        Do(sim, drive, new DropAction(you.Inventory.All.First(i => i.Kind == CoreItems.Flashlight).Id));

        Assert.False(sim.Lights[you.CarriedLight!.Value].On);
    }

    [Fact]
    public void ALocker_HidesWhatsInIt_UntilOpened_AndALockedOneWontOpen()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var placed = sim.PlaceItems(TestContent.Catalog.ParseSpec("core:locker; >core:energy-bar x3; >core:water-bottle"), new TilePos(6, 5));
        var locker = placed[0];
        var bars = placed[1];
        Assert.DoesNotContain(sim.ItemsWithinReach(you), r => r.Item == bars);

        Do(sim, drive, new PickUpAction(bars.Id));
        Assert.Contains("can't reach", refusals[^1]);
        Do(sim, drive, new OpenAction(locker.Id));
        Assert.True(locker.IsOpen);
        Assert.Contains(sim.ItemsWithinReach(you), r => r.Item == bars && r.In == locker);
        Do(sim, drive, new PickUpAction(bars.Id));
        Assert.Equal(3, you.Inventory.Count(CoreItems.EnergyBar));
        Assert.Single(locker.Contents);

        var shut = sim.PlaceItems(TestContent.Catalog.ParseSpec("core:cabinet locked; >core:knife"), new TilePos(5, 6))[0];
        Do(sim, drive, new OpenAction(shut.Id));
        Assert.False(shut.IsOpen);
        Assert.Contains("locked", refusals[^1]);
    }

    [Fact]
    public void YouCanPutThingsInAnOpenContainer_ButNotAShutOne()
    {
        var (sim, drive, you) = Person();
        var refusals = Refusals(sim);
        var knife = sim.CreateItem(CoreItems.Knife);
        Assert.True(sim.GiveItem(you, knife));
        var crate = sim.PlaceItems(new[] { new ItemSpec(CoreItems.Crate) }, new TilePos(6, 5))[0];

        Do(sim, drive, new PutAction(knife.Id, crate.Id));
        Assert.Contains("shut", refusals[^1]);
        Do(sim, drive, new OpenAction(crate.Id));
        Do(sim, drive, new PutAction(knife.Id, crate.Id));

        Assert.Same(knife, Assert.Single(crate.Contents));
        Assert.False(you.Inventory.Has(CoreItems.Knife));
    }

    [Fact]
    public void ABag_CantGoInsideWhatsInsideIt()
    {
        var (sim, _, _) = Person();
        var pack = sim.CreateItem(CoreItems.FieldPack);
        var pouch = sim.CreateItem(CoreItems.BeltPouch);
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
        var kits = pete.Inventory.All.Single(i => i.Kind == CoreItems.FirstAidKit);
        Assert.Contains(sim.ItemsWithinReach(you), r => r.Item == kits && r.On == pete);

        Do(sim, drive, new PickUpAction(kits.Id));

        Assert.Equal(2, you.Inventory.Count(CoreItems.FirstAidKit));
        Assert.False(pete.Inventory.Has(CoreItems.FirstAidKit));
    }

    // ---------- ids, the hash and map files ----------

    [Fact]
    public void ItemIds_AreTheSame_EveryRunOfASeed()
    {
        static int[] Ids()
        {
            var (sim, _, you) = Person(Loadout.TestbedSurvivor);
            sim.PlaceItems(TestContent.Catalog.ParseSpec("core:desk; >core:notebook; >core:painkillers"), new TilePos(2, 2));
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
        var knife = sim.CreateItem(CoreItems.Knife);
        sim.PlaceItem(knife, new TilePos(5, 5));
        ulong before = sim.ComputeStateHash();
        Do(sim, drive, new PickUpAction(knife.Id));
        Assert.NotEqual(before, sim.ComputeStateHash());
    }

    [Fact]
    public void ItemMarkers_LayOutTheirItems_AndRoundTrip()
    {
        const string text =
            "bhmap 3\n" +
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
            "item 2 1 core:fridge open; >core:canned-food x2; >core:water-bottle amount=250\n" +
            "end\n";
        var data = MapFile.Parse(text, TestContent.Catalog);
        Assert.Equal(text, MapFile.Serialize(data));

        var sim = ScenarioBuilder.Build(TestContent.NewSession(), data, seed: 1);
        var fridge = Assert.Single(sim.ItemsAt(new TilePos(2, 1))).Item;
        Assert.Equal(CoreItems.Fridge, fridge.Kind);
        Assert.True(fridge.IsOpen);
        Assert.Equal(new[] { CoreItems.CannedFood, CoreItems.WaterBottle }, fridge.Contents.Select(i => i.Kind));
        Assert.Equal(250, fridge.Contents[1].Amount);
    }

    [Fact]
    public void AnItemMarker_WithAnUnknownItem_IsRefused()
    {
        const string text = "bhmap 3\nsize 1 1\nheights\n0\ncover\n.\nmarkers\nplayer 0 0\nitem 0 0 core:toaster\nend\n";
        Assert.Throws<FormatException>(() => MapFile.Parse(text, TestContent.Catalog));
    }

    [Theory]
    [InlineData("bhmap 2", "item 0 0 locker")]       // the old form
    [InlineData("bhmap 2", "item 0 0 core:locker")]  // even qualified: version 2 never had content IDs
    [InlineData("bhmap 3", "item 0 0 locker")]       // version 3 qualifies every ID
    public void OldItemMarkers_AreRefused_WithWhatToDo(string header, string marker)
    {
        string text = $"{header}\nsize 1 1\nheights\n0\ncover\n.\nmarkers\nplayer 0 0\n{marker}\nend\n";
        var e = Assert.Throws<FormatException>(() => MapFile.Parse(text, TestContent.Catalog));
        Assert.Contains("core:locker", e.Message);
    }

    [Fact]
    public void AVersion2Map_WithoutItems_StillReads()
    {
        var data = MapFile.Parse("bhmap 2\nsize 1 1\nheights\n0\ncover\n.\nmarkers\nplayer 0 0\nend\n", TestContent.Catalog);
        Assert.Single(data.Markers);
    }
}
