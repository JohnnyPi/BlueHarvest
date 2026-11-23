using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>
/// P2b's catalog: the sim's items are built from content/packs. P2a's parity tests showed, field by field, that the
/// YAML says what the old code table said under one reading; each item here must come out of the catalog under that
/// same reading, so nothing the sim does changes. The behaviour tests (food, water, lights, fire, power, the
/// testbed) are the rest of the gate.
/// </summary>
public class ItemCatalogTests
{
    public static TheoryData<string> ItemIds()
    {
        var data = new TheoryData<string>();
        foreach (ItemDefinition item in RepoContent.Registry.All<ItemDefinition>())
            data.Add(item.Id.Value);
        return data;
    }

    static List<string> Words(ItemCategory categories) =>
        Enum.GetValues<ItemCategory>()
            .Where(c => c != ItemCategory.None && (categories & c) != 0)
            .Select(c => c.ToString().ToLowerInvariant())
            .ToList();

    static List<string> InFlagOrder(IEnumerable<string> words) =>
        words.OrderBy(c => ItemWords.Categories.ToList().IndexOf(c)).ToList();

    static string Word(ItemSize size) => size switch
    {
        ItemSize.Pocket => "pocket",
        ItemSize.Hand => "hand",
        ItemSize.TwoHanded => "two_handed",
        _ => "fixed",
    };

    static readonly int[] DrinkSamples = [0, 1, 7, 99, 100, 250, 330, 333, 500, 1000, 1500];

    [Theory]
    [MemberData(nameof(ItemIds))]
    public void EachItem_IsWhatItsYamlSays(string id)
    {
        ItemDefinition item = RepoContent.Registry.Get<ItemDefinition>(id);
        ItemDef def = ItemCatalog.Of(item.Id);

        Assert.Equal(item.Id, def.Id);
        Assert.Equal(item.Name, def.Name);
        Assert.Equal(item.Plural ?? item.Name + "s", def.Plural);
        Assert.Equal(item.Glyph ?? ItemDefBuilder.DefaultGlyph.ToString(), def.Glyph.ToString());
        Assert.Equal(InFlagOrder(item.Categories), Words(def.Categories));
        Assert.Equal(item.Size, Word(def.Size));
        Assert.Equal(item.MassG, def.Grams);
        Assert.Equal(item.VolumeMl, def.Ml);
        Assert.Equal(item.MaxAmount, def.MaxAmount);
        Assert.Equal(item.StartAmount, def.StartAmount);
        Assert.Equal(item.Stacks, def.Stacks);
        Assert.Equal(item.Fill?.MgPerUnit ?? 0, def.MilligramsPerUnit);
        Assert.Equal(item.Fill?.Unit, def.Unit);
        Assert.Equal(item.Fill?.FilledBy, def.Fills);
        Assert.Equal(item.Wear, def.Wear == WearSlot.None ? null : def.Wear.ToString().ToLowerInvariant());
        Assert.Equal(item.Description, def.Note ?? "");
        Assert.Equal(item.Dose, def.Dose);
        Assert.Equal(item.UseTicks, def.UseTicks);
        Assert.Equal(item.Holds is null, def.Holds is null);
        if (def.Holds is { } holds && item.Holds is { } yaml)
        {
            Assert.Equal(yaml.CapacityMl, holds.CapacityMl);
            Assert.Equal(yaml.MaxItemMl, holds.MaxItemMl);
            Assert.Equal(yaml.MaxItems, holds.MaxItems);
            Assert.Equal(InFlagOrder(yaml.Accepts), Words(holds.Accepts));
            Assert.Equal(yaml.Closes, holds.Closes);
            Assert.Equal(yaml.OpenLoudness, holds.OpenLoudness);
            Assert.Equal(yaml.OpenTicks, holds.OpenTicks);
            // a slot (a flashlight's batteries) has an Only list; a bag has none, not an empty one
            Assert.Equal(yaml.Only.Count == 0, holds.Only is null);
            Assert.Equal(yaml.Only, holds.Only ?? []);
        }
        Assert.Equal(item.ComesWith.Count == 0, def.Comes is null);
        Assert.Equal(item.ComesWith.Select(c => (c.Item, c.Quantity)).ToList(), (def.Comes ?? []).Select(c => (c.Kind, c.Count)).ToList());

        // food, drink and warmth
        ItemFood food = item.Food ?? new ItemFood(0, 0, 0);
        Assert.Equal((food.Kcal, food.WaterMl, food.AlertMinutes), Nutrition.Food(def));
        foreach (int ml in DrinkSamples)
            Assert.Equal(item.Drink?.For(ml) ?? (0, ml, 0), Nutrition.Drink(def, ml));
        Assert.Equal(item.Warmth, def.Warmth);

        // handlers, and the way of using it that its use.* handler names (at most one)
        Assert.Equal(item.Handlers.OrderBy(h => h, StringComparer.Ordinal), def.Handlers.OrderBy(h => h, StringComparer.Ordinal));
        string? useKey = item.Handlers.SingleOrDefault(h => ItemHandlers.UseKeys.Contains(h));
        Assert.Equal(useKey is null, Recipes.UseOf(def) is null);
    }

