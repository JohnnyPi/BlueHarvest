#nullable enable
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace BlueHarvest.Content;

/// <summary>
/// The handler keys the game knows. Built once and never changed; <see cref="WithImplemented"/> makes a new one.
/// Part of the fingerprint, so a pack set loaded by a build with different handlers fingerprints differently.
/// </summary>
public sealed class HandlerRegistry
{
    readonly FrozenDictionary<string, HandlerContract> byKey;

    public HandlerRegistry(IEnumerable<HandlerContract> contracts)
    {
        var map = new Dictionary<string, HandlerContract>(StringComparer.Ordinal);
        foreach (HandlerContract c in contracts)
        {
            if (!IdRules.IsHandler(c.Key))
                throw new ArgumentException($"'{c.Key}' isn't a handler key (dotted lower case, like process.transform).", nameof(contracts));
            if (!ContentKinds.All.Contains(c.Kind))
                throw new ArgumentException($"Handler {c.Key} names kind '{c.Kind}', which isn't a content kind.", nameof(contracts));
            if (!map.TryAdd(c.Key, c))
                throw new ArgumentException($"Handler {c.Key} is registered twice.", nameof(contracts));
        }
        byKey = map.ToFrozenDictionary(StringComparer.Ordinal);
        Contracts = Array.AsReadOnly(map.Values.OrderBy(c => c.Key, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Sorted by key.</summary>
    public IReadOnlyList<HandlerContract> Contracts { get; }

    public bool TryGet(string key, [NotNullWhen(true)] out HandlerContract? contract) => byKey.TryGetValue(key, out contract);

    /// <summary>A copy with these keys marked implemented: for tests now, and for each phase that adds an executor.</summary>
    public HandlerRegistry WithImplemented(params string[] keys)
    {
        foreach (string key in keys)
            if (!byKey.ContainsKey(key))
                throw new ArgumentException($"No handler {key} to mark implemented.", nameof(keys));
        return new HandlerRegistry(Contracts.Select(c => keys.Contains(c.Key) ? c with { Implemented = true } : c));
    }

    /// <summary>
    /// The contracts known at P1a. None is implemented yet: processing, gathering and devices arrive in P5, so
    /// recipes, sources and devices are authoring-valid and not runtime-ready until then.
    /// </summary>
    public static HandlerRegistry Default { get; } = new(new[]
    {
        new HandlerContract("process.transform", ContentKinds.Recipe, false,
            "Consumes inputs and makes new outputs (P5b).", CheckTransform),
        new HandlerContract("process.repair", ContentKinds.Recipe, false,
            "Services targets in place: the same item instance, better condition (P5c).", CheckRepair),
        new HandlerContract("resource.harvest", ContentKinds.ResourceSource, false,
            "Gathers bounded yields from a placed source instance (P5a)."),
        new HandlerContract("device.install_and_activate", ContentKinds.Device, false,
            "Takes parts from inventory, then activates under its power gate (P5d).", CheckDevice),
    });

    static string? CheckTransform(ContentDefinition d)
    {
        if (d is not RecipeDefinition r)
            return null;
        if (r.Outputs.Count == 0)
            return "process.transform needs at least one output.";
        if (r.Targets.Count > 0)
            return "process.transform doesn't take targets; a recipe that changes an item in place uses process.repair.";
        return null;
    }

    static string? CheckRepair(ContentDefinition d) =>
        d is RecipeDefinition { Targets.Count: 0 } ? "process.repair needs at least one target (the item it services)." : null;

    static string? CheckDevice(ContentDefinition d) =>
        d is DeviceDefinition dev && !dev.States.Contains("active")
            ? "device.install_and_activate needs an 'active' state."
            : null;
}
