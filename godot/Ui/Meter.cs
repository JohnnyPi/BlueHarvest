#nullable enable
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// A bar for a score with a threshold: green while well under it, amber close to it, red at or past it,
/// with a white tick where the threshold sits. Used for the Rex's sight score on you.
/// </summary>
public partial class Meter : Control
{
    private float _value;
    private float _max = 30f;
    private float _threshold = 10f;

    public Meter()
    {
        CustomMinimumSize = new Vector2(0, 10);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(float value, float max, float threshold)
    {
        _value = value;
        _max = Mathf.Max(1f, max);
        _threshold = threshold;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var bar = new Rect2(Vector2.Zero, Size);
        DrawRect(bar, new Color(0f, 0f, 0f, 0.6f));
        float fraction = Mathf.Clamp(_value / _max, 0f, 1f);
        var fill = _value >= _threshold ? HudTheme.Danger
            : _value >= _threshold * 0.6f ? HudTheme.Amber
            : HudTheme.Safe;
        if (fraction > 0f)
            DrawRect(new Rect2(0, 0, Size.X * fraction, Size.Y), fill);
        DrawRect(bar, HudTheme.Line, false, 1f);
        float x = Size.X * Mathf.Clamp(_threshold / _max, 0f, 1f);
        DrawLine(new Vector2(x, -3), new Vector2(x, Size.Y + 3), HudTheme.Ink, 2f);
    }
}