    [Fact]
    public void EachUseHandler_NamesAWayOfUsing()
    {
        foreach (string key in ItemHandlers.UseKeys)
        {
            var def = ItemCatalog.All.First(d => d.Has(key));
            Assert.NotNull(Recipes.UseOf(def));
        }
        Assert.Equal(ItemUse.TakeMedicine, Recipes.UseOf(ItemCatalog.Of(CoreItems.Painkillers)));
        Assert.Equal(ItemUse.TakeMedicine, Recipes.UseOf(ItemCatalog.Of(CoreItems.Antibiotics)));
        Assert.Equal(ItemUse.DressWounds, Recipes.UseOf(ItemCatalog.Of(CoreItems.Splint)));
        Assert.Equal(ItemUse.DressWounds, Recipes.UseOf(ItemCatalog.Of(CoreItems.Antiseptic)));
        Assert.Equal(ItemUse.Inject, Recipes.UseOf(ItemCatalog.Of(CoreItems.EpiPen)));
        Assert.Null(Recipes.UseOf(ItemCatalog.Of(CoreItems.FirstAidKit))); // it has its own key
    }

    [Fact]
    public void TheCorePack_HasEveryCoreItem_AndAllItemsAreRuntimeReady()
    {
        ContentRegistry registry = RepoContent.Registry;
        var named = typeof(CoreItems).GetFields().Select(f => (ContentId)f.GetValue(null)!).ToList();
        Assert.Equal(91, named.Count);
        Assert.Equal(named.Count, named.Distinct().Count());
        foreach (ContentId id in named)
            Assert.True(registry.TryGet(id, out ItemDefinition? _), $"CoreItems names {id}, which content/packs doesn't have.");
        Assert.Contains(CoreItems.EpiPen, named);
        Assert.Equal("core:epi-pen", CoreItems.EpiPen.Value);
        // the 91 kinds the code had, and the four catalog items the slice adds (P1b)
        Assert.Equal(95, registry.All<ItemDefinition>().Count());
        Assert.All(registry.All<ItemDefinition>(), d => Assert.True(registry.IsRuntimeReady(d.Id), $"{d.Id} isn't runtime-ready."));
    }

    [Fact]
    public void TheInstalledCatalog_IsTheRepoPacks()
    {
        Assert.Null(TestContent.InstallError);
        Assert.True(ItemCatalog.IsInstalled);
        Assert.Equal(RepoContent.Registry.Fingerprint, ItemCatalog.Fingerprint);
        // installing the same packs again changes nothing
        ItemDef flashlight = ItemCatalog.Of(CoreItems.Flashlight);
        ItemCatalog.Install(RepoContent.Registry);
        Assert.Same(flashlight, ItemCatalog.Of(CoreItems.Flashlight));
    }

    [Fact]
    public void PacksWhoseItemsTheSimCantUse_AreRefused_AndTheCatalogStays()
    {
        // without the sim's item handlers, every item names handlers this load doesn't know
        var options = new ContentLoadOptions { Mode = ContentMode.Authoring, GameVersion = SimContent.GameVersion, Handlers = HandlerRegistry.Default };
        ContentRegistry bare = RepoContent.Load(options).Registry!;
        Assert.NotEqual(RepoContent.Registry.Fingerprint, bare.Fingerprint);
        var e = Assert.Throws<InvalidOperationException>(() => ItemCatalog.Install(bare));
        Assert.Contains("core:flashlight", e.Message);
        Assert.Equal(RepoContent.Registry.Fingerprint, ItemCatalog.Fingerprint);
    }

    [Fact]
    public void EveryItemHandler_IsNamedByAnItem()
    {
        var named = RepoContent.Registry.All<ItemDefinition>().SelectMany(i => i.Handlers).ToHashSet();
        foreach (HandlerContract contract in ItemHandlers.Contracts)
            Assert.True(named.Contains(contract.Key), $"No item in content/packs names {contract.Key}.");
        Assert.Equal(ItemHandlers.Contracts.Count, ItemHandlers.Contracts.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public void SimContent_KnowsBothTheP5ContractsAndTheItemHandlers()
    {
        Assert.Equal(HandlerRegistry.Default.Contracts.Count + ItemHandlers.Contracts.Count, SimContent.Handlers.Contracts.Count);
        Assert.False(SimContent.Handlers.TryGet("process.transform", out HandlerContract? transform) && transform.Implemented);
        Assert.True(SimContent.Handlers.TryGet(ItemHandlers.Eat, out HandlerContract? eat) && eat.Implemented);
        Assert.Equal(SimInfo.Version, SimContent.GameVersion?.ToString());
    }
}
