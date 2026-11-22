#nullable enable
namespace BlueHarvest.Content;

/// <summary>Content held in memory: tests, and tools that build packs without writing them out.</summary>
public sealed class InMemoryContentSource : IContentSource
{
    readonly List<ContentFile> files = new();

    public InMemoryContentSource(string description = "(memory)")
    {
        Description = description;
    }

    public string Description { get; }

    public InMemoryContentSource Add(string path, string text)
    {
        files.Add(ContentFile.FromText(path, text));
        return this;
    }

    public InMemoryContentSource Add(string path, byte[] bytes)
    {
        files.Add(ContentFile.FromBytes(path, bytes));
        return this;
    }

    public IEnumerable<ContentFile> Enumerate() => files;
}
