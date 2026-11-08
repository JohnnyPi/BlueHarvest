using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Ai;
using BlueHarvest.Sim.Core;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>Contact memory: what a creature remembers, built only from its percepts.</summary>
public class ContactMemoryTests
{
    private static readonly AiProfile Profile = AiProfile.Rex;

    /// <summary>A Rex at (20,30) looking north (eyes at (20,26)) and a human 12 tiles in front of its eyes.</summary>
    private static (Simulation Sim, Actor Rex, Actor Human) Standoff(int ambient, bool humanMoves)
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1) { AmbientLightPercent = ambient };
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 30), Species.Rex, new HoldStillController(), Direction.North);
        IActorController human = humanMoves
            ? new ScriptedController(new ActorAction[] { new MoveAction(Direction.North) })
            : new HoldStillController();
        var actor = sim.AddActor("Pete", 'h', new TilePos(20, humanMoves ? 15 : 14), Species.Human, human);
        sim.RunUntil(60);
        return (sim, rex, actor);
    }

    private static ContactMemory Observe(Simulation sim, Actor self)
    {
        var memory = new ContactMemory(Profile);
        memory.Observe(sim, self, PerceptionSystem.Perceive(sim, self, sinceTick: 0));
        return memory;
    }

    [Fact]
    public void MovingHuman_InDaylight_IsCertainPrey()
    {
        var (sim, rex, human) = Standoff(ambient: 100, humanMoves: true);

        var contact = Observe(sim, rex).Find(ContactKey.ForActor(human.Id));

        Assert.NotNull(contact);
        Assert.Equal(ContactClass.Prey, contact.Class);
        Assert.Equal(100, contact.Confidence(sim.CurrentTick, Profile)); // score 70 + 50, capped
        Assert.Equal(new TilePos(20, 14), contact.Estimate);
        Assert.Equal(0, contact.Uncertainty(sim.CurrentTick, Profile));
        Assert.Equal("seen", contact.Cue);
    }

    [Fact]
    public void StillHuman_AtNight_LeavesNoContact()
    {
        var (sim, rex, _) = Standoff(ambient: 6, humanMoves: false);

        Assert.Empty(Observe(sim, rex).All); // sight score 3: not even a glimpse
    }

    [Fact]
    public void Contacts_FadeAndSpread_ThenAreForgotten()
    {
        var (sim, rex, human) = Standoff(ambient: 100, humanMoves: true);
        var memory = Observe(sim, rex);
        var contact = memory.Find(ContactKey.ForActor(human.Id))!;
        long now = sim.CurrentTick;

        Assert.Equal(60, contact.Confidence(now + 500, Profile));  // 8 per second for 5 s
        Assert.Equal(5, contact.Uncertainty(now + 500, Profile));  // a tile per second

        memory.Forget(now + 500);
        Assert.Single(memory.All);
        memory.Forget(now + 1300);
        Assert.Empty(memory.All);
    }

    [Fact]
    public void Footsteps_BehindIt_AreAnUnknownWithAFuzzyEstimate()
    {
        var sim = new Simulation(new WorldMap(40, 40), seed: 1) { AmbientLightPercent = 6 };
        var rex = sim.AddActor("Rex", 'T', new TilePos(20, 20), Species.Rex, new HoldStillController(), Direction.North);
        var human = sim.AddActor("Pete", 'h', new TilePos(25, 26), Species.Human,
            new ScriptedController(new ActorAction[] { new MoveAction(Direction.East) }));
        sim.RunUntil(60);

        var contact = Observe(sim, rex).Find(ContactKey.ForActor(human.Id));

        Assert.NotNull(contact);
        Assert.Equal(ContactClass.Unknown, contact.Class);
        Assert.Equal(new TilePos(26, 26), contact.Estimate);
        Assert.Equal(28, contact.Confidence(sim.CurrentTick, Profile)); // heard at 7 (18 - 11 tiles) × 4
        Assert.Equal(2, contact.Uncertainty(sim.CurrentTick, Profile)); // 1 + 11 / 8
    }
}

/// <summary>The creature brain: utility choices, pursuit, bites and what bites teach it.</summary>
public class CreatureBrainTests
{
    private static (Simulation Sim, Actor Rex, CreatureBrain Brain) RexWithBrain(WorldMap map, TilePos at, Direction facing, int ambient)
    {
        var sim = new Simulation(map, seed: 7) { AmbientLightPercent = ambient };
        var brain = new CreatureBrain(AiProfile.Rex, sim.Rng.Fork("ai:rex"));
        var rex = sim.AddActor("Rex", 'T', at, Species.Rex, brain, facing);
        return (sim, rex, brain);
    }

    private static long Seconds(int seconds) => Ticks.FromSeconds(seconds);

