using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>Authoring-valid versus runtime-ready: handlers named in content must be known and implemented.</summary>
public class HandlerTests
{
    static readonly HandlerRegistry AllImplemented =
        HandlerRegistry.Default.WithImplemented("process.transform", "process.repair", "resource.harvest", "device.install_and_activate");

    [Fact]
    public void AKnownUnimplementedHandler_IsANote_WhenAuthoring()
    {
        var result = TestPacks.LoadCore(TestPacks.Slice());
        TestPacks.AssertLoads(result);
        ContentDiagnostic d = TestPacks.AssertDiagnostic(result, DiagnosticCodes.NotRuntimeReady, ContentSeverity.Note);
        Assert.StartsWith("NOT_RUNTIME_READY: core:RCP-022", d.Message);
        Assert.Contains("process.transform", d.Message);
        Assert.Equal(new[] { new ContentId("core:RCP-022") }, result.Registry!.NotRuntimeReady);
        Assert.False(result.Registry!.IsRuntimeReady(new ContentId("core:RCP-022")));
        Assert.True(result.Registry!.IsRuntimeReady(new ContentId("core:ITM-CRF-004")));
    }

    [Fact]
    public void AKnownUnimplementedHandler_FailsARuntimeLoad()
    {
        var result = TestPacks.Load(TestPacks.Core(TestPacks.Slice()), ContentLoadOptions.Runtime);
        TestPacks.AssertError(result, DiagnosticCodes.NotRuntimeReady);
        Assert.Null(result.Registry);
    }

    [Fact]
    public void ImplementedHandlers_AreRuntimeReady()
    {
        var options = ContentLoadOptions.Runtime with { Handlers = AllImplemented };
        var result = TestPacks.Load(TestPacks.Core(TestPacks.Slice()), options);
        TestPacks.AssertLoads(result);
        Assert.Empty(result.Registry!.NotRuntimeReady);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.NotRuntimeReady);
    }

    [Fact]
    public void AnUnknownHandler_IsAuthoringValid_ButNotRuntimeReady()
    {
        var files = new[] { ("items/flashlight.yaml", TestPacks.Item("flashlight", "handlers: [light.battery]\n")) };
        var authoring = TestPacks.LoadCore(files);
        TestPacks.AssertLoads(authoring);
        Assert.Contains("light.battery, which this build doesn't know",
            TestPacks.AssertDiagnostic(authoring, DiagnosticCodes.NotRuntimeReady, ContentSeverity.Note).Message);
        TestPacks.AssertError(TestPacks.Load(TestPacks.Core(files), ContentLoadOptions.Runtime), DiagnosticCodes.NotRuntimeReady);
    }

    [Fact]
    public void AHandlerOnTheWrongKind_IsAnError()
    {
        string recipe = TestPacks.Recipe("RCP-022", handler: "resource.harvest");
        var result = TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe)));
        Assert.Contains("resource.harvest is a resource source handler",
            TestPacks.AssertError(result, DiagnosticCodes.HandlerMismatch).Message);
    }

    [Fact]
    public void AHandlersDataContract_IsChecked()
    {
        // process.repair works on targets; this recipe has only outputs.
        string recipe = TestPacks.Recipe("RCP-061", handler: "process.repair");
        var result = TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe)));
        Assert.Contains("needs at least one target", TestPacks.AssertError(result, DiagnosticCodes.HandlerMismatch).Message);

        string repair = TestPacks.Recipe("RCP-061", handler: "process.repair")
            .Replace("outputs:", "targets:");
        TestPacks.AssertLoads(TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", repair))));
    }

    [Fact]
    public void HandlerKeys_MustLookLikeHandlerKeys()
    {
        string recipe = TestPacks.Recipe("RCP-022", handler: "Process.Transform");
        TestPacks.AssertError(TestPacks.LoadCore(TestPacks.Slice()[..3].With(("recipes/r.yaml", recipe))), DiagnosticCodes.BadValue);
    }

    [Fact]
    public void TheDefaultRegistry_ImplementsNothingYet()
    {
        Assert.Equal(new[] { "device.install_and_activate", "process.repair", "process.transform", "resource.harvest" },
            HandlerRegistry.Default.Contracts.Select(c => c.Key));
        Assert.All(HandlerRegistry.Default.Contracts, c => Assert.False(c.Implemented));
        Assert.Throws<ArgumentException>(() => HandlerRegistry.Default.WithImplemented("no.such"));
    }
}
