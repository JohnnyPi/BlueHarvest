#nullable enable
using System.Collections.Generic;
using System.Linq;
using BlueHarvest.Content;
using Godot;

namespace BlueHarvest.Game.ContentIntegration;

/// <summary>
/// Content packs read through Godot's own file access, from <c>res://Content/packs</c>: the copy of the repo's
/// <c>content/packs</c> that the build makes (the CopyContentPacks target in BlueHarvest.csproj). It gives the same
/// relative paths and bytes as <see cref="FileSystemContentSource"/> on <c>content/packs</c>, so both load the
/// same registry and fingerprint. Hidden files are skipped, as there.
/// In an exported game the YAML files are in the .pck only if the export preset includes <c>*.yaml</c> among
/// its non-resource files.
/// </summary>
public sealed class GodotContentSource : IContentSource
{
    public const string DefaultRoot = "res://Content/packs";

    private readonly string _root;

    public GodotContentSource(string root = DefaultRoot)
    {
        _root = root.TrimEnd('/');
    }

    public string Description => _root;

    public IEnumerable<ContentFile> Enumerate()
    {
        var found = new List<string>();
        Collect("", found);
        foreach (string relative in found.OrderBy(p => p, System.StringComparer.Ordinal))
        {
            string path = _root + "/" + relative;
            yield return new ContentFile(relative, LengthOf(path), () => Godot.FileAccess.GetFileAsBytes(path));
        }
    }

    private void Collect(string relative, List<string> found)
    {
        string folder = relative.Length == 0 ? _root : _root + "/" + relative;
        if (!DirAccess.DirExistsAbsolute(folder))
            return;
        foreach (string file in DirAccess.GetFilesAt(folder))
            found.Add(relative.Length == 0 ? file : relative + "/" + file);
        foreach (string sub in DirAccess.GetDirectoriesAt(folder))
            Collect(relative.Length == 0 ? sub : relative + "/" + sub, found);
    }

    private static long LengthOf(string path)
    {
        using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        return file is null ? 0 : (long)file.GetLength();
    }
}