    /// <summary>
    /// Runs until the first bite lands (or the time limit), then through the Rex's next turn, so its
    /// brain has felt the bite. Bites wound, and a bitten person can die soon after, so checks on the
    /// Rex's focus are made right after the first one.
    /// </summary>
    private static void RunUntilFirstBite(Simulation sim, Actor rex, Func<int> bites, long limit)
    {
        while (bites() == 0 && sim.CurrentTick < limit && sim.Step() != StepResult.Idle)
        {
        }
        if (bites() > 0)
            sim.RunUntil(rex.NextActTick);
    }

    [Fact]
    public void NothingPerceived_Wanders()
    {
        var (sim, _, brain) = RexWithBrain(new WorldMap(40, 40), new TilePos(20, 20), Direction.North, ambient: 6);

        sim.Step();

        Assert.Equal(Activity.Wandering, brain.Activity);
        Assert.Null(brain.Focus);
    }

    [Fact]
    public void HuntsAMovingHuman_AndBitesIt()
    {
        var (sim, rex, brain) = RexWithBrain(new WorldMap(40, 40), new TilePos(20, 30), Direction.North, ambient: 100);
        var pacing = Enumerable.Range(0, 60)
            .Select(i => (ActorAction)new MoveAction(i % 2 == 0 ? Direction.East : Direction.West))
            .ToList();
        var human = sim.AddActor("Pete", 'h', new TilePos(20, 15), Species.Human, new ScriptedController(pacing));
        int bites = 0;
        sim.ActorBitten += (_, victim) => bites += victim == human ? 1 : 0;

        RunUntilFirstBite(sim, rex, () => bites, Seconds(20));

        Assert.True(bites > 0, $"Rex never bit; it is at {rex.Position} and is {brain.Describe(sim)}.");
        Assert.Equal(ContactKey.ForActor(human.Id), brain.Focus?.Key);
        Assert.Equal(ContactClass.Prey, brain.Focus?.Class);
        Assert.False(EscapeCheck.Evaluate(sim, rex, brain, human).AttentionElsewhere);

        var director = new Director();
        director.Update(sim, human);
        Assert.True(director.Tension >= 60, $"Tension {director.Tension}");
    }

    [Fact]
    public void InvestigatesANoise_ThenLosesInterest()
    {
        var (sim, _, brain) = RexWithBrain(new WorldMap(40, 40), new TilePos(20, 20), Direction.North, ambient: 6);
        sim.EmitNoise(new TilePos(32, 20), 30, "a crash");

        sim.Step();
        Assert.Equal(Activity.Investigating, brain.Activity);
        Assert.Equal(ContactSource.Sound, brain.Focus?.Key.Source);

        sim.RunUntil(Seconds(60));
        Assert.DoesNotContain(brain.Memory.All, c => c.Key.Source == ContactSource.Sound);
        Assert.Equal(Activity.Wandering, brain.Activity);
    }

    [Fact]
    public void TestBitesAStrangeLight_ThenIgnoresIt()
    {
        var (sim, _, brain) = RexWithBrain(new WorldMap(40, 40), new TilePos(20, 20), Direction.North, ambient: 6);
        sim.AddLight(LightSource.Emergency(new TilePos(20, 8)));
        var bites = new List<BiteResult>();
        sim.Bit += (_, bite) => bites.Add(bite);

        sim.Step();
        Assert.Equal(new ContactKey(ContactSource.Light, 0), brain.Focus?.Key);

        sim.RunUntil(Seconds(30));
        Assert.Contains(bites, b => b.Investigative);
        Assert.Equal(ContactClass.Object, brain.Memory.Find(new ContactKey(ContactSource.Light, 0))?.Class);
        Assert.Equal(Activity.Wandering, brain.Activity);
    }

    /// <summary>A two-row vehicle at x 18-21, y 9-10, a Rex 12 tiles south of it, and Marisol inside on the near row.</summary>
    private static (Simulation Sim, Actor Rex, CreatureBrain Brain, Actor Marisol, Vehicle Vehicle) RexAndVehicle(
        int ambient, IActorController marisol, bool headlight)
    {
        var map = new WorldMap(40, 40);
        for (int x = 18; x <= 21; x++)
        {
            map.Cover[new TilePos(x, 9)] = CoverType.Vehicle;
            map.Cover[new TilePos(x, 10)] = CoverType.Vehicle;
        }
        var (sim, rex, brain) = RexWithBrain(map, new TilePos(21, 22), Direction.North, ambient);
        // The headlight is on the near row: one on the far row is mostly hidden by the vehicle's body.
        if (headlight)
            sim.AddLight(LightSource.Headlight(new TilePos(21, 10), Direction.East));
        var human = sim.AddActor("Marisol", 'h', new TilePos(20, 10), Species.Human, marisol);
        return (sim, rex, brain, human, Assert.Single(sim.Vehicles));
    }

