#nullable enable
using System.Linq;
using Godot;

namespace BlueHarvest.Game.Ui;

/// <summary>
/// The field sidebar on the right: the clock and weather, how exposed you feel, what you see, hear,
/// smell and feel around you, your body, the exits, your gear and the other people. Each block keeps
/// its place so the eye learns where to look. It only lays out <see cref="HudFacts"/>.
/// </summary>
public partial class Sidebar : PanelContainer
{
    public const float PanelWidth = 300f;

    private readonly Label _clock;
    private readonly Label _badge;
    private readonly Label _weather;
    private readonly Label _verdict;
    private readonly RichTextLabel _exposure;
    private readonly RichTextLabel _senses;
    private readonly BodyDoll _doll;
    private readonly RichTextLabel _bodyNotes;
    private readonly RichTextLabel _exits;
    private readonly RichTextLabel _gear;
    private readonly RichTextLabel _people;

    public Sidebar()
    {
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 1f;
        OffsetLeft = -PanelWidth;
        OffsetRight = 0f;
        OffsetTop = 0f;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.Line, 0, 0));

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(scroll);
        var stack = HudTheme.Column(0);
        scroll.AddChild(stack);

        // Clock and weather.
        var top = Section(stack);
        var clockRow = new HBoxContainer();
        _clock = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _clock.AddThemeFontSizeOverride("font_size", 26);
        _badge = HudTheme.Text("", HudTheme.SmallSize, HudTheme.Amber);
        _badge.AutowrapMode = TextServer.AutowrapMode.Off;
        _badge.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        clockRow.AddChild(_clock);
        clockRow.AddChild(_badge);
        top.AddChild(clockRow);
        _weather = HudTheme.Text("", HudTheme.SmallSize, HudTheme.Muted);
        top.AddChild(_weather);

        // How exposed you feel: a feeling, not a measurement.
        var exposure = Section(stack);
        var exposureRow = new HBoxContainer();
        var exposureHeading = HudTheme.Heading("Exposure");
        exposureHeading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _verdict = HudTheme.Text("", HudTheme.SmallSize);
        _verdict.AutowrapMode = TextServer.AutowrapMode.Off;
        _verdict.Uppercase = true;
        exposureRow.AddChild(exposureHeading);
        exposureRow.AddChild(_verdict);
        exposure.AddChild(exposureRow);
        _exposure = HudTheme.Rich();
        exposure.AddChild(_exposure);

        // Senses: what's around you, and where.
        var senses = Section(stack);
        senses.AddChild(HudTheme.Heading("Senses"));
        _senses = HudTheme.Rich();
        senses.AddChild(_senses);

        // Body.
        var body = Section(stack);
        body.AddChild(HudTheme.Heading("Body"));
        var bodyRow = new HBoxContainer();
        bodyRow.AddThemeConstantOverride("separation", 12);
        _doll = new BodyDoll { CustomMinimumSize = new Vector2(60, 96) };
        _bodyNotes = HudTheme.Rich(HudTheme.SmallSize);
        _bodyNotes.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        bodyRow.AddChild(_doll);
        bodyRow.AddChild(_bodyNotes);
        body.AddChild(bodyRow);

        // Exits, gear, people.
        var exits = Section(stack);
        exits.AddChild(HudTheme.Heading("Exits"));
        _exits = HudTheme.Rich();
        exits.AddChild(_exits);

        var gear = Section(stack);
        gear.AddChild(HudTheme.Heading("Gear"));
        _gear = HudTheme.Rich(HudTheme.SmallSize);
        gear.AddChild(_gear);

        var people = Section(stack);
        people.AddChild(HudTheme.Heading("People"));
        _people = HudTheme.Rich(HudTheme.SmallSize);
        people.AddChild(_people);

        var footer = Section(stack, last: true);
        footer.AddChild(HudTheme.Text("F1 controls · F9 debug", HudTheme.SmallSize, HudTheme.Muted));
    }

    public void Display(HudFacts f)
    {
        _clock.Text = f.Clock;
        _badge.Text = f.WeatherBadge;
        _weather.Text = f.Weather;

        var tone = f.ExposureAlarm switch
        {
            Alarm.Spotted => HudTheme.Danger,
            Alarm.Close => HudTheme.Amber,
            _ => HudTheme.Safe,
        };
        _verdict.Text = f.ExposureVerdict;
        _verdict.AddThemeColorOverride("font_color", tone);
        _exposure.Text = $"[color={HudTheme.Hex(f.ExposureAlarm == Alarm.Calm ? HudTheme.Muted : tone)}]{f.Exposure}[/color]\n" +
            $"[font_size={HudTheme.SmallSize}]{f.ExposureDetail}[/font_size]";

        string muted = HudTheme.Hex(HudTheme.Muted);
        _senses.Text =
            $"[color={muted}]Sight[/color]  {f.Sight}\n" +
            $"[color={muted}]Sound[/color]  {f.Sound}\n" +
            $"[color={muted}]Smell[/color]  {f.Smell}\n" +
            $"[color={muted}]Feel [/color]  {f.Feel}";

        _doll.ShowHealth(f.Health);
        _bodyNotes.Text = f.BodyNotes;

        _exits.Text = string.Join("\n", f.Exits.Select(e =>
            $"[color={HudTheme.Hex(HudTheme.Safe)}]{e.Arrow}[/color] {HudTheme.Escape(e.Name)}  [color={muted}]{e.Distance} m {e.Compass}[/color]"));
        _gear.Text = f.Gear;
        _people.Text = f.People;
    }

    private static VBoxContainer Section(VBoxContainer stack, bool last = false)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var box = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = HudTheme.Line };
        box.BorderWidthBottom = last ? 0 : 1;
        box.SetContentMarginAll(12);
        box.ContentMarginTop = 10;
        box.ContentMarginBottom = 10;
        panel.AddThemeStyleboxOverride("panel", box);
        var column = HudTheme.Column(6);
        panel.AddChild(column);
        stack.AddChild(panel);
        return column;
    }
}
