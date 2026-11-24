using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BlueHarvest.Sim.Campaigns;
using BlueHarvest.Sim.Snapshots;
using Xunit;

namespace BlueHarvest.Sim.Tests;

/// <summary>P3c: the profile's one wallet across every campaign: each request applied once, nothing below zero, kept exactly.</summary>
public class ProfileTests
{
    private static WalletRequest Earn(string id, int points) => new(id, WalletKind.Earn, points, "test");

    private static WalletRequest Spend(string id, int points) => new(id, WalletKind.Spend, points, "test");

    private static byte[] Repack(byte[] saved, Action<JsonObject> change)
    {
        using var input = new GZipStream(new MemoryStream(saved), CompressionMode.Decompress);
        using var text = new MemoryStream();
        input.CopyTo(text);
        var doc = (JsonObject)JsonNode.Parse(text.ToArray())!;
        change(doc);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(doc.ToJsonString()));
        return output.ToArray();
    }

    [Fact]
    public void EachRequest_IsAppliedOnce_AndASpendBeyondTheBalanceLeavesNoTrace()
    {
        var profile = new PlayerProfile();
        Assert.Equal(new WalletResult(WalletOutcome.Applied, 10), profile.Apply(Earn("a", 10)));
        Assert.Equal(new WalletResult(WalletOutcome.AlreadyApplied, 10), profile.Apply(Earn("a", 10)));
        Assert.Equal(WalletOutcome.Applied, profile.Apply(Spend("b", 4)).Outcome);
        Assert.Equal(6, profile.Balance);

        WalletResult refused = profile.Apply(Spend("c", 7));
        Assert.Equal(WalletOutcome.Refused, refused.Outcome);
        Assert.Equal(6, refused.Balance);
        Assert.False(profile.HasApplied("c"));
        Assert.Equal(2, profile.Ledger.Count);

        // offered again once there's enough, it goes through
        profile.Apply(Earn("d", 5));
        Assert.Equal(WalletOutcome.Applied, profile.Apply(Spend("c", 7)).Outcome);
        Assert.Equal(4, profile.Balance);
        Assert.Equal(new[] { 10, 6, 11, 4 }, profile.Ledger.Select(e => e.BalanceAfter));

        Assert.Throws<ArgumentOutOfRangeException>(() => profile.Apply(Earn("e", -1)));
        Assert.Throws<ArgumentException>(() => profile.Apply(new WalletRequest("f", WalletKind.Unlock, 1, "test")));
    }

    [Fact]
    public void Unlocks_CostPoints_AndTheProfileReadsBackExactly()
    {
        var profile = new PlayerProfile();
        profile.Apply(Earn("campaign:1/exp:1/score", 3) with { CampaignId = "campaign:1", ExpeditionId = "exp:1" });
        Assert.Equal(WalletOutcome.Applied,
            profile.Apply(new WalletRequest("unlock:plane-crash", WalletKind.Unlock, 2, "a start", Unlock: "core:START-PLANE-CRASH")).Outcome);
        Assert.Equal(WalletOutcome.Refused,
            profile.Apply(new WalletRequest("unlock:halo", WalletKind.Unlock, 5, "a start", Unlock: "core:START-HALO")).Outcome);
        Assert.True(profile.IsUnlocked("core:START-PLANE-CRASH"));
        Assert.False(profile.IsUnlocked("core:START-HALO"));
        Assert.Equal("campaign:1", profile.NewCampaignId());

        PlayerProfile back = PlayerProfile.Read(profile.Save());
        Assert.Equal(1, back.Balance);
        Assert.Equal(profile.Ledger, back.Ledger);
        Assert.Equal(profile.Unlocks, back.Unlocks);
        Assert.Equal(1, back.CampaignsMade);
        Assert.Equal("campaign:2", back.NewCampaignId()); // a campaign ID is never given twice
        Assert.Equal(WalletOutcome.AlreadyApplied, back.Apply(Earn("campaign:1/exp:1/score", 3)).Outcome);
    }

    [Fact]
    public void AProfileWhoseLedgerDoesntAddUp_IsRefused()
    {
        var profile = new PlayerProfile();
        profile.Apply(Earn("a", 10));
        profile.Apply(Spend("b", 4));
        byte[] saved = profile.Save();
        Assert.Equal(6, PlayerProfile.Read(saved).Balance);

        Assert.Throws<SnapshotRefusedException>(() => PlayerProfile.Read(Repack(saved, d => d["balance"] = 600)));
        Assert.Throws<SnapshotRefusedException>(() => PlayerProfile.Read(Repack(saved, d => d["ledger"]![0]!["points"] = 100)));
        Assert.Throws<SnapshotRefusedException>(() => PlayerProfile.Read(Repack(saved, d => d["ledger"]![1]!["request"] = "a")));
        Assert.Throws<SnapshotRefusedException>(() => PlayerProfile.Read(Repack(saved, d => d["format"] = "blueharvest.profile/0")));
        Assert.Throws<SnapshotRefusedException>(() => PlayerProfile.Read(Encoding.UTF8.GetBytes("not a profile")));
    }
}
