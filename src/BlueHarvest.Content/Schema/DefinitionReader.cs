#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// Turns one <c>blueharvest.content/v1</c> file into a typed definition, checking everything that can be checked
/// from the file alone. References are collected, not resolved: that needs every pack. A definition with errors
/// is still returned (with placeholders) so duplicate-ID and reference checks can run over it too; the load fails
/// anyway, so placeholders never reach a registry.
/// </summary>
internal static class DefinitionReader
{
    public const string Schema = "blueharvest.content/v1";

    static readonly string[] Presentations =
        { "note", "journal", "terminal", "system_message", "email", "audio_log", "radio", "inscription", "sign" };

    static readonly string[] BindingKinds =
        { "unbound", "npc", "item", "world_object", "device", "site", "broadcast" };

    static readonly string[] Severities = { "info", "warning", "critical", "personal" };

    const string End = "__end__";
    const int MaxQuantity = 100_000;

    public static ContentDefinition? Read(YamlNode root, ParseContext ctx)
    {
        if (root is not YamlMapping map)
        {
            ctx.Error(DiagnosticCodes.Envelope, root, $"A content file must be a mapping with schema, kind and id, not {root.Describe}.");
            return null;
        }
        var f = new FieldReader(ctx, map, "");
        string? schema = f.String("schema", required: true);
        if (schema is null)
            return null;
        if (schema != Schema)
        {
            string why = schema == ManifestReader.Schema
                ? "This is a pack manifest; a pack's manifest must be the pack.yaml at the pack's root."
                : $"schema is '{schema}'; content files use {Schema}.";
            ctx.Error(DiagnosticCodes.Envelope, f.At("schema"), why);
            return null;
        }
        string? kind = f.String("kind", required: true);
        string? idText = f.String("id", required: true);
        if (kind is null || idText is null)
            return null;
        if (!ContentKinds.All.Contains(kind))
        {
            ctx.Error(DiagnosticCodes.Envelope, f.At("kind"),
                $"kind is '{kind}'; it must be one of {string.Join(", ", ContentKinds.All)}.");
            return null;
        }
        if (!ContentId.TryResolve(idText, ctx.Namespace, out ContentId id))
        {
            ctx.Error(DiagnosticCodes.BadId, f.At("id"), $"id is '{idText}', which isn't a content ID ({FieldReader.IdHelp}).");
            return null;
        }
        ctx.DefinitionId = id.Value;

        var common = new Common(
            id,
            kind == ContentKinds.Narrative ? "" : f.String("name", required: true, allowEmpty: false) ?? "",
            kind == ContentKinds.Narrative ? "" : f.String("description") ?? "",
            f.Strings("tags", false, IsTag, "a tag (text of 1 to 64 characters, no spaces at either end)"),
            f.Strings("source_ids", false, IdRules.IsCatalogId, "a catalog ID such as ITM-TOL-018"),
            map.Line);

        ContentDefinition definition = kind switch
        {
            ContentKinds.Item => ReadItem(f, ctx, common),
            ContentKinds.Recipe => ReadRecipe(f, ctx, common),
            ContentKinds.Station => ReadStation(f, common, ctx),
            ContentKinds.ResourceSource => ReadSource(f, ctx, common),
            ContentKinds.Device => ReadDevice(f, ctx, common),
            _ => ReadNarrative(f, ctx, common),
        };
        f.Finish();
        return definition;
    }

    sealed record Common(ContentId Id, string Name, string Description, IReadOnlyList<string> Tags, IReadOnlyList<string> SourceIds, int Line);

    static bool IsTag(string text) => text.Length is > 0 and <= 64 && text.Trim() == text;

    static IReadOnlyList<string> OneHandler(FieldReader f, bool required)
    {
        string? handler = f.Pattern("handler", required, IdRules.IsHandler, "a handler key such as process.transform");
        return handler is null ? Array.Empty<string>() : new[] { handler };
    }

