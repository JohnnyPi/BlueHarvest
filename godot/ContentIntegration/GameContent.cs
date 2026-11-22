#nullable enable
using System.Linq;
using BlueHarvest.Content;
using BlueHarvest.Sim;
using Godot;

namespace BlueHarvest.Game.ContentIntegration;

/// <summary>
/// The game's content: the packs in <c>res://Content/packs</c>, loaded once per run, the first time anything asks.
/// Every diagnostic goes to Godot's Output panel (errors and warnings to the debugger too).
/// <para>
/// Run from the editor, it also loads the repo's <c>content/packs</c> from disk and checks that the fingerprint is
/// the same: the P1b gate. A difference means the copy is stale; building the project recopies it.
/// </para>
/// <para>
/// The load is in authoring mode for now: recipes, sources and devices name handlers that arrive in P5, so a
/// runtime load would refuse them. Nothing in the game reads the registry yet; P3's WorldSession will own it.
/// </para>
/// </summary>
public static class GameContent
{
    private static ContentLoadResult? _result;

    /// <summary>The load's result, loading on first use.</summary>
    public static ContentLoadResult Result => _result ??= Load();

    public static ContentRegistry? Registry => Result.Registry;

    /// <summary>
    /// True when the repo's content/packs has the same fingerprint, false when it differs, null when it couldn't
    /// be checked (an exported game, or no content/packs next to the project).
    /// </summary>
    public static bool? MatchesSource { get; private set; }

    public static ContentLoadOptions Options => ContentLoadOptions.Authoring with
    {
        GameVersion = SemVersion.TryParse(SimInfo.Version, out SemVersion version) ? version : null,
    };

    /// <summary>One line for the front menu.</summary>
    public static string Summary
    {
        get
        {
            ContentLoadResult result = Result;
            int errors = result.Diagnostics.Count(d => d.Severity == ContentSeverity.Error);
            if (result.Registry is not { } registry)
                return $"Content didn't load: {errors} error(s); see the Output panel.";
            int packs = registry.Packs.Count;
            string check = MatchesSource switch
            {
                true => " · same as content/packs",
                false => " · DIFFERS from content/packs: rebuild to recopy",
                null => "",
            };
            return $"Content: {packs} pack{(packs == 1 ? "" : "s")} · {registry.Count} definitions · " +
                $"{registry.Fingerprint[..19]}…{check}";
        }
    }

    /// <summary>Whether the menu should show the summary as a problem.</summary>
    public static bool Healthy => Result.Succeeded && MatchesSource != false;

    private static ContentLoadResult Load()
    {
        ContentLoadResult result = ContentLoader.LoadFrom(new GodotContentSource(), Options);
        foreach (ContentDiagnostic d in result.Diagnostics)
        {
            if (d.Severity == ContentSeverity.Error)
                GD.PushError(d.ToString());
            else if (d.Severity == ContentSeverity.Warning)
                GD.PushWarning(d.ToString());
            else
                GD.Print(d.ToString());
        }
        MatchesSource = CompareWithSource(result);
        _result = result;
        GD.Print(result.Registry is { } registry
            ? $"Content: {registry.Packs.Count} pack(s), {registry.Count} definitions, {registry.NotRuntimeReady.Count} not runtime-ready, {registry.Fingerprint}"
            : $"Content: FAILED to load from {GodotContentSource.DefaultRoot}.");
        return result;
    }

    private static bool? CompareWithSource(ContentLoadResult fromGodot)
    {
        if (!OS.HasFeature("editor") || fromGodot.Registry is not { } godotRegistry)
            return null;
        string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "content", "packs"));
        if (!System.IO.Directory.Exists(folder))
            return null;
        ContentLoadResult fromDisk = ContentLoader.LoadFrom(new FileSystemContentSource(folder), Options);
        if (fromDisk.Registry is not { } diskRegistry)
        {
            GD.PushError($"Content: {folder} doesn't load on its own; run content validate on it.");
            return false;
        }
        if (diskRegistry.Fingerprint == godotRegistry.Fingerprint)
        {
            GD.Print($"Content: the same fingerprint as {folder}.");
            return true;
        }
        GD.PushError($"Content: res://Content/packs has {godotRegistry.Fingerprint}, but {folder} has {diskRegistry.Fingerprint}. " +
            "The copy is stale: build the project to recopy it.");
        return false;
    }
}
