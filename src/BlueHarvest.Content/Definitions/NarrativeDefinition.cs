#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// <c>kind: narrative</c>, as the Narrative Studio writes it: a document (note, journal, terminal message…) or a
/// branching dialogue. Its facts and binding are authoring data: reading it or choosing a reply changes nothing
/// until the sim authorises it (Narrative Studio guide §3).
/// </summary>
public sealed record NarrativeDefinition : ContentDefinition
{
    public override string Kind => ContentKinds.Narrative;

    /// <summary><c>document</c> or <c>dialogue</c>.</summary>
    public required string Form { get; init; }

    /// <summary>The reader skin: <c>note</c>, <c>journal</c>, <c>terminal</c>… or <c>dialogue</c>.</summary>
    public required string Presentation { get; init; }

    /// <summary><c>draft</c> or <c>ready</c>. A draft loads for authoring and isn't runtime-ready.</summary>
    public required string Status { get; init; }

    public bool IsDraft => Status == "draft";

    /// <summary>A facility or region code (<c>MEN</c>), or empty.</summary>
    public string Facility { get; init; } = "";

    /// <summary>Where the game would surface it (<c>source:</c> in YAML).</summary>
    public NarrativeBinding Binding { get; init; } = new("unbound", "");

    public IReadOnlyList<string> RequiresFacts { get; init; } = [];
    public IReadOnlyList<string> RevealsFacts { get; init; } = [];

    /// <summary>Set when <see cref="Form"/> is <c>document</c>.</summary>
    public NarrativeDocument? Document { get; init; }

    /// <summary>Set when <see cref="Form"/> is <c>dialogue</c>.</summary>
    public NarrativeDialogue? Dialogue { get; init; }

    internal override void WriteFields(CanonicalWriter w)
    {
        w.Field("form", Form);
        w.Field("presentation", Presentation);
        w.Field("status", Status);
        w.Field("facility", Facility);
        w.Field("source_kind", Binding.Kind);
        w.Field("source_ref", Binding.Ref);
        w.SetField("requires_facts", RequiresFacts);
        w.SetField("reveals_facts", RevealsFacts);
        if (Document is { } d)
        {
            w.Text("document");
            w.Field("author", d.Author);
            w.Field("recipient", d.Recipient);
            w.Field("subject", d.Subject);
            w.Field("severity", d.Severity);
            w.Field("body", d.Body);
        }
        if (Dialogue is { } g)
        {
            w.Text("dialogue");
            w.Field("start_node", g.StartNode);
            w.Count(g.Nodes.Count);
            foreach (DialogueNode n in g.Nodes)
            {
                w.Field("id", n.Id);
                w.Field("speaker", n.Speaker);
                w.Field("text", n.Text);
                w.Field("ending", n.Ending);
                w.Count(n.Choices.Count);
                foreach (DialogueChoice c in n.Choices)
                {
                    w.Field("id", c.Id);
                    w.Field("text", c.Text);
                    w.Field("to", c.To);
                    w.SetField("requires_facts", c.RequiresFacts);
                    w.SetField("reveals_facts", c.RevealsFacts);
                }
            }
        }
    }
}

/// <summary>
/// How a narrative entry is surfaced: <c>npc</c>, <c>item</c>, <c>world_object</c>, <c>device</c>, <c>site</c>,
/// <c>broadcast</c>, or <c>unbound</c>. Item and device refs are checked against the registry; the
/// others have no registry yet and are checked for form only.
/// </summary>
public sealed record NarrativeBinding(string Kind, string Ref);

public sealed record NarrativeDocument(string Author, string Recipient, string Subject, string Severity, string Body);

public sealed record NarrativeDialogue(string StartNode, IReadOnlyList<DialogueNode> Nodes);

public sealed record DialogueNode(string Id, string Speaker, string Text, bool Ending, IReadOnlyList<DialogueChoice> Choices);

/// <summary>A reply. <see cref="To"/> is a node in the same dialogue, or <c>__end__</c>.</summary>
public sealed record DialogueChoice(
    string Id, string Text, string To, IReadOnlyList<string> RequiresFacts, IReadOnlyList<string> RevealsFacts);
