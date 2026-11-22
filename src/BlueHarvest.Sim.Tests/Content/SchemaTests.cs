using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>Each kind's fields: required ones, types, ranges, unknown keys, and the typed result.</summary>
public class SchemaTests
{
    [Fact]
    public void TheSliceGraph_LoadsIntoTypedDefinitions()
    {
        var result = TestPacks.LoadCore(TestPacks.Slice());
        TestPacks.AssertLoads(result);
        ContentRegistry registry = result.Registry!;
        Assert.Equal(4, registry.Count);

        var recipe = registry.Get<RecipeDefinition>("core:RCP-022");
        Assert.Equal("process.transform", recipe.Handler);
        Assert.Equal("core:STN-HAND", recipe.Station.Value);
        Assert.Equal(140 * ContentTime.TicksPerActionSecond, recipe.DurationTicks);
        Assert.Equal(new ItemQuantity(new ContentId("core:ITM-CRF-004"), 3), Assert.Single(recipe.Inputs));
        Assert.Equal(new ItemQuantity(new ContentId("core:ITM-MAT-006"), 1), Assert.Single(recipe.Outputs));
        Assert.Equal("none", recipe.Heat);
        Assert.Equal("core", recipe.PackId);
        Assert.Equal("core/recipes/twist-vine-cordage.yaml", recipe.SourcePath);

        Assert.True(registry.TryGet<ItemDefinition>(new ContentId("core:ITM-CRF-004"), out var fiber));
        Assert.Equal(100, fiber!.MassG);
        Assert.False(registry.TryGet<RecipeDefinition>(new ContentId("core:ITM-CRF-004"), out _)); // an item, not a recipe
        Assert.Throws<KeyNotFoundException>(() => registry.Get<ItemDefinition>("core:ITM-CRF-999"));
        Assert.Equal(2, registry.All<ItemDefinition>().Count());
    }

    [Fact]
    public void EveryKind_ReadsItsFields()
    {
        var files = TestPacks.Slice().With(
            ("items/knife.yaml", TestPacks.Item("knife", "stacking: unique\nsource_ids: [ITM-MEL-002]\n")),
            ("items/branch.yaml", TestPacks.Item("ITM-CRF-005", "stack_limit: 20\nstacking: stack\ntags: [natural, wood]\n")),
            ("items/regulator.yaml", TestPacks.Item("ITM-ADM-001")),
            ("sources/vine-thicket.yaml", """
                schema: blueharvest.content/v1
                kind: resource_source
                id: SRC-005
                name: Vine Thicket
                handler: resource.harvest
                required_tool: knife
                renewal: {mode: world_event, event: ecology.plant_regrowth, max_batches: 3}
                yields:
                  - {item: ITM-CRF-004, min_quantity: 2, max_quantity: 5}
                  - {item: ITM-CRF-005, min_quantity: 0, max_quantity: 1}
                """),
            ("devices/controller.yaml", """
                schema: blueharvest.content/v1
                kind: device
                id: DEV-ADM-001
                name: Main Pump Power Controller
                handler: device.install_and_activate
                required_parts: [{item: ITM-ADM-001, quantity: 1}]
                requirements: {power: available}
                states: [offline, ready, active]
                facts_when_active:
                  - predicate: lit
                    args: [self]
                """));
        var result = TestPacks.LoadCore(files);
        TestPacks.AssertLoads(result);
        ContentRegistry registry = result.Registry!;

        var knife = registry.Get<ItemDefinition>("core:knife");
        Assert.Equal("unique", knife.Stacking);
        Assert.Equal(1, knife.StackLimit);
        Assert.Equal(new[] { "ITM-MEL-002" }, knife.SourceIds);
        var branch = registry.Get<ItemDefinition>("core:ITM-CRF-005");
        Assert.Equal(20, branch.StackLimit);
        Assert.Equal(new[] { "natural", "wood" }, branch.Tags);

        var source = registry.Get<ResourceSourceDefinition>("core:SRC-005");
        Assert.Equal("core:knife", source.RequiredTool?.Value);
        Assert.Equal("world_event", source.RenewalMode);
        Assert.Equal("ecology.plant_regrowth", source.RenewalEvent);
        Assert.Equal(3, source.MaxBatches);
        Assert.Equal(new YieldRange(new ContentId("core:ITM-CRF-005"), 0, 1), source.Yields[1]);

        var device = registry.Get<DeviceDefinition>("core:DEV-ADM-001");
        Assert.Equal("available", device.Power);
        Assert.Equal(new[] { "offline", "ready", "active" }, device.States);
        var fact = Assert.Single(device.FactsWhenActive);
        Assert.Equal("lit", fact.Predicate);
        Assert.Equal(new[] { "self" }, fact.Args);
    }

