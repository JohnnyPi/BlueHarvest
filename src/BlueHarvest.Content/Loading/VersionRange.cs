#nullable enable
namespace BlueHarvest.Content;

/// <summary>
/// A version requirement: one or more comparisons separated by spaces, all of which must hold.
/// <c>&gt;=0.1.0</c>, <c>&gt;=1.0.0 &lt;2.0.0</c>, <c>=1.2.3</c>; a bare version means exactly that version.
/// </summary>
public sealed class VersionRange
{
    readonly (string Op, SemVersion Version)[] terms;

    VersionRange(string text, (string Op, SemVersion Version)[] terms)
    {
        Text = text;
        this.terms = terms;
    }

    /// <summary>As written.</summary>
    public string Text { get; }

    public static bool TryParse(string? text, out VersionRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parsed = new List<(string Op, SemVersion Version)>();
        foreach (string part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string op = part.StartsWith(">=", StringComparison.Ordinal) ? ">="
                : part.StartsWith("<=", StringComparison.Ordinal) ? "<="
                : part.StartsWith('>') ? ">"
                : part.StartsWith('<') ? "<"
                : part.StartsWith('=') ? "="
                : "";
            if (!SemVersion.TryParse(part[op.Length..], out SemVersion v))
                return false;
            parsed.Add((op.Length == 0 ? "=" : op, v));
        }
        range = new VersionRange(text, parsed.ToArray());
        return true;
    }

    public bool Contains(SemVersion version)
    {
        foreach (var (op, bound) in terms)
        {
            int c = version.CompareTo(bound);
            bool ok = op switch
            {
                ">=" => c >= 0,
                "<=" => c <= 0,
                ">" => c > 0,
                "<" => c < 0,
                _ => c == 0,
            };
            if (!ok)
                return false;
        }
        return true;
    }

    public override string ToString() => Text;
}