    static ItemDefinition ReadItem(FieldReader f, ParseContext ctx, Common c)
    {
        string category = f.Pattern("category", true, IdRules.IsToken, "a lower-case word such as material") ?? "";
        int mass = f.Int("mass_g", true, 0, 1_000_000) ?? 0;
        int stackLimit = f.Int("stack_limit", false, 1, 10_000) ?? 1;
        string stacking = f.Token("stacking", false, "stack", "unique") ?? (stackLimit > 1 ? "stack" : "unique");
        if (stacking == "unique" && stackLimit > 1)
            ctx.Error(DiagnosticCodes.BadValue, f.At("stack_limit"),
                $"{ctx.Who} is unique (stacking: unique), so stack_limit must be 1, not {stackLimit}.");
        IReadOnlyList<string> handlers = f.Strings("handlers", false, IdRules.IsHandler, "a handler key such as light.battery");
        return new ItemDefinition
        {
            Id = c.Id, Name = c.Name, Description = c.Description, Tags = c.Tags, SourceIds = c.SourceIds,
            PackId = ctx.Namespace, SourcePath = ctx.File, SourceLine = c.Line, Handlers = handlers,
            Category = category, MassG = mass, StackLimit = stackLimit, Stacking = stacking,
        };
    }

    static StationDefinition ReadStation(FieldReader f, Common c, ParseContext ctx) => new()
    {
        Id = c.Id, Name = c.Name, Description = c.Description, Tags = c.Tags, SourceIds = c.SourceIds,
        PackId = ctx.Namespace, SourcePath = ctx.File, SourceLine = c.Line, Handlers = OneHandler(f, false),
    };

    static RecipeDefinition ReadRecipe(FieldReader f, ParseContext ctx, Common c)
    {
        IReadOnlyList<string> handlers = OneHandler(f, true);
        ContentId? station = f.Ref("station", true, ContentKinds.Station);
        long ticks = ReadDuration(f, ctx);
        var inputs = ItemLines(f, ctx, "inputs", "consume");
        var tools = ItemLines(f, ctx, "tools", "retain");
        var outputs = ItemLines(f, ctx, "outputs", null);
        var byproducts = ItemLines(f, ctx, "byproducts", null);
        var targets = ItemLines(f, ctx, "targets", null);
        string heat = "none", power = "none";
        if (f.Map("requirements", false) is { } req)
        {
            heat = req.Token("heat", false, "none", "available") ?? "none";
            power = req.Token("power", false, "none", "available") ?? "none";
            req.Finish();
        }
        if (outputs.Count == 0 && targets.Count == 0)
            ctx.Error(DiagnosticCodes.MissingField, f.Node, $"{ctx.Who} has no outputs and no targets, so it would do nothing.");
        return new RecipeDefinition
        {
            Id = c.Id, Name = c.Name, Description = c.Description, Tags = c.Tags, SourceIds = c.SourceIds,
            PackId = ctx.Namespace, SourcePath = ctx.File, SourceLine = c.Line, Handlers = handlers,
            Station = station ?? new ContentId(ctx.Namespace + ":missing"),
            DurationTicks = ticks,
            Inputs = inputs, Tools = tools, Outputs = outputs, Byproducts = byproducts, Targets = targets,
            Heat = heat, Power = power,
        };
    }

    /// <summary>Exactly one of duration_action_seconds and duration_game_minutes, converted to ticks.</summary>
    static long ReadDuration(FieldReader f, ParseContext ctx)
    {
        bool hasSeconds = f.Has("duration_action_seconds"), hasMinutes = f.Has("duration_game_minutes");
        int? seconds = f.Int("duration_action_seconds", false, 1, 864_000);
        int? minutes = f.Int("duration_game_minutes", false, 1, 525_600);
        if (hasSeconds && hasMinutes)
            ctx.Error(DiagnosticCodes.BadValue, f.At("duration_game_minutes"),
                $"{ctx.Who} gives both duration_action_seconds and duration_game_minutes; give one.");
        else if (!hasSeconds && !hasMinutes)
            ctx.Error(DiagnosticCodes.MissingField, f.Node,
                $"{ctx.Who} is missing its duration: give duration_action_seconds or duration_game_minutes.");
        if (minutes is int m)
            return m * ContentTime.TicksPerGameMinute;
        if (seconds is int s)
            return s * ContentTime.TicksPerActionSecond;
        return 0;
    }

