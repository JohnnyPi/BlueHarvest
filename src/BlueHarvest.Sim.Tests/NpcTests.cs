using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Injuries;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>People: what they make of what they perceive, fear, panic, cover, helping, and orders.</summary>
public class NpcTests
{
    /// <summary>Never panics: fear tops out at 100.</summary>
    private static readonly Traits Calm = new(Courage: 50, Selfishness: 30, PanicThreshold: 101, KnowsMotionVision: false);

    private static HumanBrain Brain(Simulation sim, Traits traits, string stream = "npc") =>
        new(traits, AiProfile.Human, sim.Rng.Fork(stream));

    /// <summary>Daylight, open ground, a still Rex at (16,20) with its head toward the south.</summary>
    private static (Simulation Sim, Actor Rex) RexInTheOpen(int size = 40)
    {
        var sim = new Simulation(new WorldMap(size, size), seed: 5) { AmbientLightPercent = 100 };
        var rex = sim.AddActor("Rex", 'T', new TilePos(16, 20), Species.Rex, new HoldStillController(), Direction.South);
        return (sim, rex);
    }

    /// <summary>A roofed hut (no walls) covering (24..26, 30..32).</summary>
    private static void AddHut(Simulation sim)
    {
        for (int y = 30; y <= 32; y++)
            for (int x = 24; x <= 26; x++)
                sim.Map.Structures.SetRoof(new TilePos(x, y), 30);
    }

    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    [Fact]
    public void SeeingTheRex_MakesItAThreat_AndFrightens()
    {
        var (sim, rex) = RexInTheOpen();
        var brain = Brain(sim, Calm);
        sim.AddActor("Marisol", 'h', new TilePos(16, 31), Species.Human, brain);

        sim.RunUntil(Ticks.FromSeconds(1));

        Assert.NotNull(brain.Threat);
        Assert.Equal(ContactKey.ForActor(rex.Id), brain.Threat.Key);
        Assert.Equal(ContactClass.Threat, brain.Threat.Class);
        Assert.True(brain.Fear >= HumanBrain.ShelterFear, $"fear {brain.Fear}");
    }

    [Fact]
    public void ACloserThreat_IsScarier()
    {
        int FearAt(int y)
        {
            var (sim, _) = RexInTheOpen();
            var brain = Brain(sim, Calm);
            sim.AddActor("Marisol", 'h', new TilePos(16, y), Species.Human, brain);
            sim.RunUntil(10);
            return brain.Fear;
        }

        Assert.True(FearAt(28) > FearAt(36));
    }

