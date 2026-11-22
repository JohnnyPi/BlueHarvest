using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>
/// P2a's gate: content/packs/core/items holds all 91 kinds, and each one's YAML says exactly what the sim says today,
/// field by field: ItemCatalog's every field, Nutrition's food and drink values, the clothing warmth, and the
/// handlers for what the code does with the kind. When P2b builds the catalog from content, nothing changes.
/// </summary>
public class ItemCatalogParityTests
{
    public static TheoryData<ItemKind> Kinds()
    {
        var data = new TheoryData<ItemKind>();
        foreach (ItemKind kind in Enum.GetValues<ItemKind>())
            data.Add(kind);
        return data;
    }

    /// <summary>The kind's content ID (contracts §11): its catalog id under core:, except EpiPen's.</summary>
    static string IdOf(ItemKind kind) => kind == ItemKind.EpiPen ? "core:epi-pen" : "core:" + ItemCatalog.Of(kind).Id;

    static ItemDefinition DefinitionOf(ItemKind kind)
    {
        Assert.True(RepoContent.Registry.TryGet(new ContentId(IdOf(kind)), out ItemDefinition? definition),
            $"content/packs has no item {IdOf(kind)} for ItemKind.{kind}.");
        return definition!;
    }

    static List<string> Words(ItemCategory categories) =>
        Enum.GetValues<ItemCategory>()
            .Where(c => c != ItemCategory.None && (categories & c) != 0)
            .Select(c => c.ToString().ToLowerInvariant())
            .ToList();

    static string Word(ItemSize size) => size switch
    {
        ItemSize.Pocket => "pocket",
        ItemSize.Hand => "hand",
        ItemSize.TwoHanded => "two_handed",
        _ => "fixed",
    };

    /// <summary>The use handler today's Recipes.UseOf gives the kind, as a key.</summary>
    static string? UseKey(ItemDef def) => Recipes.UseOf(def) switch
    {
        null => null,
        ItemUse.Eat => ItemHandlers.Eat,
        ItemUse.Drink => ItemHandlers.Drink,
        ItemUse.Read => ItemHandlers.Read,
        ItemUse.StopBleeding => ItemHandlers.StopBleeding,
        ItemUse.TakeMedicine => def.Kind == ItemKind.Painkillers ? ItemHandlers.Painkillers : ItemHandlers.Antibiotics,
        ItemUse.DressWounds => def.Kind == ItemKind.Splint ? ItemHandlers.DressWounds : ItemHandlers.CleanWounds,
        ItemUse.Inject => ItemHandlers.Adrenaline,
        ItemUse.SwitchLight => ItemHandlers.SwitchLight,
        ItemUse.Crack => ItemHandlers.Crack,
        ItemUse.LightTorch => ItemHandlers.LightTorch,
        ItemUse.TakeBearing => ItemHandlers.Bearing,
        ItemUse.Listen => ItemHandlers.Listen,
        ItemUse.TreatWater => ItemHandlers.TreatWater,
        var other => throw new InvalidOperationException($"No handler key for ItemUse.{other}."),
    };

    /// <summary>
    /// The kinds the sim's code checks by name, outside Recipes.UseOf (found by searching for each kind; see
    /// ItemHandlers). P2b turns each check into a check for the handler, and then this table goes.
    /// </summary>
    static readonly Dictionary<ItemKind, string[]> Specific = new()
    {
        [ItemKind.FirstAidKit] = [ItemHandlers.FirstAid],          // Simulation.TreatWound
        [ItemKind.Flare] = [ItemHandlers.Flare],                   // Simulation.Throw, Simulation.Fire.Ignite
        [ItemKind.GlowStick] = [ItemHandlers.GlowStick],           // Simulation.Throw, Simulation.Lights
        [ItemKind.Flashlight] = [ItemHandlers.Flashlight],         // Simulation, Simulation.ItemUse, Simulation.Items
        [ItemKind.Headlamp] = [ItemHandlers.Headlamp],             // Simulation.Lights
        [ItemKind.Lantern] = [ItemHandlers.Lantern],
        [ItemKind.Torch] = [ItemHandlers.Torch],                   // Simulation.Lights, Simulation.Fire.Ignite
        [ItemKind.Lighter] = [ItemHandlers.Flame],                 // Simulation.Lights, Simulation.Fire.Ignite
        [ItemKind.Matches] = [ItemHandlers.Flame],
        [ItemKind.FireExtinguisher] = [ItemHandlers.Extinguish],   // Simulation.Fire
        [ItemKind.JerryCan] = [ItemHandlers.JerryCan],             // Simulation.Fire, Simulation.Power
        [ItemKind.Rope] = [ItemHandlers.Rope],                     // Simulation (rappel)
        [ItemKind.Crowbar] = [ItemHandlers.Pry],                   // Simulation.ItemUse, Simulation.Items
        [ItemKind.ToolKit] = [ItemHandlers.ToolKit],               // Simulation.Power
        [ItemKind.SpareParts] = [ItemHandlers.SpareParts],
        [ItemKind.Keycard] = [ItemHandlers.Keycard],               // Simulation.Power
        [ItemKind.ParkMap] = [ItemHandlers.ParkMap],               // Simulation.Knowledge
        [ItemKind.WaterBottle] = [ItemHandlers.Refill],            // Simulation.Ailments (filling)
        [ItemKind.Canteen] = [ItemHandlers.Refill],
        [ItemKind.Poncho] = [ItemHandlers.RainCover],              // Simulation.Ailments
    };

