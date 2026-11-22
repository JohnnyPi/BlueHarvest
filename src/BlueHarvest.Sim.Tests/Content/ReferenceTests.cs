using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>Identity across files and packs: duplicates, references, namespaces and explicit replacement.</summary>
public class ReferenceTests
{
    [Fact]
    public void DuplicateIds_AreRejected_NamingBothFiles()
    {
        var result = TestPacks.LoadCore(
            ("items/a.yaml", TestPacks.Item("ITM-CRF-004")),
            ("items/b.yaml", TestPacks.Item("core:ITM-CRF-004"))); // qualified or not, the same ID
        ContentDiagnostic d = TestPacks.AssertError(result, DiagnosticCodes.DuplicateId);
        Assert.Equal("core/items/b.yaml", d.File);
        Assert.Contains("core/items/a.yaml", d.Message);
    }

    [Fact]
    public void ABrokenReference_IsReported_WithItsFieldAndLine()
    {
        var files = TestPacks.Slice().Where(f => f.Path != "items/vine-fiber.yaml").ToArray();
        ContentDiagnostic d = TestPacks.AssertError(TestPacks.LoadCore(files), DiagnosticCodes.UnresolvedReference);
        Assert.Equal("core/recipes/twist-vine-cordage.yaml", d.File);
        Assert.Equal(9, d.Line);
        Assert.Equal("core:RCP-022 references unregistered item core:ITM-CRF-004 (inputs[0].item).", d.Message);
        Assert.StartsWith("BH-CONTENT-014 ERROR core/recipes/twist-vine-cordage.yaml:9:", d.ToString());
    }

    [Fact]
    public void AReferenceToTheWrongKind_IsRejected()
    {
        string recipe = TestPacks.Recipe("RCP-022").Replace("station: STN-HAND", "station: ITM-CRF-004");
        var files = TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe));
        Assert.Equal("core:RCP-022 needs a station at station, but core:ITM-CRF-004 is an item.",
            TestPacks.AssertError(TestPacks.LoadCore(files), DiagnosticCodes.WrongKind).Message);
    }

    [Fact]
    public void AReferenceThatIsntAnId_IsRejected()
    {
        string recipe = TestPacks.Recipe("RCP-022", input: "Vine Fiber");
        TestPacks.AssertError(TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe))), DiagnosticCodes.BadId);
    }

    [Fact]
    public void CrossPackReferences_NeedADeclaredDependency()
    {
        string recipe = TestPacks.Recipe("RCP-100", input: "core:ITM-CRF-004", output: "core:ITM-MAT-006")
            .Replace("station: STN-HAND", "station: core:STN-HAND");
        InMemoryContentSource Source(string dependencies) => TestPacks.Core(TestPacks.Slice())
            .Add("extra/pack.yaml", TestPacks.Manifest("extra", dependencies))
            .Add("extra/recipes/r.yaml", recipe);

        ContentDiagnostic d = TestPacks.AssertError(TestPacks.Load(Source("[]")), DiagnosticCodes.UndeclaredDependency);
        Assert.Contains("extra:RCP-100", d.Message);
        var result = TestPacks.Load(Source("[core]"));
        TestPacks.AssertLoads(result);
        Assert.Equal("core:STN-HAND", result.Registry!.Get<RecipeDefinition>("extra:RCP-100").Station.Value);
    }

    [Fact]
    public void ABareReference_MeansThisPack()
    {
        // In pack extra, a bare ITM-CRF-004 is extra:ITM-CRF-004, which doesn't exist, even though core has one.
        var source = TestPacks.Core(TestPacks.Slice())
            .Add("extra/pack.yaml", TestPacks.Manifest("extra", "[core]"))
            .Add("extra/recipes/r.yaml", TestPacks.Recipe("RCP-100", output: "core:ITM-MAT-006").Replace("station: STN-HAND", "station: core:STN-HAND"));
        Assert.Contains("extra:ITM-CRF-004", TestPacks.AssertError(TestPacks.Load(source), DiagnosticCodes.UnresolvedReference).Message);
    }

    [Fact]
    public void ADefinitionInAnotherPacksNamespace_NeedsReplaces()
    {
        var source = TestPacks.Core(TestPacks.Slice())
            .Add("extra/pack.yaml", TestPacks.Manifest("extra", "[core]"))
            .Add("extra/items/fiber.yaml", TestPacks.Item("core:ITM-CRF-004"));
        Assert.Contains("replaces", TestPacks.AssertError(TestPacks.Load(source), DiagnosticCodes.ForeignNamespace).Message);
    }

    [Fact]
    public void Replaces_SwapsInTheReplacement()
    {
        var source = TestPacks.Core(TestPacks.Slice())
            .Add("extra/pack.yaml", TestPacks.Manifest("extra", "[core]", "replaces:\n  - core:ITM-CRF-004\n"))
            .Add("extra/items/fiber.yaml", TestPacks.Item("core:ITM-CRF-004").Replace("mass_g: 100", "mass_g: 175"));
        var result = TestPacks.Load(source);
        TestPacks.AssertLoads(result);
        var fiber = result.Registry!.Get<ItemDefinition>("core:ITM-CRF-004");
        Assert.Equal(175, fiber.MassG);
        Assert.Equal("extra", fiber.PackId);
        Assert.Equal(4, result.Registry!.Count);
    }

    [Fact]
    public void Replaces_IsChecked()
    {
        InMemoryContentSource Source(string dependencies, string replaces, string item) => TestPacks.Core(TestPacks.Slice())
            .Add("extra/pack.yaml", TestPacks.Manifest("extra", dependencies, $"replaces:\n  - {replaces}\n"))
            .Add("extra/items/x.yaml", item);
        string fiber = TestPacks.Item("core:ITM-CRF-004");

        // Not a dependency of the replacing pack.
        TestPacks.AssertError(TestPacks.Load(Source("[]", "core:ITM-CRF-004", fiber)), DiagnosticCodes.Replace);
        // Listed, but nothing in the pack defines it.
        TestPacks.AssertError(TestPacks.Load(Source("[core]", "core:ITM-CRF-004", TestPacks.Item("ITM-NEW-001"))), DiagnosticCodes.Replace);
        // Nothing to replace.
        TestPacks.AssertError(TestPacks.Load(Source("[core]", "core:ITM-CRF-999", TestPacks.Item("core:ITM-CRF-999"))), DiagnosticCodes.Replace);
        // A different kind.
        string station = TestPacks.Hand.Replace("id: STN-HAND", "id: core:ITM-CRF-004");
        TestPacks.AssertError(TestPacks.Load(Source("[core]", "core:ITM-CRF-004", station)), DiagnosticCodes.Replace);
        // Its own namespace.
        TestPacks.AssertError(TestPacks.Load(Source("[core]", "extra:ITM-NEW-001", TestPacks.Item("ITM-NEW-001"))), DiagnosticCodes.Replace);
    }

    [Fact]
    public void OnlyOnePack_CanReplaceADefinition()
    {
        var source = TestPacks.Core(TestPacks.Slice());
        foreach (string pack in new[] { "one", "two" })
            source.Add($"{pack}/pack.yaml", TestPacks.Manifest(pack, "[core]", "replaces:\n  - core:ITM-CRF-004\n"))
                .Add($"{pack}/items/fiber.yaml", TestPacks.Item("core:ITM-CRF-004"));
        Assert.Contains("already replaced by pack one", TestPacks.AssertError(TestPacks.Load(source), DiagnosticCodes.Replace).Message);
    }
}
