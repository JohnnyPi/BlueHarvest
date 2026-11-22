#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// Loads a set of content packs into a <see cref="ContentRegistry"/>, in the phases of the YAML plan §5.1:
/// <list type="number">
/// <item>find the packs (<c>pack.yaml</c> at the source root, or one in each folder directly inside it) and order
/// them, dependencies first, ties by priority then pack ID;</item>
/// <item>read each content file strictly (<see cref="StrictYamlReader"/>);</item>
/// <item>turn it into a typed definition, rejecting unknown and duplicate keys;</item>
/// <item>check what one file can show on its own;</item>
/// <item>build the symbol table: duplicate IDs, namespaces, explicit replacements;</item>
/// <item>resolve references and check handlers: unknown or unimplemented ones are NOT_RUNTIME_READY, a note when
/// authoring and an error in runtime mode;</item>
/// <item>freeze the registry and</item>
/// <item>compute its fingerprint.</item>
/// </list>
/// The ninth phase, handing the registry to the game, is the caller's. Nothing is loaded if anything is an error.
/// </summary>
public sealed class ContentLoader : IContentLoader
{
    public ContentLoadResult Load(IContentSource source, ContentLoadOptions options)
    {
        var diagnostics = new DiagnosticBag();
        ContentRegistry? registry = Run(source, options, diagnostics);
        return new ContentLoadResult(diagnostics.ErrorCount == 0 ? registry : null, diagnostics.Sorted());
    }

    /// <summary>Loads with a new loader: <c>ContentLoader.LoadFrom(source, ContentLoadOptions.Authoring)</c>.</summary>
    public static ContentLoadResult LoadFrom(IContentSource source, ContentLoadOptions options) =>
        new ContentLoader().Load(source, options);

    static ContentRegistry? Run(IContentSource source, ContentLoadOptions options, DiagnosticBag diagnostics)
    {
        // Every file, in one fixed order, with unsafe paths refused.
        var files = new List<ContentFile>();
        foreach (ContentFile file in source.Enumerate().OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            if (IsSafePath(file.Path))
                files.Add(file);
            else
                diagnostics.Error(DiagnosticCodes.ContentPath, file.Path, 0, 0, null,
                    "This path isn't allowed in a content source (it's absolute, uses '\\', or has an empty, '.' or '..' part).");
        }

        // Phase 1: the packs and their order.
        List<PackManifest> manifests = ReadManifests(source, files, options, diagnostics);
        List<PackManifest> order = PackOrder.Sort(manifests, options.GameVersion, diagnostics);

        // Phases 2 to 4: every definition file of every pack that can load.
        var definitions = new List<ContentDefinition>();
        var references = new List<PendingReference>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (PackManifest pack in manifests)
            foreach (ContentFile file in files)
                if (file.Path.StartsWith(pack.Root, StringComparison.Ordinal))
                    claimed.Add(file.Path);
        foreach (PackManifest pack in order)
            ReadPack(pack, files, options.Limits, diagnostics, definitions, references);
        foreach (ContentFile file in files)
            if (!claimed.Contains(file.Path) && IsYaml(file.Path) && !IsManifestPath(file.Path))
                diagnostics.Warning(DiagnosticCodes.ContentPath, file.Path, 0, 0, null,
                    "This file isn't inside a pack folder, so it isn't loaded.");

        // Phase 5: the symbol table.
        var table = BuildTable(order, definitions, diagnostics);

        // Phase 6: references, then handlers and readiness.
        var packsById = order.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (PendingReference r in references)
            Resolve(r, table, packsById, diagnostics);
        var notReady = CheckHandlers(table.Values, options, diagnostics);

        if (diagnostics.ErrorCount > 0)
            return null;
        if (order.Count == 0)
            return null;

        // Phases 7 and 8: freeze, and fingerprint.
        return new ContentRegistry(order, table.Values, notReady, options.Handlers);
    }