    /// <summary>A list of <c>{item, quantity[, mode]}</c>. Each item at most once per list.</summary>
    static IReadOnlyList<ItemQuantity> ItemLines(FieldReader f, ParseContext ctx, string key, string? mode)
    {
        var result = new List<ItemQuantity>();
        var seen = new HashSet<ContentId>();
        foreach (FieldReader line in f.Maps(key, false))
        {
            ContentId? item = line.Ref("item", true, ContentKinds.Item);
            int quantity = line.Int("quantity", true, 1, MaxQuantity) ?? 1;
            if (mode is not null)
                line.Token("mode", false, mode);
            line.Finish();
            if (item is not { } id)
                continue;
            if (!seen.Add(id))
            {
                ctx.Error(DiagnosticCodes.BadValue, line.Node, $"{ctx.Who} lists {id} twice in {key}; list it once with the total quantity.");
                continue;
            }
            result.Add(new ItemQuantity(id, quantity));
        }
        return result.AsReadOnly();
    }

    static ResourceSourceDefinition ReadSource(FieldReader f, ParseContext ctx, Common c)
    {
        IReadOnlyList<string> handlers = OneHandler(f, true);
        ContentId? tool = f.Ref("required_tool", false, ContentKinds.Item);
        string mode = "finite";
        string? renewalEvent = null;
        int batches = 1;
        if (f.Map("renewal", true) is { } renewal)
        {
            mode = renewal.Token("mode", true, "finite", "world_event") ?? "finite";
            if (mode == "finite")
                renewal.Forbid("event", "a finite source never renews");
            else
                renewalEvent = renewal.Pattern("event", true, IdRules.IsEventName, "an event name such as ecology.plant_regrowth");
            batches = renewal.Int("max_batches", true, 1, 10_000) ?? 1;
            renewal.Finish();
        }
        var yields = new List<YieldRange>();
        var seen = new HashSet<ContentId>();
        foreach (FieldReader y in f.Maps("yields", true, minCount: 1))
        {
            ContentId? item = y.Ref("item", true, ContentKinds.Item);
            int? min = y.Int("min_quantity", true, 0, MaxQuantity);
            int? max = y.Int("max_quantity", true, 1, MaxQuantity);
            y.Finish();
            if (min is int lo && max is int hi && lo > hi)
                ctx.Error(DiagnosticCodes.BadValue, y.At("min_quantity"), $"{y.PathOf("min_quantity")} is {lo}, more than max_quantity {hi}.");
            if (item is not { } id)
                continue;
            if (!seen.Add(id))
            {
                ctx.Error(DiagnosticCodes.BadValue, y.Node, $"{ctx.Who} lists {id} twice in yields.");
                continue;
            }
            yields.Add(new YieldRange(id, min ?? 0, max ?? 1));
        }
        return new ResourceSourceDefinition
        {
            Id = c.Id, Name = c.Name, Description = c.Description, Tags = c.Tags, SourceIds = c.SourceIds,
            PackId = ctx.Namespace, SourcePath = ctx.File, SourceLine = c.Line, Handlers = handlers,
            RequiredTool = tool, RenewalMode = mode, RenewalEvent = renewalEvent, MaxBatches = batches,
            Yields = yields.AsReadOnly(),
        };
    }

    static DeviceDefinition ReadDevice(FieldReader f, ParseContext ctx, Common c)
    {
        IReadOnlyList<string> handlers = OneHandler(f, true);
        var parts = ItemLines(f, ctx, "required_parts", null);
        string power = "none";
        if (f.Map("requirements", false) is { } req)
        {
            power = req.Token("power", false, "none", "available") ?? "none";
            req.Finish();
        }
        IReadOnlyList<string> states = f.Strings("states", true, IdRules.IsToken, "a lower-case state name such as active", minCount: 1);
        var facts = new List<FactProjection>();
        foreach (FieldReader p in f.Maps("facts_when_active", false))
        {
            string predicate = p.Pattern("predicate", true, IdRules.IsToken, "a lower-case predicate such as lit") ?? "";
            IReadOnlyList<string> args = p.Strings("args", false, a => a.Length is > 0 and <= 64, "an argument (1 to 64 characters)", unique: false);
            p.Finish();
            facts.Add(new FactProjection(predicate, args));
        }
        if (facts.Count > 0 && states.Count > 0 && !states.Contains("active"))
            ctx.Error(DiagnosticCodes.BadValue, f.At("states"), $"{ctx.Who} has facts_when_active but no 'active' state.");
        return new DeviceDefinition
        {
            Id = c.Id, Name = c.Name, Description = c.Description, Tags = c.Tags, SourceIds = c.SourceIds,
            PackId = ctx.Namespace, SourcePath = ctx.File, SourceLine = c.Line, Handlers = handlers,
            RequiredParts = parts, Power = power, States = states, FactsWhenActive = facts.AsReadOnly(),
        };
    }

