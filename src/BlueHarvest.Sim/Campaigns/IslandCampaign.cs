using System.Globalization;
using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Events;
using BlueHarvest.Sim.World.Generation;

namespace BlueHarvest.Sim.Campaigns;

/// <summary>
/// A campaign (contracts §8, P3c): one island, kept in one of the three save slots, across every survivor who comes to
/// it. It holds the expeditions and how each ended, the chronicle, the intermissions, and the wallet requests its runs
/// caused. The world itself (clock, actors and bodies, items, the island's changes) is the session's, and is saved
/// with the campaign as its world snapshot (<see cref="CampaignSave"/>).
/// <para>
/// <b>Ends.</b> The campaign watches its session. When the survivor dies or leaves, it records the end once (a
/// request with its own ID, so a replayed or reloaded game can't record it twice), notes it in the chronicle and asks
/// the profile to pay the run's score (<see cref="SettleWith"/>).
/// </para>
/// <para>
/// <b>Intermissions.</b> Then the days pass (<see cref="BeginIntermission"/>, <see cref="AdvanceIntermission"/>): 28
/// after a death, 14 after an extraction, an hour at a time, resumable. After that the island waits for its next
/// survivor, whose arrival comes in P7d.
/// </para>
/// </summary>
public sealed class IslandCampaign
{
    /// <summary>How many campaigns can be kept at once, each in its own slot (1 to 3).</summary>
    public const int SlotCount = 3;

    private readonly List<ExpeditionRecord> _expeditions = new();
    private readonly List<ChronicleEntry> _chronicle = new();
    private readonly List<IntermissionState> _intermissions = new();
    private readonly List<WalletRequest> _profileRequests = new();
    private WorldSession? _session;
    private IDisposable? _watching;