    static bool IsSafePath(string path) =>
        path.Length > 0 && !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    static bool IsYaml(string path) =>
        path.EndsWith(".yaml", StringComparison.Ordinal) || path.EndsWith(".yml", StringComparison.Ordinal);

    static bool IsManifestPath(string path) =>
        path == ManifestReader.FileName || path.EndsWith("/" + ManifestReader.FileName, StringComparison.Ordinal);

    static List<PackManifest> ReadManifests(IContentSource source, List<ContentFile> files, ContentLoadOptions options,
        DiagnosticBag diagnostics)
    {
        // A pack.yaml at the root makes the source a single pack; otherwise each folder directly inside it can be one.
        var found = new List<(ContentFile File, string Root)>();
        ContentFile? rootManifest = files.FirstOrDefault(x => x.Path == ManifestReader.FileName);
        if (rootManifest is not null)
            found.Add((rootManifest, ""));
        else
            foreach (ContentFile file in files)
            {
                string[] parts = file.Path.Split('/');
                if (parts.Length == 2 && parts[1] == ManifestReader.FileName)
                    found.Add((file, parts[0] + "/"));
            }
        if (found.Count == 0)
        {
            diagnostics.Error(DiagnosticCodes.Manifest, null, 0, 0, null,
                $"No {ManifestReader.FileName} in {source.Description}, or in any folder directly inside it.");
            return new List<PackManifest>();
        }

        var manifests = new List<PackManifest>();
        var byId = new Dictionary<string, PackManifest>(StringComparer.Ordinal);
        foreach (var (file, root) in found)
        {
            YamlNode? node = ReadYaml(file, options.Limits, diagnostics);
            if (node is null)
                continue;
            PackManifest? manifest = ManifestReader.Read(node, new ParseContext(diagnostics, file.Path, ""), file.Path, root);
            if (manifest is null)
                continue;
            if (byId.TryGetValue(manifest.Id, out PackManifest? first))
            {
                diagnostics.Error(DiagnosticCodes.DuplicatePack, file.Path, node.Line, node.Column, "pack " + manifest.Id,
                    $"Pack {manifest.Id} is already defined by {first.ManifestPath}.");
                continue;
            }
            byId.Add(manifest.Id, manifest);
            manifests.Add(manifest);
        }
        return manifests;
    }

    static YamlNode? ReadYaml(ContentFile file, YamlLimits limits, DiagnosticBag diagnostics)
    {
        if (file.Length > limits.MaxFileBytes)
        {
            diagnostics.Error(DiagnosticCodes.FileTooLarge, file.Path, 0, 0, null,
                $"The file is {file.Length} bytes; the limit is {limits.MaxFileBytes}.");
            return null;
        }
        byte[] bytes;
        try
        {
            bytes = file.ReadAllBytes();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Error(DiagnosticCodes.FileTooLarge, file.Path, 0, 0, null, $"The file couldn't be read: {ex.Message}");
            return null;
        }
        return StrictYamlReader.Read(bytes, file.Path, limits, diagnostics);
    }

    static void ReadPack(PackManifest pack, List<ContentFile> files, YamlLimits limits, DiagnosticBag diagnostics,
        List<ContentDefinition> definitions, List<PendingReference> references)
    {
        var roots = pack.ContentRoots.Select(r => pack.Root + r + "/").ToList();
        foreach (ContentFile file in files)
        {
            if (!file.Path.StartsWith(pack.Root, StringComparison.Ordinal) || file.Path == pack.ManifestPath || !IsYaml(file.Path))
                continue;
            if (pack.Root.Length == 0 && IsManifestPath(file.Path))
                continue; // a nested pack.yaml in a single-pack source isn't a definition
            if (!roots.Any(root => file.Path.StartsWith(root, StringComparison.Ordinal)))
            {
                if (!IsManifestPath(file.Path))
                    diagnostics.Warning(DiagnosticCodes.ContentPath, file.Path, 0, 0, null,
                        $"This file isn't under any of pack {pack.Id}'s content_roots ({string.Join(", ", pack.ContentRoots)}), so it isn't loaded.");
                continue;
            }
            if (!file.Path.EndsWith(".yaml", StringComparison.Ordinal))
            {
                diagnostics.Warning(DiagnosticCodes.ContentPath, file.Path, 0, 0, null,
                    "Content files end in .yaml, not .yml, so this one isn't loaded.");
                continue;
            }
            YamlNode? node = ReadYaml(file, limits, diagnostics);
            if (node is null)
                continue;
            var ctx = new ParseContext(diagnostics, file.Path, pack.Id);
            ContentDefinition? definition = DefinitionReader.Read(node, ctx);
            references.AddRange(ctx.References);
            if (definition is not null)
                definitions.Add(definition);
        }
    }

