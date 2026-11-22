#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// Content from a folder: a packs folder (<c>content/packs</c>, one pack per subfolder) or a single pack's folder
/// (the one holding <c>pack.yaml</c>). Skips hidden and system files and anything behind a link, so nothing outside
/// the folder is read.
/// </summary>
public sealed class FileSystemContentSource : IContentSource
{
    readonly string root;

    public FileSystemContentSource(string root)
    {
        this.root = System.IO.Path.GetFullPath(root);
    }

    public string Description => root;

    public IEnumerable<ContentFile> Enumerate()
    {
        if (!Directory.Exists(root))
            yield break;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
        };
        var paths = Directory.EnumerateFiles(root, "*", options)
            .Select(f => (full: f, relative: System.IO.Path.GetRelativePath(root, f).Replace('\\', '/')))
            .OrderBy(p => p.relative, StringComparer.Ordinal)
            .ToList();
        foreach (var (full, relative) in paths)
            yield return new ContentFile(relative, new FileInfo(full).Length, () => File.ReadAllBytes(full));
    }
}
