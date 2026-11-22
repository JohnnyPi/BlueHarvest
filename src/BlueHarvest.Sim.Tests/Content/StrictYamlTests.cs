using System.Text;
using BlueHarvest.Content;

namespace BlueHarvest.Sim.Tests.ContentLibrary;

/// <summary>The reader refuses YAML that's unsafe or ambiguous, and says where.</summary>
public class StrictYamlTests
{
    const string Head = "schema: blueharvest.content/v1\nkind: item\nid: ITM-CRF-004\n";

    static ContentLoadResult LoadItem(string yaml, ContentLoadOptions? options = null) =>
        TestPacks.Load(TestPacks.Core(("items/x.yaml", yaml)), options);

    [Theory]
    [InlineData(Head + "name: A\nname: B\ncategory: material\nmass_g: 1\n", DiagnosticCodes.DuplicateKey)]
    [InlineData(Head + "name: !!str Vine Fiber\ncategory: material\nmass_g: 1\n", DiagnosticCodes.YamlTag)]
    [InlineData(Head + "name: !ruby/object:Gem::Installer x\ncategory: material\nmass_g: 1\n", DiagnosticCodes.YamlTag)]
    [InlineData(Head + "name: ! plain\ncategory: material\nmass_g: 1\n", DiagnosticCodes.YamlTag)]
    [InlineData(Head + "name: *nothing\ncategory: material\nmass_g: 1\n", DiagnosticCodes.YamlAlias)]
    [InlineData(Head + "name: A\ncategory: material\nmass_g: 1\n---\nname: B\n", DiagnosticCodes.DocumentCount)]
    [InlineData(Head + "? [a, b]\n: x\n", DiagnosticCodes.ComplexKey)]
    [InlineData(Head + "name: [unclosed\n", DiagnosticCodes.YamlSyntax)]
    [InlineData("", DiagnosticCodes.DocumentCount)]
    public void UnsafeOrAmbiguousYaml_IsRejected(string yaml, string code)
    {
        TestPacks.AssertError(LoadItem(yaml), code);
    }

    [Fact]
    public void AliasExpansion_StopsAtTheNodeLimit()
    {
        // "Billion laughs": each level is ten aliases of the one before, so the fifth is over 100,000 nodes.
        var yaml = new StringBuilder(Head + "name: A\ncategory: material\nmass_g: 1\nlaughs:\n  a: &a [x, x, x, x, x, x, x, x, x, x]\n");
        foreach (var (name, previous) in new[] { ("b", "a"), ("c", "b"), ("d", "c"), ("e", "d"), ("f", "e") })
            yaml.Append($"  {name}: &{name} [{string.Join(", ", Enumerable.Repeat("*" + previous, 10))}]\n");
        ContentDiagnostic d = TestPacks.AssertError(LoadItem(yaml.ToString()), DiagnosticCodes.YamlAlias);
        Assert.Contains("100000", d.Message);
    }

    [Fact]
    public void AliasesWithinTheLimit_Load()
    {
        var result = LoadItem(Head + "name: &n Vine Fiber\ndescription: *n\ncategory: material\nmass_g: 150\n");
        TestPacks.AssertLoads(result);
        var item = result.Registry!.Get<ItemDefinition>("core:ITM-CRF-004");
        Assert.Equal("Vine Fiber", item.Description);
    }

    [Fact]
    public void DeepNesting_IsRejected()
    {
        string yaml = Head + "name: A\ncategory: material\nmass_g: 1\ndeep: " + new string('[', 40) + new string(']', 40) + "\n";
        TestPacks.AssertError(LoadItem(yaml), DiagnosticCodes.YamlTooDeep);
    }

    [Fact]
    public void OversizeFiles_AreRejectedBeforeReading()
    {
        // The manifest fits in 200 bytes; the item doesn't.
        var options = ContentLoadOptions.Authoring with { Limits = new YamlLimits { MaxFileBytes = 200 } };
        string yaml = TestPacks.Item("ITM-CRF-004", "description: " + new string('x', 150) + "\n");
        ContentDiagnostic d = TestPacks.AssertError(LoadItem(yaml, options), DiagnosticCodes.FileTooLarge);
        Assert.Equal("core/items/x.yaml", d.File);
    }

    [Fact]
    public void InvalidUtf8_IsRejected()
    {
        var source = TestPacks.Core();
        source.Add("core/items/x.yaml", Encoding.UTF8.GetBytes(Head + "name: A").Concat(new byte[] { 0xC3, 0x28, 0x0A }).ToArray());
        TestPacks.AssertError(TestPacks.Load(source), DiagnosticCodes.NotUtf8);
    }

    [Fact]
    public void Diagnostics_GiveTheLineAndColumn()
    {
        ContentDiagnostic d = TestPacks.AssertError(LoadItem(Head + "name: A\ncategory: material\nname: B\nmass_g: 1\n"), DiagnosticCodes.DuplicateKey);
        Assert.Equal("core/items/x.yaml", d.File);
        Assert.Equal(6, d.Line);
        Assert.Equal(1, d.Column);
        Assert.Contains("line 4", d.Message);
        Assert.StartsWith("BH-CONTENT-006 ERROR core/items/x.yaml:6:1", d.ToString());
    }

    [Fact]
    public void ByteOrderMarksAndCrlf_ReadTheSame()
    {
        string lf = TestPacks.Item("ITM-CRF-004", "description: |-\n  two\n  lines\n");
        var plain = LoadItem(lf);
        var source = TestPacks.Core();
        source.Add("core/items/x.yaml", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(lf.Replace("\n", "\r\n"))).ToArray());
        var windows = TestPacks.Load(source);
        TestPacks.AssertLoads(plain);
        TestPacks.AssertLoads(windows);
        Assert.Equal("two\nlines", windows.Registry!.Get<ItemDefinition>("core:ITM-CRF-004").Description);
        Assert.Equal(plain.Registry!.Fingerprint, windows.Registry!.Fingerprint);
    }
}