    static Dictionary<ContentId, ContentDefinition> BuildTable(List<PackManifest> order, List<ContentDefinition> definitions,
        DiagnosticBag diagnostics)
    {
        var table = new Dictionary<ContentId, ContentDefinition>();
        var replacedBy = new Dictionary<ContentId, string>();
        foreach (PackManifest pack in order)
        {
            var used = new HashSet<ContentId>();
            foreach (ContentDefinition def in definitions)
            {
                if (def.PackId != pack.Id)
                    continue;
                string at = def.Id.Value;
                if (def.Id.Namespace == pack.Id)
                {
                    if (table.TryGetValue(def.Id, out ContentDefinition? first))
                        diagnostics.Error(DiagnosticCodes.DuplicateId, def.SourcePath, def.SourceLine, 1, at,
                            $"{def.Id} is defined twice: here and in {first.SourcePath}:{first.SourceLine}.");
                    else
                        table.Add(def.Id, def);
                    continue;
                }
                string owner = def.Id.Namespace;
                if (!pack.Replaces.Contains(def.Id))
                {
                    diagnostics.Error(DiagnosticCodes.ForeignNamespace, def.SourcePath, def.SourceLine, 1, at,
                        $"{def.Id} is in the {owner} namespace, but it's in pack {pack.Id}. Write its id without a namespace, " +
                        $"or list {def.Id} under replaces in {pack.ManifestPath} to replace {owner}'s definition.");
                    continue;
                }
                if (!used.Add(def.Id))
                {
                    diagnostics.Error(DiagnosticCodes.DuplicateId, def.SourcePath, def.SourceLine, 1, at,
                        $"Pack {pack.Id} replaces {def.Id} twice.");
                    continue;
                }
                if (!table.TryGetValue(def.Id, out ContentDefinition? original))
                {
                    diagnostics.Error(DiagnosticCodes.Replace, def.SourcePath, def.SourceLine, 1, at,
                        $"{def.Id} replaces a definition, but no pack loaded before {pack.Id} defines it.");
                    continue;
                }
                // Checked before the dependency: once replaced, the table holds the first replacement, not the original.
                if (replacedBy.TryGetValue(def.Id, out string? other))
                {
                    diagnostics.Error(DiagnosticCodes.Replace, def.SourcePath, def.SourceLine, 1, at,
                        $"{def.Id} is already replaced by pack {other}; only one pack can replace a definition.");
                    continue;
                }
                // The pack that owns the namespace must be a dependency.
                if (!pack.DependsOn(owner))
                {
                    diagnostics.Error(DiagnosticCodes.Replace, def.SourcePath, def.SourceLine, 1, at,
                        $"Pack {pack.Id} replaces {def.Id}, which belongs to pack {owner}; that pack must be one of its dependencies.");
                    continue;
                }
                if (original.Kind != def.Kind)
                {
                    diagnostics.Error(DiagnosticCodes.Replace, def.SourcePath, def.SourceLine, 1, at,
                        $"{def.Id} is {ContentKinds.WithArticle(original.Kind)}, so its replacement can't be {ContentKinds.WithArticle(def.Kind)}.");
                    continue;
                }
                replacedBy.Add(def.Id, pack.Id);
                table[def.Id] = def;
            }
            foreach (ContentId wanted in pack.Replaces)
                if (!used.Contains(wanted))
                    diagnostics.Error(DiagnosticCodes.Replace, pack.ManifestPath, 0, 0, "pack " + pack.Id,
                        $"Pack {pack.Id} lists {wanted} under replaces, but none of its files defines {wanted}.");
        }
        return table;
    }