    [Fact]
    public void ALoudTread_IsAThreat_QuietFootsteps_AreNot()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1) { AmbientLightPercent = 0 };
        var listener = sim.AddActor("Marisol", 'h', new TilePos(10, 10), Species.Human, new HoldStillController());
        var rex = sim.AddActor("Rex", 'T', new TilePos(10, 25), Species.Rex, new HoldStillController(), Direction.North);
        var pete = sim.AddActor("Pete", 'h', new TilePos(14, 10), Species.Human, new HoldStillController());
        sim.EmitNoise(rex.Position, Species.Rex.FootstepLoudness, NoiseKinds.Footsteps, rex.Id);
        sim.EmitNoise(pete.Position, Species.Human.FootstepLoudness, NoiseKinds.Footsteps, pete.Id);

        var memory = new ContactMemory(AiProfile.Human);
        memory.Observe(sim, listener, PerceptionSystem.Perceive(sim, listener, sinceTick: 0));

        Assert.Equal(ContactClass.Threat, memory.Find(ContactKey.ForActor(rex.Id))!.Class);
        Assert.NotEqual(ContactClass.Threat, memory.Find(ContactKey.ForActor(pete.Id))!.Class);
    }

    [Fact]
    public void AScream_TellsTheRexItsPrey_ButPeopleOnlyThatSomeoneIsInTrouble()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1) { AmbientLightPercent = 0 };
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 30), Species.Rex, new HoldStillController(), Direction.North);
        var listener = sim.AddActor("Marisol", 'h', new TilePos(26, 20), Species.Human, new HoldStillController());
        var screamer = sim.AddActor("Pete", 'h', new TilePos(20, 20), Species.Human,
            new ScriptedController(new ActorAction[] { new ScreamAction() }));
        var screams = new List<Actor>();
        sim.ActorScreamed += screams.Add;

        sim.RunUntil(10);

        Assert.Equal(new[] { screamer }, screams);
        var rexMemory = new ContactMemory(AiProfile.Rex);
        rexMemory.Observe(sim, rex, PerceptionSystem.Perceive(sim, rex, sinceTick: 0));
        var asPrey = rexMemory.Find(ContactKey.ForActor(screamer.Id))!;
        Assert.Equal(ContactClass.Prey, asPrey.Class);
        Assert.Equal(100, asPrey.Confidence(sim.CurrentTick, AiProfile.Rex));

        var humanMemory = new ContactMemory(AiProfile.Human);
        humanMemory.Observe(sim, listener, PerceptionSystem.Perceive(sim, listener, sinceTick: 0));
        var heard = humanMemory.Find(ContactKey.ForActor(screamer.Id))!;
        Assert.NotEqual(ContactClass.Prey, heard.Class);
        Assert.NotEqual(long.MinValue, heard.LastScreamTick);
    }

    [Fact]
    public void PastTheirThreshold_TheyPanic_RunAndScream()
    {
        var (sim, rex) = RexInTheOpen(size: 60);
        var brain = Brain(sim, new Traits(Courage: 20, Selfishness: 50, PanicThreshold: 20, KnowsMotionVision: false));
        var pete = sim.AddActor("Pete", 'h', new TilePos(16, 30), Species.Human, brain);
        int screams = 0;
        sim.ActorScreamed += a => screams += a == pete ? 1 : 0;

        sim.RunUntil(Ticks.FromSeconds(3));
        Assert.True(brain.Panicking);
        Assert.Equal(Mood.Panicking, brain.Mood);

        sim.RunUntil(Ticks.FromSeconds(20));
        Assert.True(screams > 0);
        Assert.True(Chebyshev(pete.Position, rex.Position) > 15, $"only got to {pete.Position}");
    }

    [Fact]
    public void ThoseWhoKnowItSeesMotion_Freeze_OthersRun()
    {
        TilePos After5s(bool knows, out HumanBrain brain)
        {
            var (sim, _) = RexInTheOpen();
            brain = Brain(sim, Calm with { KnowsMotionVision = knows });
            var person = sim.AddActor("Dr. Okafor", 'h', new TilePos(16, 31), Species.Human, brain);
            sim.RunUntil(Ticks.FromSeconds(5));
            return person.Position;
        }

        Assert.Equal(new TilePos(16, 31), After5s(knows: true, out var frozen));
        Assert.Equal(Mood.Frozen, frozen.Mood);
        Assert.NotEqual(new TilePos(16, 31), After5s(knows: false, out _));
    }

    [Fact]
    public void Frightened_TheyGetUnderARoof_AndStay()
    {
        var (sim, _) = RexInTheOpen();
        AddHut(sim);
        var brain = Brain(sim, Calm);
        var person = sim.AddActor("Marisol", 'h', new TilePos(16, 31), Species.Human, brain);

        sim.RunUntil(Ticks.FromSeconds(20));

        Assert.True(HumanBrain.IsShelter(sim, person.Position), $"still out at {person.Position}");
        var hidden = person.Position;
        sim.RunUntil(Ticks.FromSeconds(30));
        Assert.Equal(hidden, person.Position);
    }

    [Fact]
    public void IsShelter_IndoorsVehiclesAndDenseJungle()
    {
        var map = new WorldMap(10, 3);
        map.Structures.SetRoof(new TilePos(1, 1), 30);
        map.Cover[new TilePos(3, 1)] = CoverType.DenseJungle;
        map.Cover[new TilePos(5, 1)] = CoverType.SparseJungle;
        var sim = new Simulation(map, seed: 1);

        Assert.True(HumanBrain.IsShelter(sim, new TilePos(1, 1)));
        Assert.True(HumanBrain.IsShelter(sim, new TilePos(3, 1)));
        Assert.False(HumanBrain.IsShelter(sim, new TilePos(5, 1)));
        Assert.False(HumanBrain.IsShelter(sim, new TilePos(7, 1)));
    }

    [Fact]
    public void TheBrave_PatchUpABleedingCompanion()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 2) { AmbientLightPercent = 100 };
        var doctor = sim.AddActor("Dr. Okafor", 'h', new TilePos(8, 10), Species.Human,
            Brain(sim, new Traits(Courage: 70, Selfishness: 15, PanicThreshold: 101, KnowsMotionVision: false)),
            loadout: Loadout.Doctor);
        var pete = sim.AddActor("Pete", 'h', new TilePos(12, 10), Species.Human, new HoldStillController());
        sim.Injure(pete, BodySection.Torso, Severity.Red, bleeding: true, "test");
        var treated = new List<(Actor Treater, Actor Patient)>();
        sim.ActorTreated += (by, patient, _, worked) => { if (worked) treated.Add((by, patient)); };

        sim.RunUntil(Ticks.FromSeconds(15));

        Assert.False(pete.IsDead);
        Assert.False(pete.Health[BodySection.Torso].Bleeding);
        Assert.Equal(new[] { (doctor, pete) }, treated);
        Assert.Equal(1, doctor.Inventory.Count(ItemKind.FirstAidKit));
    }

    [Fact]
    public void TheTimid_DoNot()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 2) { AmbientLightPercent = 100 };
        sim.AddActor("Pete", 'h', new TilePos(8, 10), Species.Human,
            Brain(sim, new Traits(Courage: 25, Selfishness: 75, PanicThreshold: 101, KnowsMotionVision: false)),
            loadout: Loadout.Doctor);
        var hurt = sim.AddActor("Marisol", 'h', new TilePos(12, 10), Species.Human, new HoldStillController());
        sim.Injure(hurt, BodySection.Torso, Severity.Red, bleeding: true, "test");

        sim.RunUntil(Ticks.FromSeconds(15));

        Assert.True(hurt.Health[BodySection.Torso].Bleeding);
    }

    [Fact]
    public void Follow_KeepsThemCloseBehind()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 2) { AmbientLightPercent = 100 };
        var script = new List<ActorAction> { new OrderAction(1, NpcOrder.Follow) };
        script.AddRange(Enumerable.Repeat<ActorAction>(new MoveAction(Direction.East), 12));
        var leader = sim.AddActor("You", '@', new TilePos(12, 10), Species.Human, new ScriptedController(script));
        var brain = Brain(sim, Calm);
        var follower = sim.AddActor("Marisol", 'h', new TilePos(10, 10), Species.Human, brain);

        sim.RunUntil(Ticks.FromSeconds(30));

        Assert.Equal(new StandingOrder(NpcOrder.Follow, leader.Id, 0), follower.Order);
        Assert.Equal(new TilePos(24, 10), leader.Position);
        Assert.True(Chebyshev(follower.Position, leader.Position) <= 2, $"follower at {follower.Position}");
        Assert.Equal(Mood.Following, brain.Mood);
    }

    [Fact]
    public void Stay_KeepsThemPut_EvenWhenAfraid()
    {
        var (sim, _) = RexInTheOpen();
        AddHut(sim);
        sim.AddActor("You", '@', new TilePos(15, 31), Species.Human,
            new ScriptedController(new ActorAction[] { new OrderAction(2, NpcOrder.Stay) }));
        var brain = Brain(sim, Calm);
        var person = sim.AddActor("Marisol", 'h', new TilePos(16, 31), Species.Human, brain);

        sim.RunUntil(Ticks.FromSeconds(20));

        Assert.Equal(new TilePos(16, 31), person.Position);
        Assert.Equal(Mood.Staying, brain.Mood);
        Assert.False(brain.RefusingOrder);
    }

    [Fact]
    public void TheSelfish_IgnoreOrders_OnceAfraid()
    {
        var (sim, _) = RexInTheOpen();
        AddHut(sim);
        sim.AddActor("You", '@', new TilePos(15, 31), Species.Human,
            new ScriptedController(new ActorAction[] { new OrderAction(2, NpcOrder.Stay) }));
        var brain = Brain(sim, new Traits(Courage: 25, Selfishness: 75, PanicThreshold: 101, KnowsMotionVision: false));
        var pete = sim.AddActor("Pete", 'h', new TilePos(16, 31), Species.Human, brain);

        sim.RunUntil(Ticks.FromSeconds(20));

        Assert.True(brain.RefusingOrder);
        Assert.True(HumanBrain.IsShelter(sim, pete.Position), $"still out at {pete.Position}");
    }

    [Fact]
    public void Hide_SendsThemToCover_WithNothingToFear()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 2) { AmbientLightPercent = 100 };
        AddHut(sim);
        sim.AddActor("You", '@', new TilePos(17, 31), Species.Human,
            new ScriptedController(new ActorAction[] { new OrderAction(1, NpcOrder.Hide) }));
        var brain = Brain(sim, Calm);
        var person = sim.AddActor("Marisol", 'h', new TilePos(18, 31), Species.Human, brain);

        sim.RunUntil(Ticks.FromSeconds(20));

        Assert.Equal(0, brain.Fear);
        Assert.True(HumanBrain.IsShelter(sim, person.Position), $"still out at {person.Position}");
        Assert.Equal(Mood.Hiding, brain.Mood);
    }

    [Fact]
    public void Orders_OnlyCarryEightTiles()
    {
        var sim = new Simulation(new WorldMap(30, 10), seed: 1);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive);
        var near = sim.AddActor("Marisol", 'h', new TilePos(5 + Simulation.OrderRange, 5), Species.Human, new HoldStillController());
        var far = sim.AddActor("Pete", 'h', new TilePos(6 + Simulation.OrderRange, 5), Species.Human, new HoldStillController());
        var blocked = new List<string>();
        sim.MoveBlocked += (_, reason) => blocked.Add(reason);
        var given = new List<(Actor, Actor, NpcOrder)>();
        sim.OrderGiven += (by, to, order) => given.Add((by, to, order));
        sim.RunUntilInputNeeded();

        drive.Pending = new OrderAction(far.Id, NpcOrder.Hide);
        sim.RunUntilInputNeeded();
        drive.Pending = new OrderAction(near.Id, NpcOrder.Hide);
        sim.RunUntilInputNeeded();

        Assert.Equal(new[] { "Pete is too far away to hear you" }, blocked);
        Assert.Null(far.Order);
        Assert.Equal(NpcOrder.Hide, near.Order?.Order);
        Assert.Equal(new[] { (you, near, NpcOrder.Hide) }, given);
        Assert.Contains(sim.RecentNoises, n => n.Kind == NoiseKinds.Talking && n.ActorId == you.Id);
    }

    [Fact]
    public void FirstAidOnSomeoneElse_NeedsThemNextToYou()
    {
        var sim = new Simulation(new WorldMap(20, 20), seed: 3);
        var drive = new PlayerController();
        var you = sim.AddActor("You", '@', new TilePos(5, 5), Species.Human, drive, loadout: Loadout.Doctor);
        var far = sim.AddActor("Pete", 'h', new TilePos(8, 5), Species.Human, new HoldStillController());
        var near = sim.AddActor("Marisol", 'h', new TilePos(6, 6), Species.Human, new HoldStillController());
        sim.Injure(far, BodySection.LeftLeg, Severity.Red, bleeding: true, "test");
        sim.Injure(near, BodySection.LeftLeg, Severity.Red, bleeding: true, "test");
        var blocked = new List<string>();
        sim.MoveBlocked += (_, reason) => blocked.Add(reason);
        sim.RunUntilInputNeeded();

        drive.Pending = new TreatAction(far.Id);
        sim.RunUntilInputNeeded();
        drive.Pending = new TreatAction(near.Id);
        sim.RunUntilInputNeeded();

        Assert.Equal(new[] { "Pete is out of reach" }, blocked);
        Assert.True(far.Health[BodySection.LeftLeg].Bleeding);
        Assert.Equal(Severity.Yellow, near.Health[BodySection.LeftLeg].Severity);
        Assert.False(near.Health[BodySection.LeftLeg].Bleeding);
        Assert.Equal(1, you.Inventory.Count(ItemKind.FirstAidKit));
        Assert.Equal(Severity.Green, you.Health[BodySection.LeftLeg].Severity);
    }

    [Fact]
    public void PeopleWithBrains_RunDeterministically()
    {
        Simulation Run()
        {
            var (sim, _) = RexInTheOpen();
            AddHut(sim);
            sim.AddActor("Pete", 'h', new TilePos(16, 30), Species.Human,
                Brain(sim, new Traits(25, 75, 35, false), "npc:pete"));
            sim.AddActor("Dr. Okafor", 'h', new TilePos(20, 34), Species.Human,
                Brain(sim, new Traits(65, 15, 75, true), "npc:okafor"), loadout: Loadout.Doctor);
            sim.AddActor("Marisol", 'h', new TilePos(10, 36), Species.Human,
                Brain(sim, new Traits(70, 30, 60, false), "npc:marisol"), loadout: Loadout.Ranger);
            sim.RunUntil(Ticks.FromSeconds(60));
            return sim;
        }

        Assert.Equal(Run().ComputeStateHash(), Run().ComputeStateHash());
    }
}
