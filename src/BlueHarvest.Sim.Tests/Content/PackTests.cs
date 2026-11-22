using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>Pack manifests, dependency order, versions and paths.</summary>
public class PackTests
{
    static InMemoryContentSource Packs(params (string Id, string Dependencies, string Extra)[] packs)
    {
        var source = new InMemoryContentSource();
        foreach (var (id, dependencies, extra) in packs)
            source.Add($"{id}/pack.yaml", TestPacks.Manifest(id, dependencies, extra));
        return source;
    }

    static string Order(ContentLoadResult result) => string.Join(" ", result.Registry!.Packs.Select(p => p.Id));

    [Fact]
    public void Packs_LoadAfterTheirDependencies_ThenByPriorityAndId()
    {
        var result = TestPacks.Load(Packs(
            ("zeta", "[]", ""),
            ("alpha", "[zeta]", ""),
            ("beta", "[]", "priority: 5\n"),
            ("gamma", "[]", "priority: -5\n"),
            ("delta", "[{id: alpha, version: \">=1.0.0 <2.0.0\"}]", "")));
        TestPacks.AssertLoads(result);
        Assert.Equal("gamma zeta alpha delta beta", Order(result));
    }

    [Fact]
    public void TheOrder_DoesntDependOnTheSource()
    {
        var forward = Packs(("core", "[]", ""), ("one", "[core]", ""), ("two", "[core]", ""));
        var backward = new InMemoryContentSource();
        foreach (ContentFile file in forward.Enumerate().Reverse())
            backward.Add(file.Path, file.ReadAllBytes());
        Assert.Equal(Order(TestPacks.Load(forward)), Order(TestPacks.Load(backward)));
    }

