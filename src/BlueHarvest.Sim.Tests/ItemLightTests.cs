using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.World;
using Xunit;
using BlueHarvest.Sim.ContentIntegration;

namespace BlueHarvest.Sim.Tests;

/// <summary>Step 23a: the headlamp, the lantern, glow sticks and the torch.</summary>
public class ItemLightTests
{
    private static (Simulation Sim, PlayerController Drive, Actor You) Person()
    {
        var sim = new Simulation(TestContent.NewSession(), new WorldMap(30, 12), seed: 1);
        sim.SetWeather(WeatherSettings.Clear);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive, Direction.East);
        sim.RunUntilInputNeeded();
        return (sim, drive, you);
    }

    private static void Do(Simulation sim, PlayerController drive, ActorAction action)
    {
        drive.Pending = action;
        sim.RunUntilInputNeeded();
    }

    private static bool LitAt(Simulation sim, TilePos at) => sim.Lights.Any(l => l.On && l.Position == at);

    [Fact]
    public void AHeadlamp_ShinesWhereYouFace_AndSwitchesOff()
    {
        var (sim, drive, you) = Person();
        var lamp = sim.CreateItem(CoreItems.Headlamp);
        Assert.True(sim.GiveItem(you, lamp));
        Assert.Equal(lamp, you.Inventory.WornOn(WearSlot.Head));

        Do(sim, drive, new UseItemAction(lamp.Id));
        Assert.True(lamp.Lit);
        var beam = Assert.Single(sim.Lights, l => l.On);
        Assert.Equal(you.Position, beam.Position);
        Assert.Equal(Direction.East, beam.Facing);
        Assert.Contains("(on)", lamp.Describe());

        Do(sim, drive, new MoveAction(Direction.South));
        Assert.Equal(Direction.South, Assert.Single(sim.Lights, l => l.On).Facing);

        Do(sim, drive, new UseItemAction(lamp.Id));
        Assert.False(lamp.Lit);
        Assert.DoesNotContain(sim.Lights, l => l.On);
    }

    [Fact]
    public void ALightWithoutBatteries_WontComeOn()
    {
        var (sim, drive, you) = Person();
        var lamp = sim.CreateItem(CoreItems.Lantern, fitted: false);
        Assert.True(sim.GiveItem(you, lamp));
        Do(sim, drive, new UseItemAction(lamp.Id));
        Assert.False(lamp.Lit);
        Assert.Empty(sim.LitItems);
    }

    [Fact]
    public void ALanternSetDown_KeepsShiningWhereItLies_AndGoesDarkInABag()
    {
        var (sim, drive, you) = Person();
        var lantern = sim.CreateItem(CoreItems.Lantern);
        Assert.True(sim.GiveItem(you, lantern));
        Do(sim, drive, new UseItemAction(lantern.Id));
        var spot = you.Position;
        Assert.True(LitAt(sim, spot));

        Do(sim, drive, new DropAction(lantern.Id));
        for (int i = 0; i < 3; i++)
            Do(sim, drive, new MoveAction(Direction.East));
        Assert.True(LitAt(sim, spot));
        Assert.False(LitAt(sim, you.Position));

        // back, pick it up, and into a bag: hidden
        for (int i = 0; i < 3; i++)
            Do(sim, drive, new MoveAction(Direction.West));
        Do(sim, drive, new PickUpAction(lantern.Id));
        Assert.True(LitAt(sim, you.Position));
        var pack = sim.CreateItem(CoreItems.Daypack);
        Assert.True(sim.GiveItem(you, pack));
        Do(sim, drive, new PutAction(lantern.Id, pack.Id));
        Assert.True(lantern.Lit);
        Assert.DoesNotContain(sim.Lights, l => l.On);
    }

    [Fact]
    public void ALantern_RunsItsBatteriesFlat()
    {
        var (sim, drive, you) = Person();
        var lantern = sim.CreateItem(CoreItems.Lantern);
        Assert.True(sim.GiveItem(you, lantern));
        var died = new List<Item>();
        sim.ItemLightDied += (_, item) => died.Add(item);
        Do(sim, drive, new UseItemAction(lantern.Id));
        Do(sim, drive, new WaitAction(100 * ItemRules.LightTicksPerPercent));
        Assert.False(lantern.Lit);
        Assert.Single(died);
        Assert.All(lantern.Contents, b => Assert.Equal(0, b.Amount));
    }

    [Fact]
    public void AGlowStick_CrackedFromAStack_GlowsEightHours()
    {
        var (sim, drive, you) = Person();
        var sticks = sim.CreateItem(CoreItems.GlowStick, amount: 3);
        Assert.True(sim.GiveItem(you, sticks));
        Do(sim, drive, new UseItemAction(sticks.Id));
        Assert.Equal(2, sticks.Amount);
        var glowing = Assert.Single(you.Inventory.All, i => i.Kind == CoreItems.GlowStick && i.Lit);
        Assert.NotEqual(sticks, glowing);
        Assert.True(LitAt(sim, you.Position));

        Do(sim, drive, new WaitAction(Simulation.GlowStickTicks));
        Assert.True(glowing.Spent);
        Assert.Contains("(spent)", glowing.Describe());
        Assert.False(LitAt(sim, you.Position));
    }

    [Fact]
    public void AThrownGlowStick_LiesGlowingWhereItLands()
    {
        var (sim, drive, you) = Person();
        var sticks = sim.CreateItem(CoreItems.GlowStick, amount: 2);
        Assert.True(sim.GiveItem(you, sticks));
        var target = new TilePos(9, 5);
        Do(sim, drive, new ThrowAction(CoreItems.GlowStick, target));
        Assert.Equal(1, sticks.Amount);
        var landed = Assert.Single(sim.ItemsOnMap(), l => l.Lying.Item.Kind == CoreItems.GlowStick);
        Assert.True(landed.Lying.Item.Lit);
        Assert.True(LitAt(sim, landed.At));
    }

    [Fact]
    public void ATorch_LitWithTheLighter_BurnsHalfAnHour_AndSetsThingsAlight()
    {
        var (sim, drive, you) = Person();
        var torch = sim.CreateItem(CoreItems.Torch);
        var lighter = sim.CreateItem(CoreItems.Lighter);
        Assert.True(sim.GiveItem(you, torch));
        Assert.True(sim.GiveItem(you, lighter));
        Do(sim, drive, new UseItemAction(torch.Id));
        Assert.True(torch.Lit);
        Assert.Equal(39, lighter.Amount);
        Assert.Contains(sim.Lights, l => l.On && l.Red && l.Position == you.Position);

        var ahead = new TilePos(6, 5);
        sim.SpillFuel(ahead);
        Do(sim, drive, new IgniteAction(Direction.East, CoreItems.Torch));
        Assert.NotNull(sim.FireAt(ahead));
        Assert.True(torch.Lit); // still burning

        Do(sim, drive, new WaitAction(Simulation.TorchTicks));
        Assert.True(torch.Spent);
        Assert.Contains("(burnt out)", torch.Describe());
    }
}
