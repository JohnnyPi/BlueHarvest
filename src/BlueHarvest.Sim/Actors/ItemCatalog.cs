using BlueHarvest.Content;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Actors;

/// <summary>Numbers for carrying. Weights in grams, volumes in millilitres. First guesses, to tune.</summary>
public static class ItemRules
{
    /// <summary>Pockets hold this much in all, and nothing bigger than <see cref="PocketItemMl"/> each.</summary>
    public const int PocketMl = 2000, PocketItemMl = 500;

    /// <summary>Up to this you carry freely; above it, sprinting costs more and climbing is slower.</summary>
    public const int BurdenedGrams = 10_000;

    /// <summary>Above this: no sprinting, slower walking, no climbing.</summary>
    public const int HeavyGrams = 20_000;

    /// <summary>Nobody carries more than this: you can't pick up what would take you over it.</summary>
    public const int MaxCarryGrams = 35_000;

    /// <summary>Burdened: a sprint step costs this much more (120% of the usual sprint).</summary>
    public const int BurdenedSprintPercent = 120;

    /// <summary>Burdened: a climb step costs this much more.</summary>
    public const int BurdenedClimbPercent = 125;

    /// <summary>Heavily laden: a walking step costs this much more.</summary>
    public const int HeavyWalkPercent = 115;

    /// <summary>Picking something up: 1 s, a second more if it's over 5 kg and another to stow it in a bag; searching a body takes longer.</summary>
    public const int PickUpTicks = 100, HeavyItemGrams = 5_000, HeavyPickUpTicks = 100, StowTicks = 100, FromBodyTicks = 50;

    public const int DropTicks = 50, PutTicks = 80;

    /// <summary>Handling things: the rustle of picking up or stowing, the thump of putting down.</summary>
    public const int PickUpLoudness = 8, DropLoudness = 5;

    /// <summary>A flashlight's batteries lose 1% every this many ticks while it's on: about ten game hours on a fresh pair.</summary>
    public const int LightTicksPerPercent = 3600;

    /// <summary>Loading: each round or shell pressed in.</summary>
    public const int LoadTicksPerRound = 40;

    /// <summary>Fitting a battery or a magazine; taking something out of what it's in.</summary>
    public const int FitTicks = 150, TakeOutTicks = 100;

    /// <summary>Forcing a lock with a crowbar: slow and loud.</summary>
    public const int PryTicks = 600, PryLoudness = 30;

    /// <summary>Unlocking with the right key or card.</summary>
    public const int UnlockTicks = 50;

    /// <summary>A radio's hiss when you listen.</summary>
    public const int RadioLoudness = 12;

    /// <summary>How heavily laden a weight makes you.</summary>
    public static Load LoadOf(int grams) =>
        grams > HeavyGrams ? Load.Heavy : grams > BurdenedGrams ? Load.Burdened : Load.Light;
}

/// <summary>How laden someone is (see <see cref="ItemRules"/>).</summary>
public enum Load : byte { Light, Burdened, Heavy }

/// <summary>One item, or a run of the same, to place: what map files and loot plans say.</summary>
/// <param name="Count">How many: one stack of that many for stacking kinds, that many separate items otherwise.</param>
/// <param name="Amount">How full (non-stacking kinds); the kind's starting amount if left out.</param>
/// <param name="Inside">Goes into the container placed just before it on the same tile.</param>
/// <param name="Empty">Made empty: no fill, and nothing fitted (a flashlight without its batteries).</param>
public sealed record ItemSpec(ContentId Kind, int Count = 1, int? Amount = null, string? Tag = null, int Level = 0,
    bool Locked = false, bool Open = false, bool Inside = false, bool Empty = false);

/// <summary>
/// Every kind of item, built from the content registry's item definitions (P2b): the game, the runner and the tests
/// each install their registry once (<see cref="Install"/>) before making a simulation. There are no item kinds in
/// code any more: an item is its content ID, and what it does is the handlers it names.
/// <para>
/// One catalog per process for now: every simulation shares it, so a registry must be the same one (the same
/// fingerprint) for as long as simulations exist. P3's <c>WorldSession</c> will own it, with the pack lock.
/// </para>
/// </summary>
public static class ItemCatalog
{
    private sealed record Snapshot(string Fingerprint, IReadOnlyList<ItemDef> All, IReadOnlyDictionary<ContentId, ItemDef> ById);

    private static readonly object Gate = new();
    private static volatile Snapshot? _current;

    private static Snapshot Current => _current ?? throw new InvalidOperationException(
        "No item catalog: load the content packs and call ItemCatalog.Install(registry) before making a simulation.");

    /// <summary>Whether a registry's items are installed.</summary>
    public static bool IsInstalled => _current is not null;

    /// <summary>The installed registry's fingerprint, or null.</summary>
    public static string? Fingerprint => _current?.Fingerprint;

    /// <summary>
    /// Builds the catalog from <paramref name="registry"/>'s item definitions and makes it the one every simulation
    /// uses. Installing the same registry again (the same fingerprint) does nothing. Throws if an item isn't
    /// runtime-ready (a handler the sim doesn't implement) or can't be read as a sim item.
    /// </summary>
    public static void Install(ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        lock (Gate)
        {
            if (_current?.Fingerprint == registry.Fingerprint)
                return;
            _current = Build(registry);
        }
    }

