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
/// <param name="Vehicles">Names and starting integrity for vehicles, each by one of its tiles. Vehicles not listed get defaults.</param>
public sealed record MapData(
    WorldMap Map,
    IReadOnlyList<MapMarker> Markers,
    int AmbientLight = 100,
    IReadOnlyList<VehicleSpec>? Vehicles = null)
{
    public IReadOnlyList<VehicleSpec> VehicleSpecs => Vehicles ?? Array.Empty<VehicleSpec>();
}

/// <summary>
/// Reads and writes .bhmap files: plain text, easy to diff. Writes version 2; reads 1 and 2.
/// <code>
/// bhmap 2
/// size 4 2
/// ambient 6        (optional; outdoor light 0-100, default 100 = daylight)
/// heights          (one row per line: decimeters, separated by spaces)
/// 0 0 50 80
/// 0 5 10 15
/// cover            (one character per tile; see Covers.All)
/// ..+_
/// ==V_
/// roofs            (optional; '.' for open sky, else roof height above the ground in 0.5 m levels, '6' = 3 m)
/// ...6
/// ...6
/// doors            (optional; 'x y open|closed|locked' for doorway tiles; unlisted doors are open)
/// 2 0 closed
/// vehicles         (optional; 'x y integrity [name]' for the vehicle covering x y)
/// 2 1 100 Jeep A
/// markers          (kind x y [name]; kinds: player npc rex light headlight)
/// player 0 1 Survivor
/// end
/// </code>
/// Version 1 stored heights as one 0.5 m level character per tile and had no roofs, doors or
/// vehicles sections; its floor tiles were indoors, so reading one gives every floor a 3 m roof.
/// Lines starting with ';' are comments, allowed outside the grids.
/// </summary>
public static class MapFile
{
    public const int FormatVersion = 2;

    private static readonly string[] Sections = { "roofs", "doors", "vehicles", "markers" };

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

        string[] header = Next("'bhmap 2'").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (header.Length != 2 || header[0] != "bhmap" || !int.TryParse(header[1], out int version)
            || version < 1 || version > FormatVersion)
            throw new FormatException($"Line {i}: expected 'bhmap 1' or 'bhmap {FormatVersion}'.");

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
            if (version == 1)
                ReadLevelRow(lines, ref i, width, y, (x, level) => map.Heights.SetLevel(new TilePos(x, y), level));
            else
                ReadDecimeterRow(lines, ref i, width, y, (x, dm) => map.Heights[new TilePos(x, y)] = dm);
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

        if (version == 1)
        {
            // v1 floors were indoors; now indoors means roofed.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (map.Cover[new TilePos(x, y)] == CoverType.Floor)
                        map.Structures.SetRoof(new TilePos(x, y), StructureMap.DefaultRoofDm);
        }

        var vehicles = new List<VehicleSpec>();
        int sectionIndex = 0;
        string section = Next("'markers'");
        while (section != "markers")
        {
            int found = Array.IndexOf(Sections, section);
            if (found < 0)
                throw new FormatException($"Line {i}: expected a section ({string.Join(", ", Sections)}), found '{section}'.");
            if (version == 1)
                throw new FormatException($"Line {i}: '{section}' needs 'bhmap {FormatVersion}'.");
            if (found < sectionIndex)
                throw new FormatException($"Line {i}: '{section}' is out of order; sections go {string.Join(", ", Sections)}.");
            sectionIndex = found + 1;

            switch (section)
            {
                case "roofs":
                    for (int y = 0; y < height; y++)
                    {
                        string row = ReadRow(lines, ref i, width, "roof", y);
                        for (int x = 0; x < width; x++)
                        {
                            if (row[x] == '.')
                                continue;
                            int level = HeightMap.ParseLevel(row[x]);
                            if (level <= 0)
                                throw new FormatException($"Line {i}: '{row[x]}' at column {x} is not a roof height ('.', 1-9, a-z).");
                            map.Structures.SetRoof(new TilePos(x, y), level * HeightMap.DecimetersPerLevel);
                        }
                    }
                    section = Next("'markers'");
                    break;

                case "doors":
                    section = ReadEntries(Next, () => i, (parts, line) =>
                    {
                        var p = ParsePosition(parts, map, line, "door");
                        if (parts.Length != 3 || ParseDoorState(parts[2]) is not { } state)
                            throw new FormatException($"Line {line}: expected 'x y open|closed|locked'.");
                        if (map.Cover[p] != CoverType.Door)
                            throw new FormatException($"Line {line}: door at {p} isn't on a doorway tile ('+').");
                        map.Structures.SetDoor(p, state);
                    });
                    break;

                case "vehicles":
                    section = ReadEntries(Next, () => i, (parts, line) =>
                    {
                        var p = ParsePosition(parts, map, line, "vehicle");
                        if (parts.Length < 3 || !int.TryParse(parts[2], out int integrity)
                            || integrity < 0 || integrity > Vehicle.MaxIntegrity)
                            throw new FormatException($"Line {line}: expected 'x y integrity [name]' with integrity 0-{Vehicle.MaxIntegrity}.");
                        if (map.Cover[p] != CoverType.Vehicle)
                            throw new FormatException($"Line {line}: vehicle at {p} isn't on a vehicle tile ('V').");
                        string? name = parts.Length == 4 ? parts[3].Trim() : null;
                        vehicles.Add(new VehicleSpec(p, integrity, name));
                    });
                    break;

                case "markers":
                    break;
            }
        }

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

