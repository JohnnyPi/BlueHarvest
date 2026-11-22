#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlueHarvest.Content;

/// <summary>
/// Reads the fields of one YAML mapping, each with its expected type, and reports what's wrong with each. Every
/// key read is remembered, so <see cref="Finish"/> can reject the ones the schema doesn't have. A field written as
/// nothing (<c>key:</c>, <c>~</c>, <c>null</c>) counts as absent. Numbers and booleans must be plain (unquoted);
/// text can be written any way.
/// </summary>
internal sealed class FieldReader
{
    static readonly Regex Integer = new(@"^-?(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant);

    /// <summary>Extra help for keys people are likely to write by mistake.</summary>
    static readonly Dictionary<string, string> Hints = new(StringComparer.Ordinal)
    {
        ["duration_seconds"] = "durations name their unit: duration_action_seconds or duration_game_minutes (contracts §5)",
        ["duration"] = "durations name their unit: duration_action_seconds or duration_game_minutes (contracts §5)",
        ["extensions"] = "extensions aren't supported yet",
        ["replaces"] = "a replacement is listed in the pack manifest's replaces, not in the definition",
        ["<<"] = "YAML merge keys aren't supported",
    };

    readonly ParseContext ctx;
    readonly string prefix;
    readonly HashSet<string> used = new(StringComparer.Ordinal);

    public FieldReader(ParseContext ctx, YamlMapping map, string prefix)
    {
        this.ctx = ctx;
        Node = map;
        this.prefix = prefix;
    }

    public YamlMapping Node { get; }

    /// <summary>The field's path for messages: <c>inputs[0].item</c>.</summary>
    public string PathOf(string key) => prefix + key;

    /// <summary>Where a field is, for a diagnostic; the mapping itself when the field is missing.</summary>
    public YamlNode At(string key) => Node.Get(key) ?? Node;

    /// <summary>Whether the field is present with a value. Doesn't count as reading it.</summary>
    public bool Has(string key) => Node.Get(key) is { } n && n is not YamlScalar { IsNull: true };

    /// <summary>The field's node, or null when absent (reported if required).</summary>
    public YamlNode? Raw(string key, bool required)
    {
        used.Add(key);
        YamlNode? n = Node.Get(key);
        if (n is YamlScalar { IsNull: true })
            n = null;
        if (n is null && required)
            ctx.Error(DiagnosticCodes.MissingField, Node, $"{ctx.Who} is missing the required field {PathOf(key)}.");
        return n;
    }

    public string? String(string key, bool required = false, bool allowEmpty = true)
    {
        YamlNode? n = Raw(key, required);
        if (n is null)
            return null;
        if (n is not YamlScalar s)
        {
            ctx.Error(DiagnosticCodes.BadValue, n, $"{PathOf(key)} must be text, not {n.Describe}.");
            return null;
        }
        if (!allowEmpty && s.Value.Trim().Length == 0)
        {
            ctx.Error(DiagnosticCodes.MissingField, s, $"{PathOf(key)} must not be empty.");
            return null;
        }
        return s.Value;
    }

    /// <summary>Text that must pass <paramref name="valid"/>; <paramref name="what"/> describes it ("a handler key such as …").</summary>
    public string? Pattern(string key, bool required, Func<string, bool> valid, string what)
    {
        string? text = String(key, required);
        if (text is null)
            return null;
        if (!valid(text))
        {
            ctx.Error(DiagnosticCodes.BadValue, At(key), $"{PathOf(key)} is '{text}', which isn't {what}.");
            return null;
        }
        return text;
    }

    /// <summary>One of a fixed set of words.</summary>
    public string? Token(string key, bool required, params string[] allowed)
    {
        string? text = String(key, required);
        if (text is null)
            return null;
        if (!allowed.Contains(text))
        {
            ctx.Error(DiagnosticCodes.BadValue, At(key),
                $"{PathOf(key)} is '{text}'; it must be {(allowed.Length == 1 ? allowed[0] : "one of " + string.Join(", ", allowed))}.");
            return null;
        }
        return text;
    }

    public int? Int(string key, bool required, int min, int max)
    {
        YamlNode? n = Raw(key, required);
        if (n is null)
            return null;
        if (n is not YamlScalar { Plain: true } s || !Integer.IsMatch(s.Value))
        {
            ctx.Error(DiagnosticCodes.BadValue, n, $"{PathOf(key)} must be a whole number, written without quotes.");
            return null;
        }
        if (!int.TryParse(s.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            || value < min || value > max)
        {
            ctx.Error(DiagnosticCodes.BadValue, n, $"{PathOf(key)} is {s.Value}; it must be from {min} to {max}.");
            return null;
        }
        return value;
    }

    public bool? Bool(string key, bool required)
    {
        YamlNode? n = Raw(key, required);
        if (n is null)
            return null;
        if (n is YamlScalar { Plain: true, Value: "true" })
            return true;
        if (n is YamlScalar { Plain: true, Value: "false" })
            return false;
        ctx.Error(DiagnosticCodes.BadValue, n, $"{PathOf(key)} must be true or false, written without quotes.");
        return null;
    }

    /// <summary>A list of text values, each passing <paramref name="valid"/>, each once unless <paramref name="unique"/> is false.</summary>
    public IReadOnlyList<string> Strings(string key, bool required, Func<string, bool> valid, string what,
        bool unique = true, int minCount = 0)
    {
        YamlNode? n = Raw(key, required);
        if (n is null)
            return [];
        if (n is not YamlSequence list)
        {
            ctx.Error(DiagnosticCodes.BadValue, n, $"{PathOf(key)} must be a list, not {n.Describe}.");
            return [];
        }
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < list.Items.Count; i++)
        {
            YamlNode item = list.Items[i];
            if (item is not YamlScalar s || s.IsNull)
            {
                ctx.Error(DiagnosticCodes.BadValue, item, $"{PathOf(key)}[{i}] must be {what}, not {item.Describe}.");
                continue;
            }
            if (!valid(s.Value))
            {
                ctx.Error(DiagnosticCodes.BadValue, s, $"{PathOf(key)}[{i}] is '{s.Value}', which isn't {what}.");
                continue;
            }
            if (unique && !seen.Add(s.Value))
            {
                ctx.Error(DiagnosticCodes.BadValue, s, $"{PathOf(key)} lists '{s.Value}' twice.");
                continue;
            }
            result.Add(s.Value);
        }
        if (result.Count < minCount)
            ctx.Error(DiagnosticCodes.MissingField, n, $"{PathOf(key)} needs at least {minCount} entr{(minCount == 1 ? "y" : "ies")}.");
        return result.AsReadOnly();
    }

    /// <summary>A nested mapping, read with its own reader (call its <see cref="Finish"/>).</summary>
    public FieldReader? Map(string key, bool required)
    {
        YamlNode? n = Raw(key, required);
        if (n is null)
            return null;
        if (n is not YamlMapping m)
        {
            ctx.Error(DiagnosticCodes.BadValue, n, $"{PathOf(key)} must be a mapping, not {n.Describe}.");
            return null;
        }
        return new FieldReader(ctx, m, PathOf(key) + ".");
    }

    /// <summary>A list of mappings, each read with its own reader (call each one's <see cref="Finish"/>).</summary>
    public List<FieldReader> Maps(string key, bool required, int minCount = 0)
    {
        var result = new List<FieldReader>();
        YamlNode? n = Raw(key, required);
        if (n is null)
            return result;
        if (n is not YamlSequence list)
        {
            ctx.Error(DiagnosticCodes.BadValue, n, $"{PathOf(key)} must be a list, not {n.Describe}.");
            return result;
        }
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is YamlMapping m)
                result.Add(new FieldReader(ctx, m, $"{PathOf(key)}[{i}]."));
            else
                ctx.Error(DiagnosticCodes.BadValue, list.Items[i],
                    $"{PathOf(key)}[{i}] must be a mapping, not {list.Items[i].Describe}.");
        }
        if (list.Items.Count < minCount)
            ctx.Error(DiagnosticCodes.MissingField, n, $"{PathOf(key)} needs at least {minCount} entr{(minCount == 1 ? "y" : "ies")}.");
        return result;
    }

    /// <summary>A reference to another definition, qualified with this pack's namespace if written bare.</summary>
    public ContentId? Ref(string key, bool required, string expectedKind)
    {
        YamlNode? n = Raw(key, required);
        return n is null ? null : RefFrom(n, PathOf(key), expectedKind);
    }

    public ContentId? RefFrom(YamlNode n, string field, string expectedKind)
    {
        if (n is not YamlScalar s || s.IsNull)
        {
            ctx.Error(DiagnosticCodes.BadValue, n, $"{field} must be a content ID, not {n.Describe}.");
            return null;
        }
        if (!ContentId.TryResolve(s.Value, ctx.Namespace, out ContentId id))
        {
            ctx.Error(DiagnosticCodes.BadId, s, $"{field} is '{s.Value}', which isn't a content ID ({IdHelp}).");
            return null;
        }
        ctx.References.Add(new PendingReference(id, expectedKind, field, ctx.File, s.Line, s.Column, ctx.Who, ctx.Namespace));
        return id;
    }

    /// <summary>Marks a field as one this schema forbids here, with the reason, if it's present.</summary>
    public void Forbid(string key, string reason)
    {
        if (Node.Get(key) is null)
            return;
        used.Add(key);
        ctx.Error(DiagnosticCodes.BadValue, Node.KeyNode(key) ?? Node, $"{PathOf(key)} isn't allowed here: {reason}.");
    }

    /// <summary>Reports every key that wasn't read.</summary>
    public void Finish()
    {
        foreach (var entry in Node.Entries)
        {
            string key = entry.Key.Value;
            if (used.Contains(key))
                continue;
            string hint = Hints.TryGetValue(key, out string? h) ? $" ({h})" : "";
            ctx.Error(DiagnosticCodes.UnknownField, entry.Key, $"{ctx.Who} has an unknown field {PathOf(key)}{hint}.");
        }
    }

    public const string IdHelp =
        "an ID is namespace:local, or local for this pack; local is kebab-case like first-aid-kit, " +
        "or a catalog ID like ITM-CRF-004; at most 64 characters";
}
