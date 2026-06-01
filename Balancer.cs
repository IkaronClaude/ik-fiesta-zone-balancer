namespace ZoneBalancer;

public sealed class Zone
{
    public int Id { get; init; }
    public int MobGroups { get; set; }
    public double Load { get; set; }
    public List<MapEntry> Maps { get; } = new();
}

public sealed class BalanceResult
{
    public required IReadOnlyList<Zone> Zones { get; init; }
    public int MinFeasibleZones { get; init; }
    public int TotalMobGroups { get; init; }
    public int MaxGroupsPerZone { get; init; }
}

public static class Balancer
{
    /// <summary>Smallest zone count that can hold every map under the mob-group
    /// cap: ceil(total / cap), but never fewer than the count of single maps
    /// that each already exceed the cap (those make it infeasible — reported).</summary>
    public static int MinFeasibleZones(IReadOnlyList<MapEntry> maps, int cap)
    {
        int total = maps.Sum(m => m.MobGroups);
        return Math.Max(1, (int)Math.Ceiling(total / (double)cap));
    }

    public static IEnumerable<MapEntry> OverCapMaps(IReadOnlyList<MapEntry> maps, int cap)
        => maps.Where(m => m.MobGroups > cap);

    /// <summary>
    /// Longest-processing-time greedy: place maps heaviest-load-first into the
    /// least-loaded zone that still has mob-group headroom. Even load spread,
    /// hard mob-group cap respected. Throws if N zones can't fit the groups.
    /// </summary>
    public static BalanceResult Balance(IReadOnlyList<MapEntry> maps, int zoneCount, int cap, int zoneBase)
    {
        var over = OverCapMaps(maps, cap).ToList();
        if (over.Count > 0)
            throw new InvalidOperationException(
                $"{over.Count} map(s) exceed the per-zone cap of {cap} mob groups and can never fit: "
                + string.Join(", ", over.Select(m => $"{m.MapId}({m.MobGroups})")));

        int min = MinFeasibleZones(maps, cap);
        if (zoneCount < min)
            throw new InvalidOperationException(
                $"target {zoneCount} zones can't hold {maps.Sum(m => m.MobGroups)} mob groups "
                + $"at a {cap} cap; need at least {min}. Raise --zones or --max-groups.");

        var zones = new List<Zone>();
        for (int i = 0; i < zoneCount; i++) zones.Add(new Zone { Id = zoneBase + i });

        // Heaviest load first; tie-break on mob groups so big-mob maps place early.
        var ordered = maps
            .OrderByDescending(m => m.Load)
            .ThenByDescending(m => m.MobGroups)
            .ThenBy(m => m.MapId, StringComparer.OrdinalIgnoreCase);

        foreach (var m in ordered)
        {
            Zone? best = null;
            foreach (var z in zones)
            {
                if (z.MobGroups + m.MobGroups > cap) continue;          // capacity gate
                if (best is null
                    || z.Load < best.Load
                    || (z.Load == best.Load && z.MobGroups < best.MobGroups))
                    best = z;
            }
            if (best is null)
                throw new InvalidOperationException(
                    $"could not place map '{m.MapId}' ({m.MobGroups} groups): every zone is at the "
                    + $"{cap}-group cap. Raise --zones or --max-groups.");

            best.Maps.Add(m);
            best.MobGroups += m.MobGroups;
            best.Load += m.Load;
            m.AssignedZone = best.Id;
        }

        return new BalanceResult
        {
            Zones = zones,
            MinFeasibleZones = min,
            TotalMobGroups = maps.Sum(m => m.MobGroups),
            MaxGroupsPerZone = cap,
        };
    }
}
