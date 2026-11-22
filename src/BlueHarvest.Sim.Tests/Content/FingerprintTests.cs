using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>The fingerprint follows meaning only: layout and formatting never change it.</summary>
public class FingerprintTests
{
    static string Fingerprint(IContentSource source, ContentLoadOptions? options = null)
    {
        var result = TestPacks.Load(source, options);
        TestPacks.AssertLoads(result);
        return result.Registry!.Fingerprint;
    }

    static string SliceFingerprint() => Fingerprint(TestPacks.Core(TestPacks.Slice()));

    [Fact]
    public void TheFingerprint_IsSha256Hex_AndRepeatable()
    {
        string a = SliceFingerprint(), b = SliceFingerprint();
        Assert.Equal(a, b);
        Assert.Matches("^sha256:[0-9a-f]{64}$", a);
    }

    [Fact]
    public void TheFingerprint_IgnoresLayoutAndFormatting()
    {
        // The same four definitions: other folders and file names, files in another order, a different manifest
        // layout, keys reordered, quoting, comments, CRLF, flow mappings, qualified IDs, defaults written out.
        const string manifest = """
            # the same pack, laid out differently
            id: core
            schema: "blueharvest.pack/v1"
            name: Core
            version: '1.0.0'
            priority: 3
            content_roots:
              - defs
            """;
        const string fiber = """
            kind: item
            schema: blueharvest.content/v1
            id: core:ITM-CRF-004
            name: "Item ITM-CRF-004"
            mass_g: 100
            categories: [part]   # a comment
            size: hand
            volume_ml: 100
            stack_limit: 1
            stacking: unique
            tags: []
            """;
        const string spool = """
            {schema: blueharvest.content/v1, kind: item, id: ITM-MAT-006, name: Item ITM-MAT-006, categories: [part], size: hand, mass_g: 100, volume_ml: 100}
            """;
        const string hand = """
            name: By Hand
            id: STN-HAND
            kind: station
            description: ""
            schema: blueharvest.content/v1
            """;
        const string recipe = """
            id: core:RCP-022
            kind: recipe
            schema: "blueharvest.content/v1"
            name: 'Recipe RCP-022'
            station: core:STN-HAND
            handler: process.transform
            duration_action_seconds: 140
            outputs:
            - quantity: 1
              item: core:ITM-MAT-006
            inputs:
            - quantity: 3
              item: ITM-CRF-004
              mode: consume
            requirements: {heat: none, power: none}
            """;
        var source = new InMemoryContentSource()
            .Add("core/defs/z.yaml", recipe.ReplaceLineEndings("\r\n"))
            .Add("core/defs/nested/a.yaml", hand)
            .Add("core/defs/spool.yaml", spool)
            .Add("core/pack.yaml", manifest)
            .Add("core/defs/fiber.yaml", fiber);
        Assert.Equal(SliceFingerprint(), Fingerprint(source));
    }

    [Fact]
    public void TheFingerprint_FollowsMeaning()
    {
        string Changed(string path, string before, string after) => Fingerprint(TestPacks.Core(TestPacks.Slice()
            .Select(f => f.Path == path ? (f.Path, f.Yaml.Replace(before, after)) : f).ToArray()));
        var prints = new[]
        {
            SliceFingerprint(),
            Changed("items/vine-fiber.yaml", "mass_g: 100", "mass_g: 101"),
            Changed("items/vine-fiber.yaml", "mass_g: 100", "mass_g: 100\ntags: [natural]"),
            Changed("items/vine-fiber.yaml", "name: Item ITM-CRF-004", "name: Vine Fiber"),
            Changed("recipes/twist-vine-cordage.yaml", "quantity: 3", "quantity: 4"),
            Changed("recipes/twist-vine-cordage.yaml", "duration_action_seconds: 140", "duration_action_seconds: 141"),
            Fingerprint(TestPacks.Core(TestPacks.Slice()).Add("core/stations/other.yaml",
                TestPacks.Hand.Replace("STN-HAND", "STN-COOK"))),
        };
        Assert.Equal(prints.Length, prints.Distinct().Count());
    }

    [Fact]
    public void TheFingerprint_IncludesPackVersions()
    {
        var source = new InMemoryContentSource()
            .Add("core/pack.yaml", TestPacks.CoreManifest.Replace("version: 1.0.0", "version: 1.0.1"));
        foreach (var (path, yaml) in TestPacks.Slice())
            source.Add("core/" + path, yaml);
        Assert.NotEqual(SliceFingerprint(), Fingerprint(source));
    }

    [Fact]
    public void TheFingerprint_IncludesTheHandlersThisBuildKnows()
    {
        var implemented = ContentLoadOptions.Authoring with { Handlers = HandlerRegistry.Default.WithImplemented("process.transform") };
        Assert.NotEqual(SliceFingerprint(), Fingerprint(TestPacks.Core(TestPacks.Slice()), implemented));
    }

    [Fact]
    public void TheRegistrysFingerprint_IsTheCanonicalOne()
    {
        var result = TestPacks.LoadCore(TestPacks.Slice());
        ContentRegistry registry = result.Registry!;
        Assert.Equal(registry.Fingerprint, ContentFingerprint.Compute(registry.Packs, registry.Definitions, registry.Handlers));
        Assert.Equal(registry.Fingerprint, ContentFingerprint.Compute(registry.Packs, registry.Definitions.Reverse().ToList(), registry.Handlers));
    }
}
