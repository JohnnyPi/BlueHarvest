using System.Globalization;
using System.Text.Json.Nodes;
using BlueHarvest.Sim.Snapshots;
using static BlueHarvest.Sim.Snapshots.SnapshotJson;

namespace BlueHarvest.Sim.Campaigns;

/// <summary>
/// The player's profile (contracts §8, P3c): the one wallet across every campaign, kept as an append-only ledger of
/// requests, each applied once; what has been unlocked; and a counter that names new campaigns. Nothing in it is
/// about any particular island. Saved on its own, as <c>user://profile.json.gz</c> (P3d writes the file).
/// <para>Achievement proofs and priced unlocks come with Expedition Legacy (P15); the ledger already carries unlocks.</para>
/// </summary>
public sealed class PlayerProfile
{
    /// <summary>The format this build writes and reads.</summary>
    public const string Format = "blueharvest.profile/1";

    private readonly List<WalletEntry> _ledger = new();
    private readonly Dictionary<string, WalletEntry> _applied = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _unlocks = new(StringComparer.Ordinal);

    /// <summary>The points to spend.</summary>
    public int Balance { get; private set; }

    /// <summary>How many campaigns have been named (<see cref="NewCampaignId"/>).</summary>
    public int CampaignsMade { get; private set; }

    /// <summary>Every applied request, in the order applied.</summary>
    public IReadOnlyList<WalletEntry> Ledger => _ledger;

    /// <summary>What has been unlocked, in order of ID.</summary>
    public IReadOnlyCollection<string> Unlocks => _unlocks;

    /// <summary>Whether a request with this ID is in the ledger.</summary>
    public bool HasApplied(string requestId) => _applied.ContainsKey(requestId);

    public bool IsUnlocked(string unlock) => _unlocks.Contains(unlock);

    /// <summary>A new campaign's ID, never given before by this profile: <c>campaign:&lt;n&gt;</c>.</summary>
    public string NewCampaignId() => string.Create(CultureInfo.InvariantCulture, $"campaign:{++CampaignsMade}");

    /// <summary>
    /// Applies a request once. A request already applied changes nothing. A spend or unlock costing more than the
    /// balance is refused and leaves no trace, so it can be offered again later.
    /// </summary>
    public WalletResult Apply(WalletRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);
        if (request.Points < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "A wallet request never moves a negative number of points.");
        if (request.Kind == WalletKind.Unlock && string.IsNullOrWhiteSpace(request.Unlock))
            throw new ArgumentException("An unlock request must say what it unlocks.", nameof(request));
        if (_applied.ContainsKey(request.RequestId))
            return new WalletResult(WalletOutcome.AlreadyApplied, Balance);
        int balance = request.Kind == WalletKind.Earn ? Balance + request.Points : Balance - request.Points;
        if (balance < 0)
            return new WalletResult(WalletOutcome.Refused, Balance, $"It costs {request.Points} and the balance is {Balance}.");
        Record(new WalletEntry(request.RequestId, request.Kind, request.Points, request.Purpose, request.CampaignId,
            request.ExpeditionId, request.Unlock, balance));
        return new WalletResult(WalletOutcome.Applied, Balance);
    }

    private void Record(WalletEntry entry)
    {
        _ledger.Add(entry);
        _applied.Add(entry.RequestId, entry);
        Balance = entry.BalanceAfter;
        if (entry.Kind == WalletKind.Unlock)
            _unlocks.Add(entry.Unlock!);
    }

    // ---------- the file ----------

    /// <summary>The profile as gzipped JSON.</summary>
    public byte[] Save() => Pack(new JsonObject
    {
        ["format"] = Format,
        ["game"] = SimInfo.Version,
        ["campaignsMade"] = CampaignsMade,
        ["balance"] = Balance,
        ["ledger"] = new JsonArray(_ledger.Select(e => (JsonNode?)new JsonObject
        {
            ["request"] = e.RequestId,
            ["kind"] = Name(e.Kind),
            ["points"] = e.Points,
            ["purpose"] = e.Purpose,
            ["campaign"] = e.CampaignId,
            ["expedition"] = e.ExpeditionId,
            ["unlock"] = e.Unlock,
            ["balance"] = e.BalanceAfter,
        }).ToArray()),
    });

    /// <summary>
    /// Reads a saved profile, replaying its ledger: a ledger whose balances don't add up, a request applied twice or a
    /// file in another format is refused (<see cref="SnapshotRefusedException"/>).
    /// </summary>
    public static PlayerProfile Read(byte[] data)
    {
        JsonObject doc = Unpack(data, "a saved profile");
        string format = Val<string>(doc, "format");
        if (format != Format)
            throw new SnapshotRefusedException($"This profile is in format '{format}'; this build reads '{Format}'.");
        var profile = new PlayerProfile { CampaignsMade = Val<int>(doc, "campaignsMade") };
        int balance = 0;
        foreach (JsonNode? node in Arr(doc, "ledger"))
        {
            JsonObject e = Obj(node, "a ledger entry");
            var entry = new WalletEntry(Val<string>(e, "request"), EnumOf<WalletKind>(e, "kind"), Val<int>(e, "points"),
                Val<string>(e, "purpose"), OptString(e, "campaign"), OptString(e, "expedition"), OptString(e, "unlock"),
                Val<int>(e, "balance"));
            balance += entry.Kind == WalletKind.Earn ? entry.Points : -entry.Points;
            if (entry.Points < 0 || balance < 0 || entry.BalanceAfter != balance)
                throw Bad($"the ledger's balance doesn't add up at '{entry.RequestId}'");
            if (profile._applied.ContainsKey(entry.RequestId))
                throw Bad($"'{entry.RequestId}' is in the ledger twice");
            if (entry.Kind == WalletKind.Unlock && string.IsNullOrWhiteSpace(entry.Unlock))
                throw Bad($"the unlock '{entry.RequestId}' doesn't say what it unlocked");
            profile.Record(entry);
        }
        if (Val<int>(doc, "balance") != profile.Balance)
            throw Bad("the balance isn't what the ledger adds up to");
        return profile;
    }
}
