#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// The content library's own YAML tree, built by <see cref="StrictYamlReader"/>. Scalars stay as text: nothing is
/// typed implicitly (no dates, no YAML 1.1 booleans), so a field decides what its text means.
/// </summary>
internal abstract class YamlNode
{
    protected YamlNode(int line, int column)
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }
    public int Column { get; }

    /// <summary>Nodes in this subtree, counting what aliases inside it stand for. Used for the expansion limit.</summary>
    public long Weight { get; set; } = 1;

    public abstract string Describe { get; }
}

internal sealed class YamlScalar : YamlNode
{
    public YamlScalar(string value, bool plain, int line, int column) : base(line, column)
    {
        Value = value;
        Plain = plain;
    }

    public string Value { get; }

    /// <summary>Written unquoted (not quoted and not a block scalar). Only plain scalars can be numbers, booleans or null.</summary>
    public bool Plain { get; }

    /// <summary>An empty plain value, <c>~</c> or <c>null</c>: the field counts as absent.</summary>
    public bool IsNull => Plain && Value is "" or "~" or "null" or "Null" or "NULL";

    public override string Describe => IsNull ? "nothing" : "a value";
}

internal sealed class YamlSequence : YamlNode
{
    public YamlSequence(int line, int column) : base(line, column) { }

    public List<YamlNode> Items { get; } = new();

    public override string Describe => "a list";
}

internal sealed class YamlMapping : YamlNode
{
    public YamlMapping(int line, int column) : base(line, column) { }

    /// <summary>In file order. Keys are unique (the reader rejects duplicates).</summary>
    public List<KeyValuePair<YamlScalar, YamlNode>> Entries { get; } = new();

    public YamlNode? Get(string key)
    {
        foreach (var entry in Entries)
            if (entry.Key.Value == key)
                return entry.Value;
        return null;
    }

    public YamlScalar? KeyNode(string key)
    {
        foreach (var entry in Entries)
            if (entry.Key.Value == key)
                return entry.Key;
        return null;
    }

    public override string Describe => "a mapping";
}