    [Fact]
    public void UnknownFields_AreRejected_WithAHint()
    {
        string recipe = TestPacks.Recipe("RCP-022").Replace("duration_action_seconds", "duration_seconds");
        var result = TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe)));
        ContentDiagnostic d = TestPacks.AssertError(result, DiagnosticCodes.UnknownField);
        Assert.Contains("duration_seconds", d.Message);
        Assert.Contains("duration_action_seconds or duration_game_minutes", d.Message);
        Assert.Equal("core:RCP-022", d.DefinitionId);
    }

    [Fact]
    public void UnknownNestedFields_AreRejected()
    {
        string recipe = TestPacks.Recipe("RCP-022", extra: "requirements:\n  heat: none\n  magic: plenty\n");
        var result = TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe)));
        Assert.Contains("requirements.magic", TestPacks.AssertError(result, DiagnosticCodes.UnknownField).Message);
    }

    [Fact]
    public void MissingRequiredFields_AreReported()
    {
        string item = "schema: blueharvest.content/v1\nkind: item\nid: ITM-CRF-004\nname: Vine Fiber\ncategory: material\n";
        ContentDiagnostic d = TestPacks.AssertError(TestPacks.LoadCore(("items/x.yaml", item)), DiagnosticCodes.MissingField);
        Assert.Contains("mass_g", d.Message);
    }

    [Theory]
    [InlineData("mass_g: \"150\"")]   // numbers are never quoted
    [InlineData("mass_g: -1")]
    [InlineData("mass_g: 1.5")]
    [InlineData("mass_g: 0x10")]
    [InlineData("stack_limit: 0")]
    [InlineData("stacking: pile")]
    [InlineData("tags: natural")]     // a list, not a word
    [InlineData("tags: [natural, natural]")]
    public void BadValues_AreRejected(string line)
    {
        string key = line[..line.IndexOf(':')];
        string item = "schema: blueharvest.content/v1\nkind: item\nid: ITM-CRF-004\nname: Vine Fiber\ncategory: material\n" +
            (key == "mass_g" ? "" : "mass_g: 150\n") + line + "\n";
        TestPacks.AssertError(TestPacks.LoadCore(("items/x.yaml", item)), DiagnosticCodes.BadValue);
    }

    [Fact]
    public void BadCategory_IsRejected()
    {
        string item = TestPacks.Item("ITM-CRF-004").Replace("category: material", "category: Raw Material");
        TestPacks.AssertError(TestPacks.LoadCore(("items/x.yaml", item)), DiagnosticCodes.BadValue);
    }

    [Fact]
    public void NothingIsTypedImplicitly()
    {
        // YAML 1.1 would make these a date and a boolean; content keeps them as the text written.
        string item = "schema: blueharvest.content/v1\nkind: item\nid: ITM-CRF-004\nname: 2026-11-22\ndescription: yes\ncategory: material\nmass_g: 150\n";
        var result = TestPacks.LoadCore(("items/x.yaml", item));
        TestPacks.AssertLoads(result);
        var fiber = result.Registry!.Get<ItemDefinition>("core:ITM-CRF-004");
        Assert.Equal("2026-11-22", fiber.Name);
        Assert.Equal("yes", fiber.Description);
    }

    [Fact]
    public void AUniqueItem_CantStack()
    {
        string item = TestPacks.Item("knife", "stacking: unique\nstack_limit: 5\n");
        Assert.Contains("stack_limit must be 1", TestPacks.AssertError(TestPacks.LoadCore(("items/x.yaml", item)), DiagnosticCodes.BadValue).Message);
    }

    [Fact]
    public void Durations_InSecondsOrGameMinutes_ConvertToTheSameTicks()
    {
        string seconds = TestPacks.Recipe("RCP-001").Replace("duration_action_seconds: 140", "duration_action_seconds: 60");
        string minutes = TestPacks.Recipe("RCP-002").Replace("duration_action_seconds: 140", "duration_game_minutes: 10");
        var result = TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/a.yaml", seconds), ("recipes/b.yaml", minutes)));
        TestPacks.AssertLoads(result);
        long a = result.Registry!.Get<RecipeDefinition>("core:RCP-001").DurationTicks;
        long b = result.Registry!.Get<RecipeDefinition>("core:RCP-002").DurationTicks;
        Assert.Equal(6000, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Durations_NeedExactlyOneUnit()
    {
        string both = TestPacks.Recipe("RCP-022", extra: "duration_game_minutes: 10\n");
        string neither = TestPacks.Recipe("RCP-022").Replace("duration_action_seconds: 140\n", "");
        TestPacks.AssertError(TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", both))), DiagnosticCodes.BadValue);
        TestPacks.AssertError(TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", neither))), DiagnosticCodes.MissingField);
    }

    [Fact]
    public void ARecipe_ThatMakesNothing_IsRejected()
    {
        string recipe = TestPacks.Recipe("RCP-022").Replace("outputs:\n  - item: ITM-MAT-006\n    quantity: 1\n", "");
        Assert.Contains("no outputs and no targets",
            TestPacks.AssertError(TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe))), DiagnosticCodes.MissingField).Message);
    }

    [Fact]
    public void TheSameItemTwiceInOneList_IsRejected()
    {
        string recipe = TestPacks.Recipe("RCP-022").Replace(
            "inputs:\n  - item: ITM-CRF-004\n    quantity: 3\n",
            "inputs:\n  - item: ITM-CRF-004\n    quantity: 3\n  - item: core:ITM-CRF-004\n    quantity: 1\n");
        Assert.Contains("twice", TestPacks.AssertError(TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe))), DiagnosticCodes.BadValue).Message);
    }

    [Fact]
    public void ASourceNeedsAnEventOnlyWhenItRenews()
    {
        string Source(string renewal) => $$"""
            schema: blueharvest.content/v1
            kind: resource_source
            id: SRC-005
            name: Vine Thicket
            handler: resource.harvest
            renewal: {{renewal}}
            yields: [{item: ITM-CRF-004, min_quantity: 2, max_quantity: 5}]
            """;
        var items = new[] { ("items/vine-fiber.yaml", TestPacks.Item("ITM-CRF-004")) };
        TestPacks.AssertLoads(TestPacks.LoadCore(items.With(("sources/s.yaml", Source("{mode: finite, max_batches: 1}")))));
        TestPacks.AssertError(TestPacks.LoadCore(items.With(("sources/s.yaml", Source("{mode: world_event, max_batches: 3}")))), DiagnosticCodes.MissingField);
        TestPacks.AssertError(TestPacks.LoadCore(items.With(("sources/s.yaml", Source("{mode: finite, event: ecology.plant_regrowth, max_batches: 1}")))), DiagnosticCodes.BadValue);
        TestPacks.AssertError(TestPacks.LoadCore(items.With(("sources/s.yaml",
            Source("{mode: finite, max_batches: 1}").Replace("min_quantity: 2", "min_quantity: 9")))), DiagnosticCodes.BadValue);
    }

    [Theory]
    [InlineData("schema: blueharvest.content/v2\nkind: item\nid: ITM-CRF-004\n", DiagnosticCodes.Envelope)]
    [InlineData("schema: blueharvest.content/v1\nkind: weapon\nid: ITM-CRF-004\n", DiagnosticCodes.Envelope)]
    [InlineData("schema: blueharvest.pack/v1\nid: core\n", DiagnosticCodes.Envelope)]
    [InlineData("- a list\n- not a mapping\n", DiagnosticCodes.Envelope)]
    [InlineData("schema: blueharvest.content/v1\nkind: item\nid: Vine Fiber\n", DiagnosticCodes.BadId)]
    [InlineData("schema: blueharvest.content/v1\nid: ITM-CRF-004\n", DiagnosticCodes.MissingField)]
    public void TheEnvelope_IsChecked(string yaml, string code)
    {
        TestPacks.AssertError(TestPacks.LoadCore(("items/x.yaml", yaml)), code);
    }
}
