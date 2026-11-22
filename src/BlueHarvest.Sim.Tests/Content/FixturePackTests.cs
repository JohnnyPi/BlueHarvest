using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>
/// The packs in Content/Fixtures/packs, read from disk: a small core pack (the vertical slice's item, recipe,
/// source, station, device and note) and a narrative pack exactly as the Narrative Studio v1.1 writes one
/// (made by tools/narrative-studio/make-test-fixture.js): two ready entries, exported as a pack, and a draft,
/// exported on its own.
/// </summary>
public class FixturePackTests
{
    static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Content", "Fixtures", "packs");

    static ContentLoadOptions Options(ContentLoadOptions mode) =>
        mode with { GameVersion = SemVersion.TryParse(SimInfo.Version, out SemVersion v) ? v : null };

    [Fact]
    public void TheFixturePacks_LoadFromDisk()
    {
        var result = TestPacks.Load(new FileSystemContentSource(Folder), Options(ContentLoadOptions.Authoring));
        TestPacks.AssertLoads(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity != ContentSeverity.Note);
        ContentRegistry registry = result.Registry!;

        Assert.Equal(new[] { "core", "blueharvest.narrative" }, registry.Packs.Select(p => p.Id));
        // The Studio lists core as a dependency because the terminal message is bound to core:DEV-ADM-001.
        Assert.Equal(new[] { "core" }, registry.Packs[1].Dependencies.Select(d => d.Id));
        Assert.Equal(14, registry.Count);
        Assert.Equal(
            new[] { "blueharvest.narrative:NAR-TXT-0001", "core:DEV-ADM-001", "core:RCP-022", "core:SRC-005" },
            registry.NotRuntimeReady.Select(id => id.Value).OrderBy(s => s, StringComparer.Ordinal));

        var recipe = registry.Get<RecipeDefinition>("core:RCP-022");
        Assert.Equal("core:multitool", Assert.Single(recipe.Tools).Item.Value);
        Assert.Equal(new[] { "ITM-TOL-001" }, registry.Get<ItemDefinition>("core:multitool").SourceIds);
        Assert.Equal("core:knife", registry.Get<ResourceSourceDefinition>("core:SRC-005").RequiredTool?.Value);

        var note = registry.Get<NarrativeDefinition>("core:MEN-01-N01");
        Assert.Contains("goes here.]\n\nThis is placeholder text", note.Document!.Body);

        var talk = registry.Get<NarrativeDefinition>("blueharvest.narrative:MEN-01-TALK-001");
        Assert.Equal(2, talk.Dialogue!.Nodes.Count);
        Assert.Equal("I don't yet know.\nThey may have seen something we haven't.", talk.Dialogue!.Nodes[1].Text);
        Assert.Equal(new[] { "FACT-MEN-001" }, talk.Dialogue!.Nodes[0].Choices[1].RevealsFacts);

        var terminal = registry.Get<NarrativeDefinition>("blueharvest.narrative:NAR-TERM-0001");
        Assert.Equal(new NarrativeBinding("device", "core:DEV-ADM-001"), terminal.Binding);
        Assert.Equal("WARNING: \"CAMERA\" CLOCK\n\nFrame timestamps preserved.", terminal.Document!.Body);
    }

    [Fact]
    public void DiskAndMemory_GiveTheSameRegistryAndFingerprint()
    {
        var disk = TestPacks.Load(new FileSystemContentSource(Folder), Options(ContentLoadOptions.Authoring));
        var memory = new InMemoryContentSource();
        var files = Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)
            .Select(full => Path.GetRelativePath(Folder, full).Replace('\\', '/'))
            .OrderByDescending(p => p, StringComparer.Ordinal) // the other way round, on purpose
            .ToList();
        foreach (string relative in files)
            memory.Add(relative, File.ReadAllBytes(Path.Combine(Folder, relative)));
        var fromMemory = TestPacks.Load(memory, Options(ContentLoadOptions.Authoring));
        TestPacks.AssertLoads(disk);
        TestPacks.AssertLoads(fromMemory);
        Assert.Equal(disk.Registry!.Fingerprint, fromMemory.Registry!.Fingerprint);
        Assert.Equal(disk.Registry!.Definitions.Select(d => d.Id), fromMemory.Registry!.Definitions.Select(d => d.Id));
        Assert.Equal(disk.Diagnostics, fromMemory.Diagnostics);
    }

    [Fact]
    public void TheFixturePacks_AreNotRuntimeReadyYet()
    {
        // Gathering, processing and devices arrive in P5; until then a runtime load refuses them, and the draft.
        var result = TestPacks.Load(new FileSystemContentSource(Folder), Options(ContentLoadOptions.Runtime));
        Assert.Null(result.Registry);
        var errors = result.Errors.ToList();
        Assert.Equal(4, errors.Count);
        Assert.All(errors, e => Assert.Equal(DiagnosticCodes.NotRuntimeReady, e.Code));
    }

    [Fact]
    public void TheRegistry_CantBeChanged()
    {
        ContentRegistry registry = TestPacks.Load(new FileSystemContentSource(Folder), Options(ContentLoadOptions.Authoring)).Registry!;
        var definitions = (IList<ContentDefinition>)registry.Definitions;
        Assert.True(definitions.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => definitions.Add(definitions[0]));
        var inputs = (IList<ItemQuantity>)registry.Get<RecipeDefinition>("core:RCP-022").Inputs;
        Assert.Throws<NotSupportedException>(() => inputs.Clear());
    }

    [Fact]
    public void AMissingFolder_IsAManifestError()
    {
        var result = TestPacks.Load(new FileSystemContentSource(Path.Combine(Folder, "no-such-folder")));
        TestPacks.AssertError(result, DiagnosticCodes.Manifest);
    }
}