    [Fact]
    public void AMissingDependency_LeavesThePackAndItsDependentsOut()
    {
        var result = TestPacks.Load(Packs(("core", "[]", ""), ("one", "[ghost]", ""), ("two", "[one]", "")));
        Assert.Contains("ghost", TestPacks.AssertError(result, DiagnosticCodes.MissingDependency).Message);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCodes.MissingDependency));
    }

    [Fact]
    public void ADependencyCycle_IsReported()
    {
        var result = TestPacks.Load(Packs(("core", "[]", ""), ("one", "[two]", ""), ("two", "[one]", "")));
        TestPacks.AssertError(result, DiagnosticCodes.DependencyCycle);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCodes.DependencyCycle));
    }

    [Fact]
    public void APackCantDependOnItself()
    {
        TestPacks.AssertError(TestPacks.Load(Packs(("core", "[core]", ""))), DiagnosticCodes.DependencyCycle);
    }

    [Fact]
    public void DependencyVersions_AreChecked()
    {
        var result = TestPacks.Load(Packs(("core", "[]", ""), ("one", "[{id: core, version: \">=2.0.0\"}]", "")));
        Assert.Contains("core 1.0.0", TestPacks.AssertError(result, DiagnosticCodes.DependencyVersion).Message);
    }

    [Fact]
    public void RequiresGame_IsCheckedAgainstTheGameVersion()
    {
        var source = Packs(("core", "[]", "requires_game: \">=0.1.0\"\n"));
        TestPacks.AssertLoads(TestPacks.Load(source)); // no game version given: not checked
        var old = ContentLoadOptions.Authoring with { GameVersion = new SemVersion(0, 0, 1, "") };
        TestPacks.AssertError(TestPacks.Load(source, old), DiagnosticCodes.GameVersion);
        var current = ContentLoadOptions.Authoring with { GameVersion = new SemVersion(0, 1, 0, "") };
        TestPacks.AssertLoads(TestPacks.Load(source, current));
    }

    [Fact]
    public void DuplicatePackIds_AreRejected()
    {
        var source = new InMemoryContentSource()
            .Add("a/pack.yaml", TestPacks.Manifest("core"))
            .Add("b/pack.yaml", TestPacks.Manifest("core"));
        Assert.Contains("a/pack.yaml", TestPacks.AssertError(TestPacks.Load(source), DiagnosticCodes.DuplicatePack).Message);
    }

    [Theory]
    [InlineData("schema: blueharvest.pack/v2\nid: core\nversion: 1.0.0\ncontent_roots: [items]\n", DiagnosticCodes.Manifest)]
    [InlineData("schema: blueharvest.pack/v1\nid: Core\nversion: 1.0.0\ncontent_roots: [items]\n", DiagnosticCodes.BadValue)]
    [InlineData("schema: blueharvest.pack/v1\nid: core\nversion: 1.0\ncontent_roots: [items]\n", DiagnosticCodes.BadValue)]
    [InlineData("schema: blueharvest.pack/v1\nid: core\nversion: 1.0.0\n", DiagnosticCodes.MissingField)]
    [InlineData("schema: blueharvest.pack/v1\nid: core\nversion: 1.0.0\ncontent_roots: [../outside]\n", DiagnosticCodes.BadValue)]
    [InlineData("schema: blueharvest.pack/v1\nid: core\nversion: 1.0.0\ncontent_roots: [/items]\n", DiagnosticCodes.BadValue)]
    [InlineData("schema: blueharvest.pack/v1\nid: core\nversion: 1.0.0\ncontent_roots: [items]\nrequires_game: soon\n", DiagnosticCodes.BadValue)]
    [InlineData("schema: blueharvest.pack/v1\nid: core\nversion: 1.0.0\ncontent_roots: [items]\nscript_roots: [scripts]\n", DiagnosticCodes.UnknownField)]
    public void BadManifests_AreRejected(string manifest, string code)
    {
        TestPacks.AssertError(TestPacks.Load(new InMemoryContentSource().Add("core/pack.yaml", manifest)), code);
    }

    [Fact]
    public void NoManifest_IsAnError()
    {
        var source = new InMemoryContentSource("somewhere").Add("items/x.yaml", TestPacks.Item("ITM-CRF-004"));
        Assert.Contains("somewhere", TestPacks.AssertError(TestPacks.Load(source), DiagnosticCodes.Manifest).Message);
    }

    [Fact]
    public void ASourceCanBeASinglePack()
    {
        var source = new InMemoryContentSource()
            .Add("pack.yaml", TestPacks.CoreManifest)
            .Add("items/vine-fiber.yaml", TestPacks.Item("ITM-CRF-004"));
        var result = TestPacks.Load(source);
        TestPacks.AssertLoads(result);
        Assert.Equal("", result.Registry!.Packs[0].Root);
        Assert.True(result.Registry!.Contains(new ContentId("core:ITM-CRF-004")));
    }

    [Fact]
    public void FilesOutsideTheContentRoots_AreWarnedAboutAndSkipped()
    {
        var source = TestPacks.Core(("notes/todo.yaml", TestPacks.Item("ITM-CRF-004")), ("items/old.yml", TestPacks.Item("ITM-CRF-005")),
            ("items/README.md", "not content"));
        source.Add("loose.yaml", "anything: at all\n");
        var result = TestPacks.Load(source);
        TestPacks.AssertLoads(result);
        Assert.Equal(0, result.Registry!.Count);
        Assert.Equal(3, result.Diagnostics.Count(d => d.Code == DiagnosticCodes.ContentPath && d.Severity == ContentSeverity.Warning));
    }

    [Theory]
    [InlineData("core/../escape.yaml")]
    [InlineData("/core/items/x.yaml")]
    [InlineData("core\\items\\x.yaml")]
    [InlineData("core//items/x.yaml")]
    [InlineData("C:/core/items/x.yaml")]
    public void UnsafePaths_AreRefused(string path)
    {
        var source = TestPacks.Core();
        source.Add(path, TestPacks.Item("ITM-CRF-004"));
        TestPacks.AssertError(TestPacks.Load(source), DiagnosticCodes.ContentPath);
    }

    [Fact]
    public void StudioManifests_AreAccepted()
    {
        // As the Narrative Studio exports it: every scalar quoted.
        string manifest = "schema: \"blueharvest.pack/v1\"\nid: \"blueharvest.narrative\"\nversion: \"0.1.0\"\n" +
            "requires_game: \">=0.1.0\"\ndependencies: []\npriority: 0\ncontent_roots:\n  - \"narrative\"\n";
        var result = TestPacks.Load(new InMemoryContentSource().Add("narrative/pack.yaml", manifest));
        TestPacks.AssertLoads(result);
        PackManifest pack = Assert.Single(result.Registry!.Packs);
        Assert.Equal("blueharvest.narrative", pack.Id);
        Assert.Equal(new SemVersion(0, 1, 0, ""), pack.Version);
        Assert.Equal(">=0.1.0", pack.RequiresGame?.Text);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0-rc1", "1.0.0", -1)]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    public void Versions_CompareBySemver(string a, string b, int expected)
    {
        Assert.True(SemVersion.TryParse(a, out SemVersion va));
        Assert.True(SemVersion.TryParse(b, out SemVersion vb));
        Assert.Equal(expected, Math.Sign(va.CompareTo(vb)));
    }
}
