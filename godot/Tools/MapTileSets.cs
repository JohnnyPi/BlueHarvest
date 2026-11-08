#nullable enable
using BlueHarvest.Sim.World;
using Godot;

namespace BlueHarvest.Game.Tools;

/// <summary>
/// Builds the editor's tile palettes in code, so there are no tileset files to maintain.
/// Each palette is one row of tiles; a tile's column is the value it stands for
/// (height level, CoverType or MarkerKind).
/// </summary>
public static class MapTileSets
{
    public const int TilePx = 16;

    private static readonly Color Low = new(0.10f, 0.22f, 0.12f);
    private static readonly Color Mid = new(0.55f, 0.48f, 0.30f);
    private static readonly Color High = new(0.92f, 0.92f, 0.90f);
    private static readonly Color Border = new(0f, 0f, 0f, 0.25f);

    /// <summary>One tile per height level, dark green (low) to tan to white (high), labelled 0-9 then a-z.</summary>
    public static TileSet Heights()
    {
        int count = HeightMap.MaxLevel + 1;
        var image = NewImage(count);
        for (int level = 0; level < count; level++)
        {
            float t = (float)level / (count - 1);
            var color = t < 0.5f ? Low.Lerp(Mid, t * 2f) : Mid.Lerp(High, (t - 0.5f) * 2f);
            Tile(image, level, color);
            var ink = color.Luminance > 0.5f ? Colors.Black : Colors.White;
            PixelFont.Draw(image, HeightMap.LevelChar(level), level * TilePx, TilePx, ink);
        }
        return Build(image, count);
    }

    /// <summary>One tile per cover type, labelled with its map-file character. Grass is see-through.</summary>
    public static TileSet Cover()
    {
        var colors = new[]
        {
            new Color(0f, 0f, 0f, 0f),          // Grass: empty cover means grass
            new Color(0.45f, 0.55f, 0.25f),     // Scrub
            new Color(0.20f, 0.45f, 0.18f),     // Sparse jungle
            new Color(0.06f, 0.25f, 0.08f),     // Dense jungle
            new Color(0.62f, 0.52f, 0.34f),     // Track
            new Color(0.45f, 0.45f, 0.45f),     // Floor
            new Color(0.80f, 0.80f, 0.78f),     // Wall
            new Color(0.55f, 0.35f, 0.15f),     // Door
            new Color(0.95f, 0.85f, 0.15f),     // Fence
            new Color(0.50f, 0.48f, 0.44f),     // Rock
            new Color(0.30f, 0.45f, 0.70f),     // Vehicle
            new Color(0.60f, 0.52f, 0.20f),     // Broken fence
        };

        int count = Covers.All.Count;
        var image = NewImage(count);
        for (int i = 0; i < count; i++)
        {
            var color = colors[i];
            if (color.A > 0f)
                Tile(image, i, color);
            var ink = color.A == 0f ? new Color(1f, 1f, 1f, 0.5f) : color.Luminance > 0.5f ? Colors.Black : Colors.White;
            PixelFont.Draw(image, Covers.All[i].FileChar, i * TilePx, TilePx, ink);
        }
        return Build(image, count);
    }

    /// <summary>One tile per marker kind: player, NPC, Rex, light, headlight.</summary>
    public static TileSet Markers()
    {
        var looks = new (Color Color, char Label)[]
        {
            (new Color(1f, 0.90f, 0.30f), '@'),  // Player
            (new Color(0.40f, 0.85f, 1f), 'h'),  // Npc
            (new Color(0.95f, 0.20f, 0.15f), 't'), // Rex
            (new Color(1f, 0.65f, 0.20f), '*'),  // Light
            (new Color(1f, 1f, 0.75f), '>'),     // Headlight: put it on a vehicle's front tile
        };

        var image = NewImage(looks.Length);
        for (int i = 0; i < looks.Length; i++)
        {
            Tile(image, i, looks[i].Color);
            PixelFont.Draw(image, looks[i].Label, i * TilePx, TilePx, Colors.Black);
        }
        return Build(image, looks.Length);
    }

    private static Image NewImage(int count) =>
        Image.CreateEmpty(count * TilePx, TilePx, false, Image.Format.Rgba8);

    private static void Tile(Image image, int index, Color color)
    {
        image.FillRect(new Rect2I(index * TilePx, 0, TilePx, TilePx), Border);
        image.FillRect(new Rect2I(index * TilePx + 1, 1, TilePx - 2, TilePx - 2), color);
    }

    private static TileSet Build(Image image, int count)
    {
        var source = new TileSetAtlasSource
        {
            Texture = ImageTexture.CreateFromImage(image),
            TextureRegionSize = new Vector2I(TilePx, TilePx),
        };
        for (int i = 0; i < count; i++)
            source.CreateTile(new Vector2I(i, 0));

        var tileSet = new TileSet { TileSize = new Vector2I(TilePx, TilePx) };
        tileSet.AddSource(source, 0);
        return tileSet;
    }
}
