#nullable enable
using BlueHarvest.Sim.Injuries;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// The paper doll: head, torso, arms and legs, each outlined green, yellow or red, with a thick red
/// outline on a section that's bleeding.
/// </summary>
public partial class BodyDoll : Control
{
    private Health? _health;

    public BodyDoll()
    {
        CustomMinimumSize = new Vector2(60, 100);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void ShowHealth(Health health)
    {
        _health = health;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_health is null)
            return;

        float scale = Mathf.Min(Size.X / 60f, Size.Y / 100f);
        var origin = new Vector2((Size.X - 60f * scale) / 2f, (Size.Y - 100f * scale) / 2f);
        Rect2 At(float x, float y, float w, float h) => new(origin + new Vector2(x, y) * scale, new Vector2(w, h) * scale);

        var head = _health[BodySection.Head];
        var headColor = HudTheme.SeverityColor(head.Severity);
        var center = origin + new Vector2(30, 12) * scale;
        DrawCircle(center, 8.5f * scale, Fill(headColor));
        DrawArc(center, 8.5f * scale, 0f, Mathf.Tau, 32, head.Bleeding ? HudTheme.Danger : headColor, head.Bleeding ? 3f : 1.6f);

        Part(BodySection.Torso, At(20, 24, 20, 32));
        Part(BodySection.LeftArm, At(9, 25, 8, 30));
        Part(BodySection.RightArm, At(43, 25, 8, 30));
        Part(BodySection.LeftLeg, At(20.5f, 59, 8.5f, 36));
        Part(BodySection.RightLeg, At(31, 59, 8.5f, 36));
    }

    private void Part(BodySection section, Rect2 rect)
    {
        var wound = _health![section];
        var color = HudTheme.SeverityColor(wound.Severity);
        DrawRect(rect, Fill(color));
        DrawRect(rect, wound.Bleeding ? HudTheme.Danger : color, false, wound.Bleeding ? 3f : 1.6f);
    }

    private static Color Fill(Color c) => new(c.R, c.G, c.B, 0.28f);
}
