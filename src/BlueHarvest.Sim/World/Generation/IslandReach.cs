namespace BlueHarvest.Sim.World.Generation;

/// <summary>Whether two key sites can be walked between today, and if not, what is in the way.</summary>
public enum RouteStatus : byte
{
    /// <summary>A route on foot with no road tunnel and no river too deep to wade at base flow.</summary>
    Ok,
    /// <summary>The only routes use a road tunnel (impassable on foot until step 55).</summary>
    NeedsTunnel,
    /// <summary>The only routes cross a river too deep to wade at base flow.</summary>
    NeedsDeepWater,
    /// <summary>Every route needs a tunnel or a deep river, and none manages without both.</summary>
    NeedsBoth,
    /// <summary>A site has no walkable ground near it (nothing passable within 5 walking-grid cells).</summary>
    NoGround,
    /// <summary>Both sites have ground, but no walk joins them even through tunnels and deep water.</summary>
    NoRoute,
}

/// <summary>
/// One pair of key sites. Minutes is the walk today (tunnels and deep rivers shut), infinity unless Ok;
/// MinutesAnyWay lets tunnels and deep rivers be walked, as the island's Walk table does.
/// MustFord: walkable today, but only across a river, and every river floods past wading in heavy rain.
/// DryMinutes: the walk with every river over 1 m³/s shut as well (bridges stay open), as in a flood;
/// infinity unless Ok and not MustFord.
/// </summary>
public sealed record AnchorRoute(int FromSite, int ToSite, string From, string To, RouteStatus Status,
    double Minutes, double MinutesAnyWay, bool MustFord, double DryMinutes);

/// <summary>
/// Walking routes between every pair of the key sites (step 24b). AnchorSites are indices into
/// <see cref="IslandWorks.Sites"/>; Anchors are their names, in the same order.
/// </summary>
public sealed record IslandReach(IReadOnlyList<int> AnchorSites, IReadOnlyList<string> Anchors, IReadOnlyList<AnchorRoute> Routes)
{
    /// <summary>The objective anchors (decision 2): docks, airstrip, comms relay, the Court (in place of the control centre) and watch posts.</summary>
    public static IReadOnlyList<string> AnchorKinds { get; } = IslandWorks.AnchorKinds;

    public int Gaps => Routes.Count(r => r.Status != RouteStatus.Ok);
    public int TunnelGaps => Routes.Count(r => r.Status is RouteStatus.NeedsTunnel or RouteStatus.NeedsBoth);
    public int DeepGaps => Routes.Count(r => r.Status is RouteStatus.NeedsDeepWater or RouteStatus.NeedsBoth);
    public int GroundGaps => Routes.Count(r => r.Status is RouteStatus.NoGround or RouteStatus.NoRoute);
    public int MustFord => Routes.Count(r => r.MustFord);
    public bool FullyConnected => Gaps == 0;

    /// <summary>The longest walk between two key sites today, minutes (0 when no pair is walkable).</summary>
    public double SlowestMinutes => Routes.Where(r => r.Status == RouteStatus.Ok).Select(r => r.Minutes).DefaultIfEmpty(0).Max();

    /// <summary>
    /// How much longer the dry route (no river crossings, as in a flood) is than today's walk, at worst:
    /// 1 when no walkable pair needs a detour, over pairs that are walkable and have a dry route.
    /// </summary>
    public double WorstDryDetour => Routes.Where(r => r.Status == RouteStatus.Ok && !r.MustFord && r.Minutes > 0)
        .Select(r => r.DryMinutes / r.Minutes).DefaultIfEmpty(1).Max();

    /// <summary>Key sites that no other key site can walk to today.</summary>
    public IReadOnlyList<string> Unreached
    {
        get
        {
            var names = new List<string>();
            for (int k = 0; k < AnchorSites.Count; k++)
            {
                int site = AnchorSites[k];
                var mine = Routes.Where(r => r.FromSite == site || r.ToSite == site).ToList();
                if (mine.Count > 0 && mine.All(r => r.Status != RouteStatus.Ok)) names.Add(Anchors[k]);
            }
            return names;
        }
    }

    /// <summary>What a status means, for reports.</summary>
    public static string Describe(RouteStatus status) => status switch
    {
        RouteStatus.Ok => "walkable",
        RouteStatus.NeedsTunnel => "needs a road tunnel",
        RouteStatus.NeedsDeepWater => "crosses deep water",
        RouteStatus.NeedsBoth => "needs a tunnel and deep water",
        RouteStatus.NoGround => "no walkable ground at a site",
        _ => "no route at all",
    };
}
