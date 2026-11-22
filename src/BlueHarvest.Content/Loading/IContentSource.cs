#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// Where content files come from: the filesystem for the runner and tests, Godot's resources for the game (P1b).
/// Every source must give the same paths and bytes for the same packs, so they load the same registry and
/// fingerprint.
/// </summary>
public interface IContentSource
{
    /// <summary>What it reads, for messages: a folder path, say.</summary>
    string Description { get; }

    IEnumerable<ContentFile> Enumerate();
}
