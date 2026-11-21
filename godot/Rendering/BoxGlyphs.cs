#nullable enable
using Godot;

namespace BlueHarvest.Game.Rendering;

/// <summary>
/// Box-drawing and block glyphs (U+2500–U+259F) drawn as lines and rectangles instead of text. A font
/// glyph is only as wide as the font's advance, about half a 24 px cell, so ═ and ─ left a gap between
/// cells and walls never joined sideways. Drawn from the glyph's arms (north, east, south, west: light,
/// heavy, double or dashed), every line reaches the cell's edge and meets its neighbour's. Double lines
/// are the outline of the corridors their arms make, so ╔ ╦ ╬ join the way they do in a terminal.
/// Looks only.
/// </summary>
public static class BoxGlyphs
{
    private enum Arm : byte { None, Light, Heavy, Double, Dashed }

    /// <summary>Whether this glyph is drawn here rather than by the font.</summary>
    public static bool Handles(char glyph) => glyph is >= '─' and <= '▟' && (Arms(glyph) is not null || IsSpecial(glyph));

    /// <summary>
    /// Draws the glyph into the cell and returns true, or returns false (drawing nothing) if it isn't a
    /// box or block glyph this class knows.
    /// </summary>
    public static bool TryDraw(CanvasItem canvas, char glyph, Rect2 cell, Color color)
    {
        if (glyph < '─' || glyph > '▟')
            return false;
        if (Arms(glyph) is { } arms)
        {
            DrawArms(canvas, arms, cell, color);
            return true;
        }
        return DrawSpecial(canvas, glyph, cell, color);
    }

    // ---- The table: arms (N, E, S, W) per glyph ------------------------------------------------

    private const Arm L = Arm.Light, H = Arm.Heavy, D = Arm.Double, X = Arm.Dashed, O = Arm.None;

    private static (Arm N, Arm E, Arm S, Arm W)? Arms(char c) => c switch
    {
        '─' => (O, L, O, L), '│' => (L, O, L, O),
        '┌' => (O, L, L, O), '┐' => (O, O, L, L), '└' => (L, L, O, O), '┘' => (L, O, O, L),
        '├' => (L, L, L, O), '┤' => (L, O, L, L), '┬' => (O, L, L, L), '┴' => (L, L, O, L), '┼' => (L, L, L, L),
        '━' => (O, H, O, H), '┃' => (H, O, H, O),
        '┏' => (O, H, H, O), '┓' => (O, O, H, H), '┗' => (H, H, O, O), '┛' => (H, O, O, H),
        '┣' => (H, H, H, O), '┫' => (H, O, H, H), '┳' => (O, H, H, H), '┻' => (H, H, O, H), '╋' => (H, H, H, H),
        '═' => (O, D, O, D), '║' => (D, O, D, O),
        '╔' => (O, D, D, O), '╗' => (O, O, D, D), '╚' => (D, D, O, O), '╝' => (D, O, O, D),
        '╠' => (D, D, D, O), '╣' => (D, O, D, D), '╦' => (O, D, D, D), '╩' => (D, D, O, D), '╬' => (D, D, D, D),
        '╒' => (O, D, L, O), '╓' => (O, L, D, O), '╕' => (O, O, L, D), '╖' => (O, O, D, L),
        '╘' => (L, D, O, O), '╙' => (D, L, O, O), '╛' => (L, O, O, D), '╜' => (D, O, O, L),
        '╞' => (L, D, L, O), '╟' => (D, L, D, O), '╡' => (L, O, L, D), '╢' => (D, O, D, L),
        '╤' => (O, D, L, D), '╥' => (O, L, D, L), '╧' => (L, D, O, D), '╨' => (D, L, O, L),
        '╪' => (L, D, L, D), '╫' => (D, L, D, L),
        '╌' or '┄' or '┈' => (O, X, O, X), '╎' or '┆' or '┊' => (X, O, X, O),
        '╴' => (O, O, O, L), '╵' => (L, O, O, O), '╶' => (O, L, O, O), '╷' => (O, O, L, O),
        _ => null,
    };

    private static bool IsSpecial(char c) => c is '╭' or '╮' or '╯' or '╰' or '╱' or '╲' or '╳'
        or '█' or '▀' or '▄' or '▌' or '▐' or '░' or '▒' or '▓';

    // ---- Sizes, from the cell ---------------------------------------------------------------

    /// <summary>A light line: 2 px in a 24 px cell.</summary>
    private static float LightWidth(float s) => Mathf.Max(1f, s / 12f);

