#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlueHarvest.Content;

/// <summary>
/// A pack or game version: <c>major.minor.patch</c>, with an optional <c>-prerelease</c> that sorts before the
/// release (<c>1.0.0-rc1 &lt; 1.0.0</c>). Prerelease labels compare as plain text. No build metadata.
/// </summary>
public readonly record struct SemVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<SemVersion>
{
    static readonly Regex Form = new(
        @"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-([0-9A-Za-z.-]+))?\z", RegexOptions.CultureInvariant);

    public static bool TryParse(string? text, out SemVersion version)
    {
        version = default;
        if (text is null)
            return false;
        Match m = Form.Match(text);
        if (!m.Success)
            return false;
        version = new SemVersion(
            int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            m.Groups[4].Success ? m.Groups[4].Value : "");
        return true;
    }

    public int CompareTo(SemVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        string mine = Prerelease ?? "", theirs = other.Prerelease ?? "";
        if (mine.Length == 0 && theirs.Length == 0)
            return 0;
        if (mine.Length == 0)
            return 1; // a release sorts after its prereleases
        if (theirs.Length == 0)
            return -1;
        return Math.Sign(string.CompareOrdinal(mine, theirs));
    }

    public override string ToString() =>
        string.IsNullOrEmpty(Prerelease) ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