    private IslandCampaign(int slot, string id, IslandParams island)
    {
        if (slot is < 1 or > SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), $"Campaign slots are 1 to {SlotCount}.");
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(island);
        Slot = slot;
        Id = id;
        Island = island;
    }

    /// <summary>A new campaign on an island, with no expedition yet. Name it with <see cref="PlayerProfile.NewCampaignId"/>.</summary>
    public static IslandCampaign New(int slot, string id, IslandParams island) => new(slot, id, island);

    /// <summary>The save slot it's kept in, 1 to <see cref="SlotCount"/>.</summary>
    public int Slot { get; }

    /// <summary>The campaign's ID (<c>campaign:&lt;n&gt;</c>), distinct from its island's seed.</summary>
    public string Id { get; }

    /// <summary>The island: every expedition plays on the one generated from these.</summary>
    public IslandParams Island { get; }

    public CampaignPhase Phase { get; private set; }

    /// <summary>Every expedition, in order.</summary>
    public IReadOnlyList<ExpeditionRecord> Expeditions => _expeditions;

    /// <summary>The island's history, oldest first.</summary>
    public IReadOnlyList<ChronicleEntry> Chronicle => _chronicle;

    /// <summary>Every intermission, in order.</summary>
    public IReadOnlyList<IntermissionState> Intermissions => _intermissions;

    /// <summary>The wallet requests this campaign's runs caused, in order: offered to the profile by <see cref="SettleWith"/>.</summary>
    public IReadOnlyList<WalletRequest> ProfileRequests => _profileRequests;

    /// <summary>The expedition under way, if a survivor is on the island.</summary>
    public ExpeditionRecord? Current => Phase == CampaignPhase.Exploring ? _expeditions[^1] : null;

    /// <summary>The latest expedition, ended or not.</summary>
    public ExpeditionRecord? Last => _expeditions.Count > 0 ? _expeditions[^1] : null;

    /// <summary>The intermission under way, if one is.</summary>
    public IntermissionState? Intermission => Phase == CampaignPhase.Intermission ? _intermissions[^1] : null;

    /// <summary>The expeditions that ended in death: each survivor's body is still in the world, where it fell.</summary>
    public IEnumerable<ExpeditionRecord> Bodies => _expeditions.Where(e => e.Outcome == ExpeditionOutcome.Died);

    /// <summary>The session the campaign is playing in, once it has one.</summary>
    public WorldSession? Session => _session;

    // ---------- expeditions ----------

    /// <summary>
    /// The campaign's first expedition: the survivor lands as <see cref="IslandExpedition.Start"/> lands them, and the
    /// campaign starts watching the session for the end. The island given must be the campaign's.
    /// </summary>
    public Simulation StartExpedition(WorldSession session, IslandWorld world, IslandChunker chunker, IslandStart start,
        IActorController player, FeatureIndex? features = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(player);
        if (Phase != CampaignPhase.New)
            throw new InvalidOperationException("Only a new campaign's first expedition starts like this; the next survivor's arrival comes later (P7d).");
        if (world.Params != Island)
            throw new ArgumentException("That isn't this campaign's island.", nameof(world));
        Simulation sim = IslandExpedition.Start(session, world, chunker, start, player, features);
        Actor survivor = sim.Actors.Last(a => ReferenceEquals(a.Controller, player));
        int n = _expeditions.Count + 1;
        var record = new ExpeditionRecord(Ref("exp", n), n, Ref("person", n), survivor.Id, survivor.Name, sim.CurrentTick);
        _expeditions.Add(record);
        Phase = CampaignPhase.Exploring;
        Note(sim.CurrentTick, ChronicleKinds.ExpeditionStarted, record.Id, $"{survivor.Name} arrived on the island.",
            sim.ToIsland(survivor.Position));
        Attach(session);
        return sim;
    }

    /// <summary>
    /// Starts watching a session that plays on the campaign's island: after <see cref="StartExpedition"/> (which calls
    /// it) and after a save is put back (<see cref="CampaignFile.Restore"/> calls it).
    /// </summary>
    public void Attach(WorldSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Island is not { } window || window.World.Params != Island)
            throw new ArgumentException("That session doesn't play on this campaign's island.", nameof(session));
        _watching?.Dispose();
        _session = session;
        _watching = session.Observe(ObserverStage.Projections, Watch);
    }

    private WorldSession Attached() =>
        _session ?? throw new InvalidOperationException("The campaign has no session: start its expedition or put its save back first.");

    private void Watch(SimEvent e)
    {
        if (Current is not { } expedition)
            return;
        switch (e.Payload)
        {
            case ActorDiedPayload died when died.Actor.Id == expedition.ActorId:
                End(expedition, ExpeditionOutcome.Died, died.Cause, died.Actor, e.Tick);
                break;
            case ActorEscapedPayload left when left.Actor.Id == expedition.ActorId:
                End(expedition, ExpeditionOutcome.Extracted, left.Exit.Name, left.Actor, e.Tick);
                break;
        }
    }

    /// <summary>Records an expedition's end, once: the outcome, the chronicle, the score's wallet request, the event.</summary>
    private void End(ExpeditionRecord expedition, ExpeditionOutcome outcome, string detail, Actor survivor, long tick)
    {
        WorldSession session = Attached();
        Simulation sim = session.Surface;
        (long X, long Y) at = sim.ToIsland(survivor.Position);
        bool deck = survivor.OnDeck;
        session.CommitOnce($"{Id}/{expedition.Id}/ended", () =>
        {
            expedition.Outcome = outcome;
            expedition.EndTick = tick;
            expedition.Detail = detail;
            expedition.EndAt = at;
            expedition.EndOnDeck = deck;
            // the run's score and intermission currency come with the scoring rules (P7b)
            expedition.Score = 0;
            expedition.IntermissionCurrency = 0;
            var score = new WalletRequest($"{Id}/{expedition.Id}/score", WalletKind.Earn, expedition.Score, "run score", Id, expedition.Id);
            _profileRequests.Add(score);
            expedition.ScoreRequestId = score.RequestId;
            Phase = CampaignPhase.Ended;
            if (outcome == ExpeditionOutcome.Died)
                Note(tick, ChronicleKinds.SurvivorDied, expedition.Id, $"{expedition.SurvivorName} died: {detail}.", at);
            else
                Note(tick, ChronicleKinds.SurvivorExtracted, expedition.Id, $"{expedition.SurvivorName} left the island by the {detail}.", at);
            session.Emit(new ExpeditionEndedPayload(survivor, expedition.Id, outcome, detail), tick);
        });
    }

    /// <summary>
    /// Offers the profile every wallet request the campaign's runs caused. Each is applied once, so this is safe to
    /// call after every end and after every load: a request the profile already has changes nothing.
    /// </summary>
    public IReadOnlyList<WalletResult> SettleWith(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return _profileRequests.Select(profile.Apply).ToList();
    }

    // ---------- intermissions ----------

    /// <summary>
    /// After an expedition has ended, the intermission begins: 28 days after a death, 14 after an extraction, less any
    /// days bought with the extraction's own intermission currency (down to <see cref="IntermissionRules.FloorDays"/>;
    /// whatever isn't spent is lost). Call it between turns. Then call <see cref="AdvanceIntermission"/> until it's done.
    /// </summary>
    public IntermissionState BeginIntermission(int daysBought = 0)
    {
        WorldSession session = Attached();
        if (Phase != CampaignPhase.Ended || Last is not { } expedition)
            throw new InvalidOperationException("An intermission begins once an expedition has ended.");
        int most = IntermissionRules.MostDaysBought(expedition.Outcome, expedition.IntermissionCurrency);
        if (daysBought < 0 || daysBought > most)
            throw new ArgumentOutOfRangeException(nameof(daysBought), $"This intermission can have 0 to {most} days bought off.");
        int number = _intermissions.Count + 1;
        IntermissionState? begun = null;
        session.CommitOnce($"{Id}/intermission:{number}/begun", () =>
        {
            long tick = session.Surface.CurrentTick;
            var state = new IntermissionState(number, expedition.Id, expedition.Outcome, daysBought, tick);
            _intermissions.Add(state);
            Phase = CampaignPhase.Intermission;
            session.Emit(new IntermissionBegunPayload(number, state.Days, daysBought), tick);
            begun = state;
        });
        return begun ?? throw new InvalidOperationException("The intermission couldn't begin now: call it between turns.");
    }

    /// <summary>
    /// Passes up to <paramref name="maxHours"/> hours of the intermission (all of it by default) and returns whether
    /// it's over. When it is, the chronicle notes the days that passed and the island waits for its next arrival.
    /// Stopped part way and saved, it resumes from where it was and comes out exactly the same.
    /// </summary>
    public bool AdvanceIntermission(int maxHours = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHours);
        WorldSession session = Attached();
        if (Intermission is not { } state)
            throw new InvalidOperationException("No intermission is under way.");
        Simulation sim = session.Surface;
        for (int i = 0; i < maxHours && sim.CurrentTick < state.TargetTick; i++)
            state.Add(sim.PassIntermissionHour(state.TargetTick));
        if (sim.CurrentTick < state.TargetTick)
            return false;
        session.CommitOnce($"{Id}/intermission:{state.Number}/ended", () =>
        {
            long tick = sim.CurrentTick;
            state.Done = true;
            Phase = CampaignPhase.AwaitingArrival;
            Note(tick, ChronicleKinds.IntermissionPassed, state.ExpeditionId,
                state.Days == 1 ? "A day passed." : string.Create(CultureInfo.InvariantCulture, $"{state.Days} days passed."), null);
            session.Emit(new IntermissionEndedPayload(state.Number, state.Days), tick);
        });
        return true;
    }

    // ---------- the chronicle ----------

    private void Note(long tick, string kind, string? expeditionId, string text, (long X, long Y)? at) =>
        _chronicle.Add(new ChronicleEntry(_chronicle.Count + 1, tick, kind, expeditionId, text, at));

    private static string Ref(string kind, int n) => string.Create(CultureInfo.InvariantCulture, $"{kind}:{n}");

    // ---------- put back from a save (CampaignSave) ----------

    internal static IslandCampaign Rebuild(int slot, string id, IslandParams island, CampaignPhase phase,
        IEnumerable<ExpeditionRecord> expeditions, IEnumerable<ChronicleEntry> chronicle,
        IEnumerable<IntermissionState> intermissions, IEnumerable<WalletRequest> requests)
    {
        var campaign = new IslandCampaign(slot, id, island) { Phase = phase };
        campaign._expeditions.AddRange(expeditions);
        campaign._chronicle.AddRange(chronicle);
        campaign._intermissions.AddRange(intermissions);
        campaign._profileRequests.AddRange(requests);
        return campaign;
    }
}
