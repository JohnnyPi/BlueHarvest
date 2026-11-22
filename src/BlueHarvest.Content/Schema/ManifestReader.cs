#nullable enable
using System.Text.RegularExpressions;

namespace BlueHarvest.Content;

/// <summary>
/// Reads a <c>pack.yaml</c> (<c>schema: blueharvest.pack/v1</c>): the shape in the YAML plan §4.2, which is also
/// what the Narrative Studio exports, plus <c>name</c>, <c>description</c> and <c>replaces</c>. A dependency is a
/// pack ID, or <c>{id, version}</c> with a version range.
/// </summary>
internal static class ManifestReader
{
    public const string Schema = "blueharvest.pack/v1";
    public const string FileName = "pack.yaml";

    static readonly Regex RootSegment = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant);

    public static PackManifest? Read(YamlNode root, ParseContext ctx, string manifestPath, string packRoot)
    {
        if (root is not YamlMapping map)
        {
            ctx.Error(DiagnosticCodes.Manifest, root, $"A pack manifest must be a mapping, not {root.Describe}.");
            return null;
        }
        int before = ctx.Diagnostics.ErrorCount;
        var f = new FieldReader(ctx, map, "");
        string? schema = f.String("schema", required: true);
        if (schema is not null && schema != Schema)
            ctx.Error(DiagnosticCodes.Manifest, f.At("schema"), $"schema is '{schema}'; a pack manifest uses {Schema}.");
        string? id = f.Pattern("id", true, IdRules.IsNamespace,
            "a pack ID (lower case, starting with a letter; letters, digits and '-', in segments joined by '.'; up to 40)");
        if (id is not null)
            ctx.DefinitionId = "pack " + id;
        string name = f.String("name") ?? "";
        string description = f.String("description") ?? "";
        string? versionText = f.String("version", required: true);
        SemVersion version = default;
        if (versionText is not null && !SemVersion.TryParse(versionText, out version))
            ctx.Error(DiagnosticCodes.BadValue, f.At("version"), $"version is '{versionText}'; it must be major.minor.patch, like 1.0.0.");
        VersionRange? requiresGame = null;
        if (f.String("requires_game") is { } requiresText && !VersionRange.TryParse(requiresText, out requiresGame))
            ctx.Error(DiagnosticCodes.BadValue, f.At("requires_game"), $"requires_game is '{requiresText}'; {RangeHelp}");
        int priority = f.Int("priority", false, -1000, 1000) ?? 0;
        var dependencies = ReadDependencies(f, ctx, id);
        var roots = ReadRoots(f, ctx);
        var replaces = new List<ContentId>();
        foreach (string text in f.Strings("replaces", false, t => ContentId.TryParse(t, out _), "a qualified content ID such as core:knife"))
        {
            var target = new ContentId(text);
            if (target.Namespace == id)
                ctx.Error(DiagnosticCodes.Replace, f.At("replaces"), $"replaces lists {text}, which is this pack's own; a pack can't replace itself.");
            else
                replaces.Add(target);
        }
        f.Finish();
        if (id is null || ctx.Diagnostics.ErrorCount > before)
            return null;
        return new PackManifest
        {
            Id = id, Name = name, Description = description, Version = version, RequiresGame = requiresGame,
            Dependencies = dependencies, Priority = priority, ContentRoots = roots, Replaces = replaces.AsReadOnly(),
            ManifestPath = manifestPath, Root = packRoot,
        };
    }

    const string RangeHelp = "a version range is comparisons such as >=0.1.0 or >=1.0.0 <2.0.0.";

    static IReadOnlyList<PackDependency> ReadDependencies(FieldReader f, ParseContext ctx, string? self)
    {
        var result = new List<PackDependency>();
        if (f.Raw("dependencies", false) is not { } node)
            return result;
        if (node is not YamlSequence list)
        {
            ctx.Error(DiagnosticCodes.BadValue, node, $"dependencies must be a list, not {node.Describe}.");
            return result;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < list.Items.Count; i++)
        {
            YamlNode item = list.Items[i];
            string? depId = null;
            VersionRange? range = null;
            if (item is YamlScalar { IsNull: false } s)
                depId = s.Value;
            else if (item is YamlMapping m)
            {
                var d = new FieldReader(ctx, m, $"dependencies[{i}].");
                depId = d.String("id", required: true);
                if (d.String("version") is { } rangeText && !VersionRange.TryParse(rangeText, out range))
                    ctx.Error(DiagnosticCodes.BadValue, d.At("version"), $"dependencies[{i}].version is '{rangeText}'; {RangeHelp}");
                d.Finish();
            }
            else
            {
                ctx.Error(DiagnosticCodes.BadValue, item, $"dependencies[{i}] must be a pack ID or {{id, version}}, not {item.Describe}.");
                continue;
            }
            if (depId is null)
                continue;
            if (!IdRules.IsNamespace(depId))
                ctx.Error(DiagnosticCodes.BadValue, item, $"dependencies[{i}] is '{depId}', which isn't a pack ID.");
            else if (depId == self)
                ctx.Error(DiagnosticCodes.DependencyCycle, item, "The pack depends on itself.");
            else if (!seen.Add(depId))
                ctx.Error(DiagnosticCodes.BadValue, item, $"dependencies lists {depId} twice.");
            else
                result.Add(new PackDependency(depId, range));
        }
        return result.AsReadOnly();
    }

    static IReadOnlyList<string> ReadRoots(FieldReader f, ParseContext ctx) =>
        f.Strings("content_roots", true, IsRoot,
            "a folder inside the pack, like items or narrative/documents (no '..', no leading '/')", minCount: 1);

    static bool IsRoot(string text) =>
        text.Length <= 200 && text.Split('/').All(seg => RootSegment.IsMatch(seg) && seg != "." && seg != "..");
}
