using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>The item fields P2a adds: the sim catalog's fields, and the checks that keep them consistent.</summary>
public class ItemSchemaTests
{
    static ContentLoadResult LoadItems(params (string Path, string Yaml)[] files) => TestPacks.LoadCore(files);

    const string Battery = """
        schema: blueharvest.content/v1
        kind: item
        id: battery-d
        name: "D battery"
        plural: "D batteries"
        glyph: "("
        categories: [power]
        size: pocket
        mass_g: 140
        volume_ml: 60
        fill: {capacity: 100, start: 100, unit: "%"}
        """;

    [Fact]
    public void EveryItemField_IsRead()
    {
        const string flashlight = """
            schema: blueharvest.content/v1
            kind: item
            id: flashlight
            name: flashlight
            plural: flashlights
            glyph: "!"
            description: Takes two D batteries.
            categories: [light, usable, container]
            size: pocket
            mass_g: 400
            volume_ml: 400
            dose: 2
            use_ticks: 300
            holds:
              capacity_ml: 120
              max_items: 2
              accepts: [power]
              closes: true
              open_loudness: 3
              open_ticks: 40
              only: [battery-d]
            comes_with:
              - item: battery-d
                quantity: 2
            handlers: [light.flashlight]
            """;
        var result = LoadItems(("items/battery-d.yaml", Battery), ("items/flashlight.yaml", flashlight));
        TestPacks.AssertLoads(result);
        var light = result.Registry!.Get<ItemDefinition>("core:flashlight");
        Assert.Equal("flashlights", light.Plural);
        Assert.Equal("!", light.Glyph);
        Assert.Equal(new[] { "light", "usable", "container" }, light.Categories);
        Assert.True(light.Is("container"));
        Assert.Equal("pocket", light.Size);
        Assert.Equal((400, 400, 2, 300), (light.MassG, light.VolumeMl, light.Dose, light.UseTicks));
        Assert.Equal((1, 1, false), (light.MaxAmount, light.StartAmount, light.Stacks));
        ItemContainer holds = light.Holds!;
        Assert.Equal((120, int.MaxValue, 2, true, 3, 40), (holds.CapacityMl, holds.MaxItemMl, holds.MaxItems, holds.Closes, holds.OpenLoudness, holds.OpenTicks));
        Assert.Equal(new[] { "power" }, holds.Accepts);
        Assert.Equal(new[] { new ContentId("core:battery-d") }, holds.Only);
        Assert.Equal(new ItemQuantity(new ContentId("core:battery-d"), 2), Assert.Single(light.ComesWith));

        var battery = result.Registry!.Get<ItemDefinition>("core:battery-d");
        Assert.Equal((100, 100, "%", 0, (ContentId?)null), (battery.MaxAmount, battery.StartAmount, battery.Fill!.Unit, battery.Fill.MgPerUnit, battery.Fill.FilledBy));
        Assert.Null(battery.Wear);
        Assert.Null(battery.Holds);
    }

    [Fact]
    public void FoodDrinkAndWarmth_AreRead()
    {
        string soda = TestPacks.Item("soda", "fill: {capacity: 330, start: 330, unit: ml, mg_per_unit: 1000}\ndose: 330\n" +
            "drink: {per_ml: 330, kcal: 140, water_ml: 297, alert_minutes: 30}\n").Replace("categories: [part]", "categories: [potable]");
        string bar = TestPacks.Item("energy-bar", "stack_limit: 12\nfood: {kcal: 250}\n").Replace("categories: [part]", "categories: [edible]");
        string vest = TestPacks.Item("vest", "wear: body\nwarmth: 20\n").Replace("categories: [part]", "categories: [wearable]");
        var result = LoadItems(("items/soda.yaml", soda), ("items/bar.yaml", bar), ("items/vest.yaml", vest));
        TestPacks.AssertLoads(result);
        ContentRegistry registry = result.Registry!;
        ItemDrink drink = registry.Get<ItemDefinition>("core:soda").Drink!;
        Assert.Equal((140, 297, 30), drink.For(330));
        Assert.Equal((106, 225, 22), drink.For(250)); // rounded down, as the sim does
        Assert.Equal(new ItemFood(250, 0, 0), registry.Get<ItemDefinition>("core:energy-bar").Food);
        Assert.Equal((12, 1, true), (registry.Get<ItemDefinition>("core:energy-bar").MaxAmount,
            registry.Get<ItemDefinition>("core:energy-bar").StartAmount, registry.Get<ItemDefinition>("core:energy-bar").Stacks));
        var worn = registry.Get<ItemDefinition>("core:vest");
        Assert.Equal(("body", 20), (worn.Wear, worn.Warmth));
    }

    [Theory]
    [InlineData("[part]", "stack_limit: 5\nfill: {capacity: 5, start: 5, unit: uses}\n", DiagnosticCodes.BadValue)]   // a stack has no fill
    [InlineData("[part]", "fill: {capacity: 5, start: 6, unit: uses}\n", DiagnosticCodes.BadValue)]                   // fuller than full
    [InlineData("[part]", "fill: {capacity: 5, start: 5}\n", DiagnosticCodes.MissingField)]                            // a fill needs its unit
    [InlineData("[part, container]", "", DiagnosticCodes.MissingField)]                                               // a container needs holds
    [InlineData("[part]", "holds: {capacity_ml: 100}\n", DiagnosticCodes.BadValue)]                                    // holds needs container
    [InlineData("[part]", "wear: head\n", DiagnosticCodes.BadValue)]                                                    // worn needs wearable
    [InlineData("[part]", "warmth: 5\n", DiagnosticCodes.BadValue)]
    [InlineData("[part]", "food: {kcal: 100}\n", DiagnosticCodes.BadValue)]                                             // food needs edible
    [InlineData("[part]", "drink: {per_ml: 1, water_ml: 1}\n", DiagnosticCodes.BadValue)]                               // drink needs potable
    [InlineData("[part]", "glyph: \"!!\"\n", DiagnosticCodes.BadValue)]
    [InlineData("[part, magic]", "", DiagnosticCodes.BadValue)]
    [InlineData("[part]", "size: huge\n", DiagnosticCodes.BadValue)]
    [InlineData("[part]", "wear: tail\n", DiagnosticCodes.BadValue)]
    [InlineData("[part, container]", "holds: {capacity_ml: 100, only: [battery-d]}\ncomes_with: [{item: knife, quantity: 1}]\n", DiagnosticCodes.BadValue)]
    public void ItemRules_AreChecked(string categories, string extra, string code)
    {
        string knife = TestPacks.Item("knife");
        string item = TestPacks.Item("thing", extra).Replace("categories: [part]", "categories: " + categories);
        if (extra.StartsWith("size:"))
            item = item.Replace("size: hand\n", "");
        TestPacks.AssertError(LoadItems(("items/battery-d.yaml", Battery), ("items/knife.yaml", knife), ("items/thing.yaml", item)), code);
    }
}
