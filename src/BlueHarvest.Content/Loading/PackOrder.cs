#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// Orders packs so every pack comes after its dependencies. Among packs that are free to go next, the lower
/// <see cref="PackManifest.Priority"/> goes first, then the lower pack ID (ordinal), so the order never depends on
/// how the source lists its files. A pack is left out, with an error, when it needs another game version, when a
/// dependency is missing or the wrong version, when one of its dependencies is left out, or when it's in a cycle.
/// </summary>
internal static class PackOrder
{
    public static List<PackManifest> Sort(IReadOnlyList<PackManifest> manifests, SemVersion? gameVersion, DiagnosticBag diagnostics)
    {
        var byId = manifests.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);

        foreach (PackManifest m in manifests)
        {
            if (gameVersion is { } game && m.RequiresGame is { } needs && !needs.Contains(game))
            {
                Fail(m, DiagnosticCodes.GameVersion, $"Pack {m.Id} requires game {needs}, and this is {game}.");
                continue;
            }
            foreach (PackDependency dep in m.Dependencies)
            {
                if (!byId.TryGetValue(dep.Id, out PackManifest? target))
                    Fail(m, DiagnosticCodes.MissingDependency, $"Pack {m.Id} depends on pack {dep.Id}, which isn't in the pack set.");
                else if (dep.Version is { } range && !range.Contains(target.Version))
                    Fail(m, DiagnosticCodes.DependencyVersion,
                        $"Pack {m.Id} needs {dep.Id} {range}, but the pack set has {dep.Id} {target.Version}.");
            }
        }

        // A pack whose dependency is left out is left out too, until nothing changes.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (PackManifest m in manifests)
            {
                if (failed.Contains(m.Id))
                    continue;
                PackDependency? broken = m.Dependencies.FirstOrDefault(d => failed.Contains(d.Id));
                if (broken is null)
                    continue;
                Fail(m, DiagnosticCodes.MissingDependency, $"Pack {m.Id} isn't loaded because its dependency {broken.Id} isn't.");
                changed = true;
            }
        }

        // Kahn's algorithm over the packs still in.
        var remaining = manifests.Where(m => !failed.Contains(m.Id)).ToList();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<PackManifest>();
        while (remaining.Count > 0)
        {
            PackManifest? next = remaining
                .Where(m => m.Dependencies.All(d => placed.Contains(d.Id)))
                .OrderBy(m => m.Priority)
                .ThenBy(m => m.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
                break;
            order.Add(next);
            placed.Add(next.Id);
            remaining.Remove(next);
        }
        foreach (PackManifest m in remaining.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var waiting = m.Dependencies.Where(d => !placed.Contains(d.Id)).Select(d => d.Id);
            diagnostics.Error(DiagnosticCodes.DependencyCycle, m.ManifestPath, 0, 0, "pack " + m.Id,
                $"Pack {m.Id} is in a dependency cycle (it waits on {string.Join(", ", waiting)}).");
        }
        return order;

        void Fail(PackManifest pack, string code, string message)
        {
            if (failed.Add(pack.Id))
                diagnostics.Error(code, pack.ManifestPath, 0, 0, "pack " + pack.Id, message);
        }
    }
}