    [Fact]
    public void TestBiteOnAnIntactVehicle_DamagesIt_ButCantReachInside()
    {
        var (sim, rex, brain, human, vehicle) = RexAndVehicle(ambient: 6, new HoldStillController(), headlight: true);
        int bites = 0;
        sim.ActorBitten += (_, victim) => bites += victim == human ? 1 : 0;

        sim.Step();
        Assert.Equal(Activity.Investigating, brain.Activity); // drawn to the headlight; can't see her

        sim.RunUntil(Seconds(15));
        Assert.Equal(0, bites);
        Assert.True(vehicle.Integrity < Vehicle.MaxIntegrity, $"Vehicle untouched; the Rex is at {rex.Position}.");
        Assert.False(vehicle.Breached);
        Assert.Equal(ContactClass.Object, brain.Memory.Find(new ContactKey(ContactSource.Light, 0))?.Class);
        Assert.NotEqual(ContactKey.ForActor(human.Id), brain.Focus?.Key);
    }

    [Fact]
    public void TestBiteOnABreachedVehicle_FindsSomeoneInside_AndTurnsIntoAHunt()
    {
        var (sim, rex, brain, human, vehicle) = RexAndVehicle(ambient: 6, new HoldStillController(), headlight: true);
        sim.SetVehicleIntegrity(vehicle, 0);
        int bites = 0;
        sim.ActorBitten += (_, victim) => bites += victim == human ? 1 : 0;

        RunUntilFirstBite(sim, rex, () => bites, Seconds(15));
        Assert.True(bites > 0, $"Rex never bit her; it is at {rex.Position} and is {brain.Describe(sim)}.");
        Assert.Equal(ContactClass.Object, brain.Memory.Find(new ContactKey(ContactSource.Light, 0))?.Class);
        Assert.Equal(ContactKey.ForActor(human.Id), brain.Focus?.Key);
        Assert.Equal(ContactClass.Prey, brain.Focus?.Class);
    }

    [Fact]
    public void PreyItCanSee_InAVehicle_GetsChewedOut()
    {
        var pacing = Enumerable.Range(0, 60)
            .Select(i => (ActorAction)new MoveAction(i % 2 == 0 ? Direction.East : Direction.West))
            .ToList();
        var (sim, rex, brain, human, vehicle) = RexAndVehicle(ambient: 100, new ScriptedController(pacing), headlight: false);
        int bites = 0;
        sim.ActorBitten += (_, victim) => bites += victim == human ? 1 : 0;

        sim.RunUntil(Seconds(20));
        Assert.True(vehicle.Breached, $"Integrity {vehicle.Integrity}; the Rex is at {rex.Position} and is {brain.Describe(sim)}.");
        Assert.True(bites > 0, $"Rex never bit her; it is at {rex.Position} and is {brain.Describe(sim)}.");
    }

    [Fact]
    public void Escape_NeedsOutOfReach_AndAttentionElsewhere()
    {
        var map = new WorldMap(40, 40);
        for (int i = 20; i <= 34; i++)
        {
            map.Cover[new TilePos(i, 10)] = CoverType.Wall;
            map.Cover[new TilePos(i, 24)] = CoverType.Wall;
            map.Cover[new TilePos(20, i - 10)] = CoverType.Wall;
            map.Cover[new TilePos(34, i - 10)] = CoverType.Wall;
        }
        var (walled, wRex, wBrain) = RexWithBrain(map, new TilePos(8, 30), Direction.North, ambient: 6);
        var inside = walled.AddActor("Pete", 'h', new TilePos(27, 17), Species.Human, new HoldStillController());

        var (open, oRex, oBrain) = RexWithBrain(new WorldMap(40, 40), new TilePos(8, 30), Direction.North, ambient: 6);
        var outside = open.AddActor("Pete", 'h', new TilePos(27, 17), Species.Human, new HoldStillController());

        var safe = EscapeCheck.Evaluate(walled, wRex, wBrain, inside);
        var exposed = EscapeCheck.Evaluate(open, oRex, oBrain, outside);

        Assert.True(safe.OutOfReach);
        Assert.True(safe.AttentionElsewhere); // it hasn't noticed anyone
        Assert.True(safe.Escaped);
        Assert.False(exposed.OutOfReach);
        Assert.False(exposed.Escaped);
    }

    [Fact]
    public void SameSeed_SameHunt()
    {
        ulong Run()
        {
            var (sim, _, _) = RexWithBrain(new WorldMap(40, 40), new TilePos(20, 30), Direction.North, ambient: 6);
            var wander = new RandomWalker(sim.Rng.Fork("ai:pete"));
            sim.AddActor("Pete", 'h', new TilePos(20, 12), Species.Human, wander);
            sim.RunUntil(Seconds(60));
            return sim.ComputeStateHash();
        }

        Assert.Equal(Run(), Run());
    }
}
