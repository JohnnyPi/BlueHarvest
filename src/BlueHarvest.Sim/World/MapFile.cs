using System.Text;

namespace BlueHarvest.Sim.World;

/// <summary>Things placed on a map that aren't terrain. Order matters: the editor palette indexes by it.</summary>
public enum MarkerKind : byte
{
    Player,
    Npc,
    Rex,
    Light,
    Headlight,
}

public sealed record MapMarker(MarkerKind Kind, TilePos Position, string? Name = null);

/// <param name="AmbientLight">Outdoor light before any lights, 0-100. 100 is daylight.</param>
public sealed record MapData(WorldMap Map, IReadOnlyList<MapMarker> Markers, int AmbientLight = 100);

/// <summary>
/// Reads and writes .bhmap files: plain text, one character per tile, easy to diff.
/// <code>
/// bhmap 1
/// size 4 2
/// ambient 6        (optional; outdoor light 0-100, default 100 = daylight)
/// heights          ('0'-'9' then 'a'-'z', each 0.5 m)
/// 00gg
/// 00gg
/// cover            (see Covers.All for the characters)
/// ..FF
/// ==..
/// markers          (kind x y [name]; kinds: player npc rex light headlight)
/// player 0 1 Survivor
/// end
/// </code>
/// Lines starting with ';' are comments, allowed outside the grids.
/// </summary>
public static class MapFile
{
    public const int FormatVersion = 1;

    public static MapData Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        int i = 0;

        string Next(string expecting)
        {
            while (i < lines.Length)
            {
                string line = lines[i++].Trim();
                if (line.Length > 0 && !line.StartsWith(';'))
                    return line;
            }
            throw new FormatException($"Unexpected end of file; expected {expecting}.");
        }

        void Expect(string keyword)
        {
            string line = Next($"'{keyword}'");
            if (line != keyword)
                throw new FormatException($"Line {i}: expected '{keyword}', found '{line}'.");
        }

        string[] header = Next("'bhmap 1'").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (header.Length != 2 || header[0] != "bhmap" || header[1] != FormatVersion.ToString())
            throw new FormatException($"Line {i}: expected 'bhmap {FormatVersion}'.");

        string[] size = Next("'size W H'").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (size.Length != 3 || size[0] != "size"
            || !int.TryParse(size[1], out int width) || !int.TryParse(size[2], out int height)
            || width <= 0 || height <= 0)
            throw new FormatException($"Line {i}: expected 'size W H' with positive numbers.");

        var map = new WorldMap(width, height);

        int ambient = 100;
        string next = Next("'heights'");
        if (next.StartsWith("ambient", StringComparison.Ordinal))
        {
            string[] parts = next.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts[0] != "ambient" || !int.TryParse(parts[1], out ambient) || ambient < 0 || ambient > 100)
                throw new FormatException($"Line {i}: expected 'ambient N' with N from 0 to 100.");
            next = Next("'heights'");
        }
        if (next != "heights")
            throw new FormatException($"Line {i}: expected 'heights', found '{next}'.");
        for (int y = 0; y < height; y++)
        {
            string row = ReadRow(lines, ref i, width, "height", y);
            for (int x = 0; x < width; x++)
            {
                int level = HeightMap.ParseLevel(row[x]);
                if (level < 0)
                    throw new FormatException($"Line {i}: '{row[x]}' at column {x} is not a height (0-9, a-z).");
                map.Heights.SetLevel(new TilePos(x, y), level);
            }
        }

        Expect("cover");
        for (int y = 0; y < height; y++)
        {
            string row = ReadRow(lines, ref i, width, "cover", y);
            for (int x = 0; x < width; x++)
            {
                var info = Covers.FromFileChar(row[x])
                    ?? throw new FormatException($"Line {i}: '{row[x]}' at column {x} is not a cover type.");
                map.Cover[new TilePos(x, y)] = info.Type;
            }
        }

        Expect("markers");
        var markers = new List<MapMarker>();
        while (true)
        {
            string line = Next("a marker or 'end'");
            if (line == "end")
                break;

            string[] parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y))
                throw new FormatException($"Line {i}: expected 'kind x y [name]', found '{line}'.");

            var kind = ParseKind(parts[0])
                ?? throw new FormatException($"Line {i}: unknown marker kind '{parts[0]}'.");
            var position = new TilePos(x, y);
            if (!map.InBounds(position))
                throw new FormatException($"Line {i}: marker at {position} is outside the {width}x{height} map.");

            markers.Add(new MapMarker(kind, position, parts.Length == 4 ? parts[3].Trim() : null));
        }

        return new MapData(map, markers, ambient);
    }

    public static string Serialize(MapData data)
    {
        var map = data.Map;
        var sb = new StringBuilder();
        sb.Append("bhmap ").Append(FormatVersion).Append('\n');
        sb.Append("size ").Append(map.Width).Append(' ').Append(map.Height).Append('\n');
        if (data.AmbientLight != 100)
            sb.Append("ambient ").Append(data.AmbientLight).Append('\n');

        sb.Append("heights\n");
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
                sb.Append(HeightMap.LevelChar(map.Heights.LevelAt(new TilePos(x, y))));
            sb.Append('\n');
        }

        sb.Append("cover\n");
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
                sb.Append(Covers.Get(map.Cover[new TilePos(x, y)]).FileChar);
            sb.Append('\n');
        }

        sb.Append("markers\n");
        foreach (var marker in data.Markers)
        {
            sb.Append(KindName(marker.Kind)).Append(' ')
                .Append(marker.Position.X).Append(' ')
                .Append(marker.Position.Y);
            if (!string.IsNullOrWhiteSpace(marker.Name))
                sb.Append(' ').Append(marker.Name.Trim());
            sb.Append('\n');
        }
        sb.Append("end\n");
        return sb.ToString();
    }

    public static string KindName(MarkerKind kind) => kind switch
    {
        MarkerKind.Player => "player",
        MarkerKind.Npc => "npc",
        MarkerKind.Rex => "rex",
        MarkerKind.Light => "light",
        MarkerKind.Headlight => "headlight",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static MarkerKind? ParseKind(string name) => name switch
    {
        "player" => MarkerKind.Player,
        "npc" => MarkerKind.Npc,
        "rex" => MarkerKind.Rex,
        "light" => MarkerKind.Light,
        "headlight" => MarkerKind.Headlight,
        _ => null,
    };

    private static string ReadRow(string[] lines, ref int i, int width, string layer, int y)
    {
        if (i >= lines.Length)
            throw new FormatException($"Unexpected end of file in {layer} row {y}.");

        string row = lines[i++];
        if (row.Length != width)
            throw new FormatException($"Line {i}: {layer} row {y} has {row.Length} characters; expected {width}.");
        return row;
    }
}