    static NarrativeDefinition ReadNarrative(FieldReader f, ParseContext ctx, Common c)
    {
        f.Forbid("name", "a narrative entry's display name is its title");
        f.Forbid("description", "a narrative entry has its text in document or dialogue");
        string status = f.Token("status", true, "draft", "ready") ?? "draft";
        bool ready = status == "ready";
        string form = f.Token("form", true, "document", "dialogue") ?? "document";
        string presentation = form == "dialogue"
            ? f.Token("presentation", true, "dialogue") ?? "dialogue"
            : f.Token("presentation", true, Presentations) ?? "note";
        string title = f.String("title") ?? "";
        if (title.Trim().Length == 0)
            ctx.Incomplete(ready, f.At("title"), $"{ctx.Who} has no title.");
        string facility = f.String("facility") ?? "";
        if (facility.Length > 0 && !IdRules.IsFacility(facility))
            ctx.Error(DiagnosticCodes.BadValue, f.At("facility"), $"facility is '{facility}'; it must be a code of 2 to 4 capitals (MEN, ADM) or empty.");

        var binding = new NarrativeBinding("unbound", "");
        if (f.Map("source", false) is { } src)
        {
            string kind = src.Token("kind", true, BindingKinds) ?? "unbound";
            string reference = src.String("ref") ?? "";
            src.Finish();
            if (reference.Length > 0)
            {
                if (kind is "item" or "device")
                {
                    // Kept qualified, like every other reference.
                    if (src.RefFrom(src.At("ref"), "source.ref", kind == "item" ? ContentKinds.Item : ContentKinds.Device) is { } resolved)
                        reference = resolved.Value;
                }
                else if (!IdRules.IsBindingReference(reference))
                    ctx.Error(DiagnosticCodes.BadValue, src.At("ref"),
                        $"source.ref is '{reference}', which isn't a reference (letters, digits, '.', '_', ':' and '-', up to 64).");
            }
            else if (kind is not ("unbound" or "broadcast"))
                ctx.Incomplete(ready, src.Node, $"{ctx.Who} is presented through a {kind} but source.ref is empty.");
            binding = new NarrativeBinding(kind, reference);
        }
        IReadOnlyList<string> requires = f.Strings("requires_facts", false, IdRules.IsFact, "a fact ID such as FACT-MEN-001");
        IReadOnlyList<string> reveals = f.Strings("reveals_facts", false, IdRules.IsFact, "a fact ID such as FACT-MEN-001");

        NarrativeDocument? document = null;
        NarrativeDialogue? dialogue = null;
        if (form == "document")
        {
            f.Forbid("dialogue", "a document has no dialogue (form: document)");
            if (f.Map("document", true) is { } d)
            {
                string body = d.String("body") ?? "";
                if (body.Trim().Length == 0)
                    ctx.Incomplete(ready, d.At("body"), $"{ctx.Who} has no body text.");
                document = new NarrativeDocument(
                    d.String("author") ?? "", d.String("recipient") ?? "", d.String("subject") ?? "",
                    d.Token("severity", false, Severities) ?? "info", body);
                d.Finish();
            }
        }
        else
        {
            f.Forbid("document", "a dialogue has no document (form: dialogue)");
            if (f.Map("dialogue", true) is { } d)
            {
                dialogue = ReadDialogue(d, ctx, ready);
                d.Finish();
            }
        }

        return new NarrativeDefinition
        {
            Id = c.Id, Name = title, Tags = c.Tags, SourceIds = c.SourceIds,
            PackId = ctx.Namespace, SourcePath = ctx.File, SourceLine = c.Line,
            Form = form, Presentation = presentation, Status = status, Facility = facility, Binding = binding,
            RequiresFacts = requires, RevealsFacts = reveals, Document = document, Dialogue = dialogue,
        };
    }