    static readonly int[] DrinkSamples = [0, 1, 7, 99, 100, 250, 330, 333, 500, 1000, 1500];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EachKind_SaysWhatTheSimSays(ItemKind kind)
    {
        ItemDef def = ItemCatalog.Of(kind);
        ItemDefinition item = DefinitionOf(kind);
        string who = $"{IdOf(kind)} (ItemKind.{kind})";

        // ItemCatalog, field by field.
        Assert.Equal(def.Name, item.Name);
        Assert.Equal(def.Plural, item.Plural);
        Assert.Equal(def.Glyph.ToString(), item.Glyph);
        Assert.Equal(Words(def.Categories), item.Categories.OrderBy(c => ItemWords.Categories.ToList().IndexOf(c)).ToList());
        Assert.Equal(Word(def.Size), item.Size);
        Assert.Equal(def.Grams, item.MassG);
        Assert.Equal(def.Ml, item.VolumeMl);
        Assert.Equal(def.MaxAmount, item.MaxAmount);
        Assert.Equal(def.StartAmount, item.StartAmount);
        Assert.Equal(def.Stacks, item.Stacks);
        Assert.Equal(def.MilligramsPerUnit, item.Fill?.MgPerUnit ?? 0);
        Assert.Equal(def.Unit, item.Fill?.Unit);
        Assert.Equal(def.Fills is { } fills ? IdOf(fills) : null, item.Fill?.FilledBy?.Value);
        Assert.Equal(def.Wear == WearSlot.None ? null : def.Wear.ToString().ToLowerInvariant(), item.Wear);
        Assert.Equal(def.Note ?? "", item.Description);
        Assert.Equal(def.Dose, item.Dose);
        Assert.Equal(def.UseTicks, item.UseTicks);
        Assert.Equal(def.Holds is null, item.Holds is null);
        if (def.Holds is { } holds && item.Holds is { } yaml)
        {
            Assert.Equal(holds.CapacityMl, yaml.CapacityMl);
            Assert.Equal(holds.MaxItemMl, yaml.MaxItemMl);
            Assert.Equal(holds.MaxItems, yaml.MaxItems);
            Assert.Equal(Words(holds.Accepts), yaml.Accepts.ToList());
            Assert.Equal(holds.Closes, yaml.Closes);
            Assert.Equal(holds.OpenLoudness, yaml.OpenLoudness);
            Assert.Equal(holds.OpenTicks, yaml.OpenTicks);
            Assert.Equal((holds.Only ?? []).Select(IdOf).ToList(), yaml.Only.Select(id => id.Value).ToList());
        }
        Assert.Equal((def.Comes ?? []).Select(c => (IdOf(c.Kind), c.Count)).ToList(),
            item.ComesWith.Select(c => (c.Item.Value, c.Quantity)).ToList());

        // Food, drink and warmth, from the sim's own tables.
        ItemFood food = item.Food ?? new ItemFood(0, 0, 0);
        Assert.Equal(Nutrition.Food(kind), (food.Kcal, food.WaterMl, food.AlertMinutes));
        if (def.Is(ItemCategory.Potable) && !def.Stacks)
            foreach (int ml in DrinkSamples)
                Assert.True(Nutrition.Drink(kind, ml) == (item.Drink?.For(ml) ?? (0, ml, 0)),
                    $"{who}: drinking {ml} ml gives {Nutrition.Drink(kind, ml)} in the sim and {item.Drink?.For(ml) ?? (0, ml, 0)} in the YAML.");
        else
            Assert.Null(item.Drink);
        Assert.Equal(Simulation.WarmthOf(kind), item.Warmth);

        // Handlers: the use handler, then the kind's specific mechanics, each known and implemented.
        Assert.Equal(UseKey(def), item.Handlers.SingleOrDefault(h => ItemHandlers.UseKeys.Contains(h)));
        Assert.Equal(Specific.TryGetValue(kind, out string[]? keys) ? keys : [],
            item.Handlers.Where(h => !ItemHandlers.UseKeys.Contains(h)).ToArray());
        foreach (string key in item.Handlers)
        {
            Assert.True(SimContent.Handlers.TryGet(key, out HandlerContract? contract), $"{who} names {key}, which SimContent doesn't register.");
            Assert.True(contract!.Implemented, $"{who}'s handler {key} isn't implemented.");
        }
    }

    [Fact]
    public void TheCorePack_HasEveryKind_AndTheyAreAllRuntimeReady()
    {
        ContentRegistry registry = RepoContent.Registry;
        var kindIds = Enum.GetValues<ItemKind>().Select(IdOf).ToList();
        Assert.Equal(91, kindIds.Distinct().Count());
        foreach (string id in kindIds)
            Assert.True(registry.IsRuntimeReady(new ContentId(id)), $"{id} isn't runtime-ready.");
        // The 91 kinds and the four catalog items the slice adds (P1b).
        Assert.Equal(95, registry.All<ItemDefinition>().Count());
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
