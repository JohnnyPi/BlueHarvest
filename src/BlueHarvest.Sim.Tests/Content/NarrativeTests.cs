using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>Narrative entries as the Narrative Studio writes them: documents, dialogues, drafts.</summary>
public class NarrativeTests
{
    static string Document(string status = "ready", string body = "The paddock was empty again at sunrise.", string extra = "") => $"""
        schema: blueharvest.content/v1
        kind: narrative
        id: MEN-01-N01
        form: document
        presentation: journal
        status: {status}
        title: The North Enclosure
        facility: MEN
        source:
          kind: world_object
          ref: MEN-OBSERVATION-BLIND
        reveals_facts:
          - FACT-MEN-ANIMAL-FOREKNOWLEDGE
        document:
          author: Dr. Sera Okonkwo
          severity: personal
          body: "{body}"
        {extra}
        """.ReplaceLineEndings("\n");

    /// <summary>A dialogue; <paramref name="nodes"/> is the YAML of the node list, indented four spaces.</summary>
    static string Dialogue(string nodes, string start = "start", string status = "ready") => $"""
        schema: blueharvest.content/v1
        kind: narrative
        id: MEN-01-TALK-001
        form: dialogue
        presentation: dialogue
        status: {status}
        title: Ask about the eastern paddock
        dialogue:
          start_node: {start}
          nodes:
        {nodes}
        """;

    const string TwoNodes = """
            - id: start
              text: The animals refuse to enter that part of the enclosure.
              choices:
                - {id: c1, text: "What frightened them?", to: explanation}
                - {id: c2, text: "I'll go.", to: __end__}
            - id: explanation
              text: I don't yet know.
              ending: true
        """;

    static ContentLoadResult Load(string yaml, ContentLoadOptions? options = null) =>
        TestPacks.Load(TestPacks.Core(("narrative/entry.yaml", yaml)), options);

    [Fact]
    public void ADocument_Loads()
    {
        var result = Load(Document());
        TestPacks.AssertLoads(result);
        var note = result.Registry!.Get<NarrativeDefinition>("core:MEN-01-N01");
        Assert.Equal("The North Enclosure", note.Name);
        Assert.Equal("document", note.Form);
        Assert.Equal(new NarrativeBinding("world_object", "MEN-OBSERVATION-BLIND"), note.Binding);
        Assert.Equal("The paddock was empty again at sunrise.", note.Document!.Body);
        Assert.Equal("personal", note.Document!.Severity);
        Assert.Equal("", note.Document!.Recipient);
        Assert.Null(note.Dialogue);
        Assert.Equal(new[] { "FACT-MEN-ANIMAL-FOREKNOWLEDGE" }, note.RevealsFacts);
        Assert.True(result.Registry!.IsRuntimeReady(note.Id));
    }

    [Fact]
    public void ADialogue_Loads()
    {
        var result = Load(Dialogue(TwoNodes));
        TestPacks.AssertLoads(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == ContentSeverity.Warning);
        var talk = result.Registry!.Get<NarrativeDefinition>("core:MEN-01-TALK-001");
        NarrativeDialogue graph = talk.Dialogue!;
        Assert.Equal("start", graph.StartNode);
        Assert.Equal(new[] { "start", "explanation" }, graph.Nodes.Select(n => n.Id));
        Assert.Equal(new[] { "explanation", "__end__" }, graph.Nodes[0].Choices.Select(c => c.To));
        Assert.True(graph.Nodes[1].Ending);
    }

    [Fact]
    public void AChoiceToAMissingNode_IsAnError()
    {
        string nodes = TwoNodes.Replace("to: explanation", "to: explain");
        Assert.Contains("'explain'", TestPacks.AssertError(Load(Dialogue(nodes)), DiagnosticCodes.DialogueGraph).Message);
    }

    [Fact]
    public void AMissingStartNode_IsAnError()
    {
        TestPacks.AssertError(Load(Dialogue(TwoNodes, start: "begin")), DiagnosticCodes.DialogueGraph);
    }

