namespace BlueHarvest.Sim.Campaigns;

/// <summary>What a wallet request does: pay points in, spend them, or spend them on an unlock.</summary>
public enum WalletKind
{
    Earn,
    Spend,
    Unlock,
}

/// <summary>
/// A change to the profile's wallet (contracts §8): it happens once, however often it's offered, because the profile
/// keeps every request ID it has applied. A campaign keeps the requests it caused and offers them again after a load,
/// so a crash between saving the campaign and saving the profile can't lose a payment or make one twice.
/// </summary>
/// <param name="RequestId">Unique across every campaign: the campaign's ID comes first (<c>campaign:2/exp:1/score</c>).</param>
/// <param name="Points">What it pays or costs; never negative (0 is allowed: a run that scored nothing still settles).</param>
/// <param name="Unlock">For <see cref="WalletKind.Unlock"/>: what it unlocks.</param>
public sealed record WalletRequest(string RequestId, WalletKind Kind, int Points, string Purpose,
    string? CampaignId = null, string? ExpeditionId = null, string? Unlock = null);

/// <summary>An applied request in the profile's ledger, with the balance it left.</summary>
public sealed record WalletEntry(string RequestId, WalletKind Kind, int Points, string Purpose,
    string? CampaignId, string? ExpeditionId, string? Unlock, int BalanceAfter);

/// <summary>What happened to a request.</summary>
public enum WalletOutcome
{
    /// <summary>It's in the ledger now.</summary>
    Applied,

    /// <summary>It was already in the ledger; nothing changed.</summary>
    AlreadyApplied,

    /// <summary>It couldn't be applied (more than the balance); nothing changed and nothing was recorded.</summary>
    Refused,
}

/// <summary>A request's outcome and the balance after it.</summary>
public sealed record WalletResult(WalletOutcome Outcome, int Balance, string? Reason = null);
