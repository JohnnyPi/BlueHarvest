#nullable enable
using System.Linq;
using BlueHarvest.Content;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.ContentIntegration;
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
/// runtime load would refuse them. The sim's items come from it (P2b): a successful load installs them in the
/// item catalog (<see cref="SimContent.LoadForPlay"/>), and nothing can be played without that
/// (<see cref="RequireItems"/>). P3's WorldSession will own the registry.
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

    /// <summary>The sim's handlers and the game version (<see cref="SimContent"/>), in authoring mode until P5.</summary>
    public static ContentLoadOptions Options => SimContent.Options(ContentMode.Authoring);

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
            string items = ItemCatalog.IsInstalled ? $" · {ItemCatalog.All.Count} items" : " · items NOT installed: see the Output panel";
            return $"Content: {packs} pack{(packs == 1 ? "" : "s")} · {registry.Count} definitions{items} · " +
                $"{registry.Fingerprint[..19]}…{check}";
        }
    }

    /// <summary>Whether the menu should show the summary as a problem.</summary>
    public static bool Healthy => ItemsReady && MatchesSource != false;

    /// <summary>Whether the items are in the catalog, loading the content first if it hasn't been.</summary>
    public static bool ItemsReady => Result.Succeeded && ItemCatalog.IsInstalled;

    /// <summary>Loads the content if it hasn't been, and throws, saying why, if the items didn't come with it.</summary>
    public static void RequireItems()
    {
        if (!ItemsReady)
            throw new System.InvalidOperationException(
                $"The items come from the content packs, and they didn't load ({Summary}). See the Output panel; building the project recopies the packs.");
    }

    private static ContentLoadResult Load()
    {
        ContentLoadResult result;
        try
        {
            result = SimContent.LoadForPlay(new GodotContentSource());
        }
        catch (System.InvalidOperationException e)
        {
            // the packs loaded, but their items can't go in the catalog (an item the sim can't use)
            GD.PushError($"Content: the items couldn't be installed: {e.Message}");
            result = ContentLoader.LoadFrom(new GodotContentSource(), Options);
        }
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