    [Fact]
    public void RepeatedNodeIds_AreAnError()
    {
        string nodes = TwoNodes.Replace("- id: explanation", "- id: start");
        TestPacks.AssertError(Load(Dialogue(nodes)), DiagnosticCodes.DialogueGraph);
    }

    [Fact]
    public void AnUnreachableNode_IsAWarning()
    {
        string nodes = TwoNodes + "\n    - id: orphan\n      text: Nobody hears this.\n      ending: true";
        var result = Load(Dialogue(nodes));
        TestPacks.AssertLoads(result);
        Assert.Contains("orphan", TestPacks.AssertDiagnostic(result, DiagnosticCodes.DialogueShape, ContentSeverity.Warning).Message);
    }

    [Fact]
    public void ADeadEnd_IsAnErrorWhenReady_AndAWarningInADraft()
    {
        // The explanation node is no longer an ending and has no choices.
        string nodes = TwoNodes.Replace("      ending: true", "      ending: false");
        TestPacks.AssertError(Load(Dialogue(nodes)), DiagnosticCodes.Incomplete);
        var draft = Load(Dialogue(nodes, status: "draft"));
        TestPacks.AssertLoads(draft);
        TestPacks.AssertDiagnostic(draft, DiagnosticCodes.Incomplete, ContentSeverity.Warning);
    }

    [Fact]
    public void AnEmptyBody_IsAnErrorWhenReady_AndAWarningInADraft()
    {
        TestPacks.AssertError(Load(Document(body: "")), DiagnosticCodes.Incomplete);
        TestPacks.AssertDiagnostic(Load(Document(status: "draft", body: "")), DiagnosticCodes.Incomplete, ContentSeverity.Warning);
    }

    [Fact]
    public void ADraft_IsNotRuntimeReady()
    {
        var authoring = Load(Document(status: "draft"));
        TestPacks.AssertLoads(authoring);
        Assert.Contains("draft", TestPacks.AssertDiagnostic(authoring, DiagnosticCodes.NotRuntimeReady, ContentSeverity.Note).Message);
        TestPacks.AssertError(Load(Document(status: "draft"), ContentLoadOptions.Runtime), DiagnosticCodes.NotRuntimeReady);
        TestPacks.AssertLoads(Load(Document(), ContentLoadOptions.Runtime));
    }

    [Theory]
    [InlineData("presentation: journal", "presentation: hologram")]
    [InlineData("severity: personal", "severity: urgent")]
    [InlineData("facility: MEN", "facility: Menagerie")]
    [InlineData("kind: world_object", "kind: rumour")]
    [InlineData("kind: world_object", "kind: location")] // the one name for a site binding is site
    [InlineData("- FACT-MEN-ANIMAL-FOREKNOWLEDGE", "- fact.men.foreknowledge")]
    public void BadNarrativeValues_AreRejected(string before, string after)
    {
        TestPacks.AssertError(Load(Document().Replace(before, after)), DiagnosticCodes.BadValue);
    }

    [Fact]
    public void FormAndContent_MustAgree()
    {
        TestPacks.AssertError(Load(Document().Replace("form: document", "form: dialogue")), DiagnosticCodes.BadValue);
        TestPacks.AssertError(Load(Document(extra: "dialogue:\n  start_node: start\n")), DiagnosticCodes.BadValue);
        TestPacks.AssertError(Load(Document(extra: "name: The North Enclosure\n")), DiagnosticCodes.BadValue);
    }

    [Fact]
    public void ItemAndDeviceBindings_AreResolved()
    {
        string bound = Document().Replace("kind: world_object\n  ref: MEN-OBSERVATION-BLIND", "kind: device\n  ref: DEV-ADM-001");
        Assert.Contains("unregistered device core:DEV-ADM-001 (source.ref)",
            TestPacks.AssertError(Load(bound), DiagnosticCodes.UnresolvedReference).Message);
    }

    [Fact]
    public void ABindingWithoutARef_IsIncomplete()
    {
        string unbound = Document().Replace("  ref: MEN-OBSERVATION-BLIND\n", "");
        TestPacks.AssertError(Load(unbound), DiagnosticCodes.Incomplete);
        TestPacks.AssertLoads(Load(unbound.Replace("kind: world_object", "kind: unbound")));
    }
}