    static NarrativeDialogue ReadDialogue(FieldReader d, ParseContext ctx, bool ready)
    {
        string start = d.String("start_node", required: true) ?? "";
        var nodes = new List<DialogueNode>();
        var nodeAt = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        var targets = new List<(string To, YamlNode At, string NodeId)>();
        foreach (FieldReader n in d.Maps("nodes", true, minCount: 1))
        {
            string id = n.Pattern("id", true, IdRules.IsNodeId, "a node ID (a letter, then letters, digits, '_' or '-', up to 41)") ?? "";
            string speaker = n.String("speaker") ?? "";
            string text = n.String("text") ?? "";
            bool ending = n.Bool("ending", false) ?? false;
            var choices = new List<DialogueChoice>();
            var choiceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldReader ch in n.Maps("choices", false))
            {
                string choiceId = ch.Pattern("id", true, IdRules.IsNodeId, "a choice ID such as c1") ?? "";
                string choiceText = ch.String("text") ?? "";
                string to = ch.String("to", required: true) ?? End;
                IReadOnlyList<string> requires = ch.Strings("requires_facts", false, IdRules.IsFact, "a fact ID such as FACT-MEN-001");
                IReadOnlyList<string> reveals = ch.Strings("reveals_facts", false, IdRules.IsFact, "a fact ID such as FACT-MEN-001");
                ch.Finish();
                if (choiceId.Length > 0 && !choiceIds.Add(choiceId))
                    ctx.Error(DiagnosticCodes.DialogueGraph, ch.At("id"), $"Node {id} has two choices with ID {choiceId}.");
                if (choiceText.Trim().Length == 0)
                    ctx.Incomplete(ready, ch.At("text"), $"A choice in node {id} has no text.");
                targets.Add((to, ch.At("to"), id));
                choices.Add(new DialogueChoice(choiceId, choiceText, to, requires, reveals));
            }
            n.Finish();
            if (id.Length > 0)
            {
                if (id == End)
                    ctx.Error(DiagnosticCodes.DialogueGraph, n.At("id"), $"{End} is reserved for the end of the conversation.");
                else if (!nodeAt.TryAdd(id, n.At("id")))
                    ctx.Error(DiagnosticCodes.DialogueGraph, n.At("id"), $"Two nodes have the ID {id} (first at line {nodeAt[id].Line}).");
            }
            if (text.Trim().Length == 0)
                ctx.Incomplete(ready, n.At("text"), $"Node {id} has no text.");
            if (ending && choices.Count > 0)
                ctx.Warning(DiagnosticCodes.DialogueShape, n.At("ending"), $"Node {id} is an ending, so its {choices.Count} choice(s) are ignored.");
            if (!ending && choices.Count == 0)
                ctx.Incomplete(ready, n.Node, $"Node {id} has no choices and isn't marked as an ending.");
            nodes.Add(new DialogueNode(id, speaker, text, ending, choices.AsReadOnly()));
        }

        if (start.Length > 0 && !nodeAt.ContainsKey(start))
            ctx.Error(DiagnosticCodes.DialogueGraph, d.At("start_node"), $"start_node is '{start}', but no node has that ID.");
        foreach (var (to, at, fromNode) in targets)
            if (to != End && !nodeAt.ContainsKey(to))
                ctx.Error(DiagnosticCodes.DialogueGraph, at, $"A choice in node {fromNode} goes to '{to}', but no node has that ID (use {End} to end).");

        // Reachability from the start, over choices of nodes that aren't endings.
        var byId = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);
        foreach (DialogueNode node in nodes)
            byId.TryAdd(node.Id, node);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (byId.ContainsKey(start))
        {
            reached.Add(start);
            queue.Enqueue(start);
        }
        while (queue.Count > 0)
        {
            DialogueNode here = byId[queue.Dequeue()];
            if (here.Ending)
                continue;
            foreach (DialogueChoice choice in here.Choices)
                if (byId.ContainsKey(choice.To) && reached.Add(choice.To))
                    queue.Enqueue(choice.To);
        }
        if (byId.ContainsKey(start))
            foreach (DialogueNode node in nodes)
                if (node.Id.Length > 0 && !reached.Contains(node.Id) && nodeAt.TryGetValue(node.Id, out YamlNode? at))
                    ctx.Warning(DiagnosticCodes.DialogueShape, at, $"Node {node.Id} can't be reached from the start node.");

        return new NarrativeDialogue(start, nodes.AsReadOnly());
    }
}
