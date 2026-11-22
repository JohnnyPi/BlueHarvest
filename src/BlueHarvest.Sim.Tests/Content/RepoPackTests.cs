using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>
/// The game's own packs in the repo's content/packs: they always load, the only thing between them and a runtime
/// load is the handlers P5 adds, the crosswalk agrees with them, and Godot's copy (when a build has made one) is
/// the same bytes.
/// </summary>
public class RepoPackTests
{
    static readonly string Root = FindRoot();
    static readonly string Packs = Path.Combine(Root, "content", "packs");

    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BlueHarvest.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Can't find BlueHarvest.sln above the test folder.");
    }

    static ContentLoadOptions Options(ContentLoadOptions mode) =>
        mode with { GameVersion = SemVersion.TryParse(SimInfo.Version, out SemVersion v) ? v : null };

    static ContentLoadResult LoadRepo(ContentLoadOptions mode) =>
        TestPacks.Load(new FileSystemContentSource(Packs), Options(mode));

    [Fact]
    public void TheRepoPacks_LoadWithNothingButNotes()
    {
        var result = LoadRepo(ContentLoadOptions.Authoring);
        TestPacks.AssertLoads(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity != ContentSeverity.Note);
        ContentRegistry registry = result.Registry!;
        Assert.Equal("core", Assert.Single(registry.Packs).Id);
        foreach (string kind in ContentKinds.All)
            Assert.True(registry.Definitions.Any(d => d.Kind == kind), $"content/packs has no {kind}.");
        // Only the definitions whose handlers arrive in P5 wait.
        Assert.Equal(new[] { "core:DEV-ADM-001", "core:RCP-022", "core:SRC-005" },
            registry.NotRuntimeReady.Select(id => id.Value).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void TheRepoPacks_LoadAtRunTime_OnceTheirHandlersExist()
    {
        var implemented = HandlerRegistry.Default.Contracts.Select(c => c.Key).ToArray();
        var options = ContentLoadOptions.Runtime with { Handlers = HandlerRegistry.Default.WithImplemented(implemented) };
        var result = LoadRepo(options);
        TestPacks.AssertLoads(result);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TheCrosswalk_AndTheSourceIds_Agree()
    {
        // content/tooling/crosswalk.csv: old,new,reason. Every row's new ID exists and lists the old one in source_ids;
        // every source_id in the packs is either the definition's own ID or a row of the crosswalk.
        ContentRegistry registry = LoadRepo(ContentLoadOptions.Authoring).Registry!;
        var rows = File.ReadAllLines(Path.Combine(Root, "content", "tooling", "crosswalk.csv"))
            .Skip(1).Where(line => line.Trim().Length > 0)
            .Select(line => line.Split(',', 3))
            .ToList();
        Assert.NotEmpty(rows);
        foreach (string[] row in rows)
        {
            Assert.True(registry.TryGet<ContentDefinition>(new ContentId(row[1]), out var definition), $"crosswalk: {row[1]} isn't defined.");
            Assert.Contains(row[0], definition!.SourceIds);
        }
        var mapped = rows.Select(row => (Old: row[0], New: row[1])).ToHashSet();
        foreach (ContentDefinition d in registry.Definitions)
            foreach (string old in d.SourceIds)
                Assert.True(d.Id.Local == old || mapped.Contains((old, d.Id.Value)),
                    $"{d.Id} lists {old} in source_ids, but content/tooling/crosswalk.csv has no {old},{d.Id} row.");
    }

    [Fact]
    public void GodotsCopy_IsTheSameBytes_WhenABuildHasMadeIt()
    {
        // The Godot project's build copies content/packs to godot/Content/packs (BlueHarvest.csproj, CopyContentPacks),
        // and a solution build makes it before the tests run. Godot reads the copy; GameContent checks the fingerprint.
        string copy = Path.Combine(Root, "godot", "Content", "packs");
        if (!Directory.Exists(copy))
            return; // only the Sim projects were built: nothing to compare
        static List<string> Files(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        List<string> source = Files(Packs), copied = Files(copy);
        Assert.Equal(source, copied);
        foreach (string file in source)
            Assert.True(File.ReadAllBytes(Path.Combine(Packs, file)).SequenceEqual(File.ReadAllBytes(Path.Combine(copy, file))),
                $"godot/Content/packs/{file} differs from content/packs/{file}: build the Godot project to recopy.");
        Assert.Equal(LoadRepo(ContentLoadOptions.Authoring).Registry!.Fingerprint,
            TestPacks.Load(new FileSystemContentSource(copy), Options(ContentLoadOptions.Authoring)).Registry!.Fingerprint);
    }
}