    private static Snapshot Build(ContentRegistry registry)
    {
        var items = registry.All<ItemDefinition>().OrderBy(d => d.Id).ToList();
        var notReady = items.Where(d => !registry.IsRuntimeReady(d.Id)).Select(d => d.Id.Value).ToList();
        if (notReady.Count > 0)
            throw new InvalidOperationException(
                $"These items aren't runtime-ready, so the sim can't use them: {string.Join(", ", notReady)}. Run 'content validate --runtime'.");
        var defs = items.Select(ItemDefBuilder.From).ToList();
        var byId = defs.ToDictionary(d => d.Id);
        return new Snapshot(registry.Fingerprint, defs.AsReadOnly(), byId);
    }

    /// <summary>The item with this ID. Throws if there's none: code that names an item expects it to exist.</summary>
    public static ItemDef Of(ContentId id) =>
        Current.ById.TryGetValue(id, out var def) ? def : throw new KeyNotFoundException($"There's no item {id} in the content packs.");

    /// <summary>Every item, by ID.</summary>
    public static IReadOnlyList<ItemDef> All => Current.All;

    /// <summary>The item with this ID, or null.</summary>
    public static ItemDef? Find(ContentId id) => Current.ById.TryGetValue(id, out var def) ? def : null;

    /// <summary>
    /// The item with this ID as text (<c>core:flashlight</c>), or null. A bare ID (<c>flashlight</c>) takes
    /// <paramref name="defaultNamespace"/>, if one is given; otherwise it must be qualified. IDs are case-sensitive.
    /// </summary>
    public static ItemDef? Find(string text, string? defaultNamespace = null)
    {
        ContentId id;
        bool parsed = defaultNamespace is null ? ContentId.TryParse(text, out id) : ContentId.TryResolve(text, defaultNamespace, out id);
        return parsed ? Find(id) : null;
    }

    /// <summary>
    /// Reads an item spec: items separated by ';', each "[>]id [xN] [amount=N] [tag=T] [level=N] [locked] [open] [empty]".
    /// A leading '>' puts the item inside the container before it. "core:locker locked; >core:flashlight; >core:energy-bar x3".
    /// IDs must be qualified unless <paramref name="defaultNamespace"/> is given (a pack's own tables, where a bare
    /// ID means that pack's, as in YAML); map files always qualify them.
    /// </summary>
    public static IReadOnlyList<ItemSpec> ParseSpec(string text, string? defaultNamespace = null)
    {
        var specs = new List<ItemSpec>();
        foreach (string rawPart in text.Split(';'))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
                continue;
            bool inside = part.StartsWith('>');
            if (inside)
                part = part[1..].TrimStart();
            string[] tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                throw new FormatException($"An empty item in '{text}'.");
            var def = Find(tokens[0], defaultNamespace) ?? throw new FormatException(UnknownItem(tokens[0], defaultNamespace));
            var spec = new ItemSpec(def.Id, Inside: inside);
            foreach (string token in tokens.Skip(1))
            {
                int eq = token.IndexOf('=');
                string key = eq < 0 ? token : token[..eq];
                string value = eq < 0 ? "" : token[(eq + 1)..];
                spec = key.ToLowerInvariant() switch
                {
                    "locked" => spec with { Locked = true },
                    "open" => spec with { Open = true },
                    "empty" => spec with { Empty = true },
                    "tag" when value.Length > 0 => spec with { Tag = value },
                    "amount" when int.TryParse(value, out int amount) && amount >= 0 && amount <= def.MaxAmount => spec with { Amount = amount },
                    "level" when int.TryParse(value, out int level) && level is >= 0 and <= 3 => spec with { Level = level },
                    _ when eq < 0 && token.Length > 1 && (token[0] is 'x' or 'X') && int.TryParse(token[1..], out int count) && count >= 1 && count <= 100
                        => spec with { Count = count },
                    _ => throw new FormatException($"Can't read '{token}' in item '{part}'."),
                };
            }
            if (spec.Inside && specs.Count == 0)
                throw new FormatException($"'{part}' goes inside something, but nothing comes before it.");
            specs.Add(spec);
        }
        return specs;
    }

    private static string UnknownItem(string text, string? defaultNamespace)
    {
        if (defaultNamespace is null && text.IndexOf(':') < 0 && Find(text, "core") is not null)
            return $"Unknown item '{text}': item IDs must be qualified, 'core:{text}'.";
        return $"Unknown item '{text}'.";
    }

    /// <summary>Writes specs back in the form <see cref="ParseSpec"/> reads, with qualified IDs.</summary>
    public static string FormatSpec(IEnumerable<ItemSpec> specs) => string.Join("; ", specs.Select(s =>
    {
        var parts = new List<string> { (s.Inside ? ">" : "") + s.Kind.Value };
        if (s.Count != 1) parts.Add($"x{s.Count}");
        if (s.Amount is { } amount) parts.Add($"amount={amount}");
        if (s.Tag is { } tag) parts.Add($"tag={tag}");
        if (s.Level != 0) parts.Add($"level={s.Level}");
        if (s.Locked) parts.Add("locked");
        if (s.Open) parts.Add("open");
        if (s.Empty) parts.Add("empty");
        return string.Join(' ', parts);
    }));
}