    static void Resolve(PendingReference r, Dictionary<ContentId, ContentDefinition> table,
        Dictionary<string, PackManifest> packs, DiagnosticBag diagnostics)
    {
        string ns = r.Target.Namespace;
        if (ns != r.FromPack && packs.TryGetValue(r.FromPack, out PackManifest? referrer) && !referrer.DependsOn(ns))
        {
            diagnostics.Error(DiagnosticCodes.UndeclaredDependency, r.File, r.Line, r.Column, r.From,
                $"{r.From} references {r.Target} ({r.Field}), but {ns} isn't one of pack {r.FromPack}'s dependencies.");
            return;
        }
        if (!table.TryGetValue(r.Target, out ContentDefinition? target))
        {
            diagnostics.Error(DiagnosticCodes.UnresolvedReference, r.File, r.Line, r.Column, r.From,
                $"{r.From} references unregistered {ContentKinds.Label(r.ExpectedKind)} {r.Target} ({r.Field}).");
            return;
        }
        if (target.Kind != r.ExpectedKind)
            diagnostics.Error(DiagnosticCodes.WrongKind, r.File, r.Line, r.Column, r.From,
                $"{r.From} needs {ContentKinds.WithArticle(r.ExpectedKind)} at {r.Field}, but {r.Target} is {ContentKinds.WithArticle(target.Kind)}.");
    }

    static List<ContentId> CheckHandlers(IEnumerable<ContentDefinition> definitions, ContentLoadOptions options,
        DiagnosticBag diagnostics)
    {
        var notReady = new List<ContentId>();
        ContentSeverity readiness = options.Mode == ContentMode.Runtime ? ContentSeverity.Error : ContentSeverity.Note;
        foreach (ContentDefinition def in definitions.OrderBy(d => d.Id.Value, StringComparer.Ordinal))
        {
            var reasons = new List<string>();
            foreach (string key in def.Handlers)
            {
                if (!options.Handlers.TryGet(key, out HandlerContract? contract))
                {
                    reasons.Add($"it names handler {key}, which this build doesn't know");
                    continue;
                }
                if (contract.Kind != def.Kind)
                {
                    diagnostics.Error(DiagnosticCodes.HandlerMismatch, def.SourcePath, def.SourceLine, 1, def.Id.Value,
                        $"{key} is {ContentKinds.WithArticle(contract.Kind)} handler, but {def.Id} is {ContentKinds.WithArticle(def.Kind)}.");
                    continue;
                }
                if (contract.Check?.Invoke(def) is { } problem)
                    diagnostics.Error(DiagnosticCodes.HandlerMismatch, def.SourcePath, def.SourceLine, 1, def.Id.Value,
                        $"{def.Id}: {problem}");
                if (!contract.Implemented)
                    reasons.Add($"its handler {key} is a known contract that isn't implemented yet");
            }
            if (def is NarrativeDefinition { IsDraft: true })
                reasons.Add("it's a draft (status: draft)");
            if (reasons.Count == 0)
                continue;
            notReady.Add(def.Id);
            diagnostics.Add(DiagnosticCodes.NotRuntimeReady, readiness, def.SourcePath, def.SourceLine, 1, def.Id.Value,
                $"NOT_RUNTIME_READY: {def.Id} can't be used at run time yet: {string.Join("; ", reasons)}.");
        }
        return notReady;
    }
}