    /// <summary>Each rail of a double line.</summary>
    private static float RailWidth(float s) => Mathf.Max(1f, s / 16f);

    /// <summary>Half the gap between a double line's rails, centre to centre: 3.5 px in a 24 px cell.</summary>
    private static float RailOffset(float s) => s * 0.145f;

    // ---- Arms ---------------------------------------------------------------------------------

    private static void DrawArms(CanvasItem canvas, (Arm N, Arm E, Arm S, Arm W) a, Rect2 cell, Color color)
    {
        float s = cell.Size.X;
        float left = cell.Position.X, top = cell.Position.Y, right = cell.End.X, bottom = cell.End.Y;
        float cx = left + s / 2f, cy = top + cell.Size.Y / 2f;
        bool anyDouble = a.N == D || a.E == D || a.S == D || a.W == D;
        float g = RailOffset(s);

        if (anyDouble)
        {
            // the outline of the union of the centre square and the double arms' corridors. Where a
            // horizontal and a vertical stroke meet in a corner, the horizontal one reaches over the corner
            // and the vertical one stops short, so nothing is drawn twice (it would show with translucent ink)
            float r = RailWidth(s);
            bool n = a.N == D, e = a.E == D, so = a.S == D, we = a.W == D;
            if (e) { Horizontal(canvas, cx + g, right, cy - g, r, color, n, false); Horizontal(canvas, cx + g, right, cy + g, r, color, so, false); }
            if (we) { Horizontal(canvas, left, cx - g, cy - g, r, color, false, n); Horizontal(canvas, left, cx - g, cy + g, r, color, false, so); }
            if (!n) Horizontal(canvas, cx - g, cx + g, cy - g, r, color, !we, !e);
            if (!so) Horizontal(canvas, cx - g, cx + g, cy + g, r, color, !we, !e);
            if (n) { Vertical(canvas, cx - g, top, cy - g, r, color, false, we); Vertical(canvas, cx + g, top, cy - g, r, color, false, e); }
            if (so) { Vertical(canvas, cx - g, cy + g, bottom, r, color, we, false); Vertical(canvas, cx + g, cy + g, bottom, r, color, e, false); }
            if (!we) Vertical(canvas, cx - g, cy - g, cy + g, r, color, !n, !so);
            if (!e) Vertical(canvas, cx + g, cy - g, cy + g, r, color, !n, !so);
        }

        // light, heavy and dashed arms: from the centre (or from outside the double corridor) to the edge
        float Width(Arm arm) => arm == H ? LightWidth(s) * 2f : LightWidth(s);
        float startX = anyDouble ? g : 0f, startY = anyDouble ? g : 0f;
        bool across = (a.E is L or H) || (a.W is L or H);
        float acrossHalf = across ? Mathf.Max((a.E is L or H) ? Width(a.E) : 0f, (a.W is L or H) ? Width(a.W) : 0f) / 2f : 0f;
        if (a.E is L or H)
            canvas.DrawRect(new Rect2(cx + (anyDouble ? startX : -acrossHalf), cy - Width(a.E) / 2f, right - cx - (anyDouble ? startX : -acrossHalf), Width(a.E)), color);
        if (a.W is L or H)
        {
            // when both sides are present the east arm already covers the centre
            float end = anyDouble ? cx - startX : (a.E is L or H) ? cx - acrossHalf : cx + acrossHalf;
            canvas.DrawRect(new Rect2(left, cy - Width(a.W) / 2f, end - left, Width(a.W)), color);
        }
        if (a.N is L or H)
        {
            float end = anyDouble ? cy - startY : across ? cy - acrossHalf : cy + Width(a.N) / 2f;
            canvas.DrawRect(new Rect2(cx - Width(a.N) / 2f, top, Width(a.N), end - top), color);
        }
        if (a.S is L or H)
        {
            float start = anyDouble ? cy + startY : across ? cy + acrossHalf : (a.N is L or H) ? cy + Width(a.N) / 2f : cy - Width(a.S) / 2f;
            canvas.DrawRect(new Rect2(cx - Width(a.S) / 2f, start, Width(a.S), bottom - start), color);
        }

        // dashed: one dash per half cell, a quarter cell long, so the pattern runs on across cells
        float w = LightWidth(s), q = s / 8f;
        if (a.E == X) canvas.DrawRect(new Rect2(cx + q, cy - w / 2f, 2f * q, w), color);
        if (a.W == X) canvas.DrawRect(new Rect2(cx - 3f * q, cy - w / 2f, 2f * q, w), color);
        if (a.N == X) canvas.DrawRect(new Rect2(cx - w / 2f, cy - 3f * q, w, 2f * q), color);
        if (a.S == X) canvas.DrawRect(new Rect2(cx - w / 2f, cy + q, w, 2f * q), color);
    }