        return new MapData(map, markers, ambient, vehicles);
    }

    /// <summary>Writes the current format (v2). Roofs, doors and vehicles sections appear only when there is something in them.</summary>
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
            {
                if (x > 0)
                    sb.Append(' ');
                sb.Append(map.Heights[new TilePos(x, y)]);
            }
            sb.Append('\n');
        }

        sb.Append("cover\n");
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
                sb.Append(Covers.Get(map.Cover[new TilePos(x, y)]).FileChar);
            sb.Append('\n');
        }

        if (map.Structures.AnyRoofs)
        {
            sb.Append("roofs\n");
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int dm = map.Structures.RoofDm(new TilePos(x, y));
                    int level = Math.Clamp((dm + HeightMap.DecimetersPerLevel / 2) / HeightMap.DecimetersPerLevel, 0, HeightMap.MaxLevel);
                    sb.Append(dm <= 0 ? '.' : HeightMap.LevelChar(Math.Max(1, level)));
                }
                sb.Append('\n');
            }
        }

        var doors = new StringBuilder();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var p = new TilePos(x, y);
                if (map.Cover[p] == CoverType.Door)
                    doors.Append(x).Append(' ').Append(y).Append(' ').Append(DoorStateName(map.Structures.DoorAt(p))).Append('\n');
            }
        }
        if (doors.Length > 0)
            sb.Append("doors\n").Append(doors);

        if (data.VehicleSpecs.Count > 0)
        {
            sb.Append("vehicles\n");
            foreach (var vehicle in data.VehicleSpecs)
            {
                sb.Append(vehicle.At.X).Append(' ').Append(vehicle.At.Y).Append(' ').Append(vehicle.Integrity);
                if (!string.IsNullOrWhiteSpace(vehicle.Name))
                    sb.Append(' ').Append(vehicle.Name.Trim());
                sb.Append('\n');
            }
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

    public static string DoorStateName(DoorState state) => state switch
    {
        DoorState.Open => "open",
        DoorState.Closed => "closed",
        DoorState.Locked => "locked",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static DoorState? ParseDoorState(string name) => name switch
    {
        "open" => DoorState.Open,
        "closed" => DoorState.Closed,
        "locked" => DoorState.Locked,
        _ => null,
    };

    /// <summary>Reads 'x y ...' lines until the next section keyword, which it returns.</summary>
    private static string ReadEntries(Func<string, string> next, Func<int> lineNumber, Action<string[], int> entry)
    {
        while (true)
        {
            string line = next("'markers'");
            if (Array.IndexOf(Sections, line) >= 0)
                return line;
            entry(line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries), lineNumber());
        }
    }

    private static TilePos ParsePosition(string[] parts, WorldMap map, int line, string what)
    {
        if (parts.Length < 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
            throw new FormatException($"Line {line}: expected '{what}' entry to start with 'x y'.");
        var p = new TilePos(x, y);
        if (!map.InBounds(p))
            throw new FormatException($"Line {line}: {what} at {p} is outside the {map.Width}x{map.Height} map.");
        return p;
    }

    private static void ReadLevelRow(string[] lines, ref int i, int width, int y, Action<int, int> set)
    {
        string row = ReadRow(lines, ref i, width, "height", y);
        for (int x = 0; x < width; x++)
        {
            int level = HeightMap.ParseLevel(row[x]);
            if (level < 0)
                throw new FormatException($"Line {i}: '{row[x]}' at column {x} is not a height (0-9, a-z).");
            set(x, level);
        }
    }

    private static void ReadDecimeterRow(string[] lines, ref int i, int width, int y, Action<int, short> set)
    {
        if (i >= lines.Length)
            throw new FormatException($"Unexpected end of file in height row {y}.");
        string[] values = lines[i++].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != width)
            throw new FormatException($"Line {i}: height row {y} has {values.Length} values; expected {width}.");
        for (int x = 0; x < width; x++)
        {
            if (!short.TryParse(values[x], out short dm))
                throw new FormatException($"Line {i}: '{values[x]}' at column {x} is not a height in decimeters.");
            set(x, dm);
        }
    }

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
