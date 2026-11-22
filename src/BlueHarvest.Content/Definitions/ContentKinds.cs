#nullable enable
namespace BlueHarvest.Content;

/// <summary>The <c>kind:</c> values a <c>blueharvest.content/v1</c> file can have.</summary>
public static class ContentKinds
{
    public const string Item = "item";
    public const string Recipe = "recipe";
    public const string Station = "station";
    public const string ResourceSource = "resource_source";
    public const string Device = "device";
    public const string Narrative = "narrative";

    /// <summary>All of them, in the order the loader lists them in messages.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Item, Recipe, Station, ResourceSource, Device, Narrative });

    /// <summary>How a kind reads in a sentence: "resource source", not "resource_source".</summary>
    public static string Label(string kind) => kind.Replace('_', ' ');

    /// <summary>The label with its article: "an item", "a recipe".</summary>
    public static string WithArticle(string kind) => (kind == Item ? "an " : "a ") + Label(kind);
}
