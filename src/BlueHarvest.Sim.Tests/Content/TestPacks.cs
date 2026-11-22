using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>Builds small in-memory packs for the content-library tests.</summary>
internal static class TestPacks
{
    public const string CoreManifest = """
        schema: blueharvest.pack/v1
        id: core
        version: 1.0.0
        content_roots: [items, recipes, stations, sources, devices, narrative]
        """;

    /// <summary>A pack manifest for pack <paramref name="id"/> with these dependency lines (already YAML).</summary>
    public static string Manifest(string id, string dependencies = "[]", string extra = "") =>
        $"schema: blueharvest.pack/v1\nid: {id}\nversion: 1.0.0\ndependencies: {dependencies}\n" +
        $"content_roots: [items, recipes, stations, sources, devices, narrative]\n{extra}";

    /// <summary>The core pack with these files (paths inside the pack).</summary>
    public static InMemoryContentSource Core(params (string Path, string Yaml)[] files)
    {
        var source = new InMemoryContentSource();
        source.Add("core/pack.yaml", CoreManifest);
        foreach (var (path, yaml) in files)
            source.Add("core/" + path, yaml);
        return source;
    }

    public static ContentLoadResult Load(IContentSource source, ContentLoadOptions? options = null) =>
        ContentLoader.LoadFrom(source, options ?? ContentLoadOptions.Authoring);

    public static ContentLoadResult LoadCore(params (string Path, string Yaml)[] files) => Load(Core(files));

    /// <summary>A minimal valid item; <paramref name="extra"/> is more YAML lines.</summary>
    public static string Item(string id, string extra = "") =>
        $"schema: blueharvest.content/v1\nkind: item\nid: {id}\nname: Item {id}\ncategories: [part]\nsize: hand\nmass_g: 100\nvolume_ml: 100\n{extra}";

    public const string Hand = """
        schema: blueharvest.content/v1
        kind: station
        id: STN-HAND
        name: By Hand
        """;

    /// <summary>A recipe turning 3 of <paramref name="input"/> into 1 of <paramref name="output"/> by hand.</summary>
    public static string Recipe(string id, string input = "ITM-CRF-004", string output = "ITM-MAT-006",
        string handler = "process.transform", string extra = "") =>
        $"""
        schema: blueharvest.content/v1
        kind: recipe
        id: {id}
        name: Recipe {id}
        handler: {handler}
        station: STN-HAND
        duration_action_seconds: 140
        inputs:
          - item: {input}
            quantity: 3
        outputs:
          - item: {output}
            quantity: 1
        {extra}
        """.ReplaceLineEndings("\n");

    /// <summary>The vertical-slice graph: two items, a station and a recipe that uses them.</summary>
    public static (string Path, string Yaml)[] Slice() =>
    [
        ("items/vine-fiber.yaml", Item("ITM-CRF-004")),
        ("items/cordage-spool.yaml", Item("ITM-MAT-006")),
        ("stations/hand.yaml", Hand),
        ("recipes/twist-vine-cordage.yaml", Recipe("RCP-022")),
    ];

    public static (string Path, string Yaml)[] With(this (string Path, string Yaml)[] files, params (string Path, string Yaml)[] more) =>
        files.Concat(more).ToArray();

    /// <summary>Every diagnostic, for assertion messages.</summary>
    public static string Report(ContentLoadResult result) =>
        result.Diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("\n", result.Diagnostics);

    public static void AssertLoads(ContentLoadResult result)
    {
        Assert.True(result.Succeeded, Report(result));
    }

    /// <summary>The load failed with at least one error with this code.</summary>
    public static ContentDiagnostic AssertError(ContentLoadResult result, string code)
    {
        Assert.False(result.Succeeded, "Expected the load to fail.\n" + Report(result));
        ContentDiagnostic? found = result.Diagnostics.FirstOrDefault(d => d.Code == code && d.Severity == ContentSeverity.Error);
        Assert.True(found is not null, $"Expected an error {code}.\n" + Report(result));
        return found!;
    }

    public static ContentDiagnostic AssertDiagnostic(ContentLoadResult result, string code, ContentSeverity severity)
    {
        ContentDiagnostic? found = result.Diagnostics.FirstOrDefault(d => d.Code == code && d.Severity == severity);
        Assert.True(found is not null, $"Expected a {severity} {code}.\n" + Report(result));
        return found!;
    }
}