    /// <summary>A horizontal stroke from x0 to x1 at y; <paramref name="padStart"/>/<paramref name="padEnd"/> reach half a stroke past an inside end, to fill the corner.</summary>
    private static void Horizontal(CanvasItem canvas, float x0, float x1, float y, float width, Color color, bool padStart, bool padEnd)
    {
        float half = width / 2f;
        float from = padStart ? x0 - half : x0, to = padEnd ? x1 + half : x1;
        canvas.DrawRect(new Rect2(from, y - half, to - from, width), color);
    }

    /// <summary>A vertical stroke from y0 to y1 at x; <paramref name="trimStart"/>/<paramref name="trimEnd"/> stop half a stroke short of an inside end, where a horizontal stroke already is.</summary>
    private static void Vertical(CanvasItem canvas, float x, float y0, float y1, float width, Color color, bool trimStart, bool trimEnd)
    {
        float half = width / 2f;
        float from = trimStart ? y0 + half : y0, to = trimEnd ? y1 - half : y1;
        if (to > from)
            canvas.DrawRect(new Rect2(x - half, from, width, to - from), color);
    }

    // ---- Rounded corners, diagonals, blocks and shades -------------------------------------------

    private static bool DrawSpecial(CanvasItem canvas, char c, Rect2 cell, Color color)
    {
        float s = cell.Size.X, w = LightWidth(s), r = s / 2f;
        float left = cell.Position.X, top = cell.Position.Y, right = cell.End.X, bottom = cell.End.Y;
        switch (c)
        {
            // a quarter circle from one edge's middle to the other's, about the cell's corner
            case '╭': canvas.DrawArc(new Vector2(right, bottom), r, Mathf.Pi, 1.5f * Mathf.Pi, 12, color, w); return true;
            case '╮': canvas.DrawArc(new Vector2(left, bottom), r, -0.5f * Mathf.Pi, 0f, 12, color, w); return true;
            case '╯': canvas.DrawArc(new Vector2(left, top), r, 0f, 0.5f * Mathf.Pi, 12, color, w); return true;
            case '╰': canvas.DrawArc(new Vector2(right, top), r, 0.5f * Mathf.Pi, Mathf.Pi, 12, color, w); return true;
            case '╱': canvas.DrawLine(new Vector2(left, bottom), new Vector2(right, top), color, w); return true;
            case '╲': canvas.DrawLine(new Vector2(left, top), new Vector2(right, bottom), color, w); return true;
            case '╳':
                canvas.DrawLine(new Vector2(left, bottom), new Vector2(right, top), color, w);
                canvas.DrawLine(new Vector2(left, top), new Vector2(right, bottom), color, w);
                return true;
            case '█': canvas.DrawRect(cell, color); return true;
            case '▀': canvas.DrawRect(new Rect2(left, top, s, cell.Size.Y / 2f), color); return true;
            case '▄': canvas.DrawRect(new Rect2(left, top + cell.Size.Y / 2f, s, cell.Size.Y / 2f), color); return true;
            case '▌': canvas.DrawRect(new Rect2(left, top, s / 2f, cell.Size.Y), color); return true;
            case '▐': canvas.DrawRect(new Rect2(left + s / 2f, top, s / 2f, cell.Size.Y), color); return true;
            case '░': Stipple(canvas, cell, color, 1); return true;
            case '▒': Stipple(canvas, cell, color, 2); return true;
            case '▓': Stipple(canvas, cell, color, 3); return true;
        }
        return false;
    }

    /// <summary>A 6 × 6 grid of dots: about a quarter (░), half (▒) or three quarters (▓) of them filled.</summary>
    private static void Stipple(CanvasItem canvas, Rect2 cell, Color color, int quarters)
    {
        float b = cell.Size.X / 6f;
        for (int j = 0; j < 6; j++)
        {
            for (int i = 0; i < 6; i++)
            {
                bool sparse = (i & 1) == 0 && (j & 1) == 0 && ((i + j) & 2) == 0 || (i & 1) == 1 && (j & 1) == 1 && ((i + j) & 2) == 2;
                bool on = quarters switch
                {
                    1 => sparse,
                    2 => ((i + j) & 1) == 0,
                    _ => !sparse,
                };
                if (on)
                    canvas.DrawRect(new Rect2(cell.Position.X + i * b, cell.Position.Y + j * b, b, b), color);
            }
        }
    }
}
