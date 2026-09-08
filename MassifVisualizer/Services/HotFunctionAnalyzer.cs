using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;
using MassifVisualizer.Services.Detection;

namespace MassifVisualizer.Services;

/// Ranks by the most a function ever held, over all detailed snapshots and not just the peak one.
/// Massif gives a function one node per allocation line, and those get added up here.
public static class HotFunctionAnalyzer
{
    public static List<HotFunction> Analyze(MassifProfile profile)
    {
        var detailed = profile.Snapshots.Where(s => s.TreeType != TreeType.Empty).ToList();
        if (detailed.Count == 0) return [];

        var peakOf = new Dictionary<string, (long Bytes, MassifSnapshot Snap)>();
        foreach (var snap in detailed)
            foreach (var kv in FunctionMap(snap))
                if (!peakOf.TryGetValue(kv.Key, out var current) || kv.Value > current.Bytes)
                    peakOf[kv.Key] = (kv.Value, snap);

        var final = FunctionMap(detailed[^1]);

        // Not a heap size: these peaks happened at different times. It is only here so the
        // shares add up to 100 %.
        long rankedTotal = peakOf.Values.Sum(v => v.Bytes);

        return peakOf
            .OrderByDescending(kv => kv.Value.Bytes)
            .Select((kv, idx) =>
            {
                var hf = new HotFunction
                {
                    Rank = idx + 1,
                    Label = kv.Key,
                    PeakBytes = kv.Value.Bytes,
                    PeakSnapshotIndex = kv.Value.Snap.Index,
                    FinalBytes = final.GetValueOrDefault(kv.Key),
                    SharePercent = rankedTotal > 0 ? kv.Value.Bytes * 100.0 / rankedTotal : 0
                };
                hf.Sites.AddRange(SitesOf(kv.Value.Snap, kv.Key));
                hf.CallChain.AddRange(CallersOf(kv.Value.Snap, kv.Key));
                return hf;
            })
            .ToList();
    }

    private static Dictionary<string, long> FunctionMap(MassifSnapshot snap)
    {
        var map = new Dictionary<string, long>();
        foreach (var kv in SiteSeriesBuilder.SiteMap(snap))
        {
            var function = FunctionKey(kv.Key);
            map[function] = map.GetValueOrDefault(function) + kv.Value;
        }

        return map;
    }

    private static IEnumerable<AllocationSite> SitesOf(MassifSnapshot snap, string function) =>
        SiteSeriesBuilder.SiteMap(snap)
            .Where(kv => FunctionKey(kv.Key) == function)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new AllocationSite { Location = LocationOf(kv.Key), Bytes = kv.Value });

    private static IEnumerable<CallSite> CallersOf(MassifSnapshot snap, string function)
    {
        var root = snap.HeapTree.FirstOrDefault();
        if (root == null) return [];

        return root.Children
            .Where(n => FunctionKey(SiteSeriesBuilder.SiteKey(n)) == function)
            .OrderByDescending(n => n.Bytes)
            .SelectMany(n => n.Children)
            .Select(ToCallSite);
    }

    /// "parse_line (logcrunch.c:74)" -> "parse_line (logcrunch.c)". The file stays, otherwise two
    /// static functions with the same name in different files would end up merged.
    private static string FunctionKey(string site)
    {
        int close = site.LastIndexOf(')');
        int colon = site.LastIndexOf(':');
        if (close < 0 || colon < 0 || colon > close) return site;

        return int.TryParse(site[(colon + 1)..close], out _) ? site[..colon] + site[close..] : site;
    }

    private static string LocationOf(string site)
    {
        int close = site.LastIndexOf(')');
        int open = close > 0 ? site.LastIndexOf('(', close) : -1;
        return open >= 0 ? site[(open + 1)..close] : site;
    }

    private static CallSite ToCallSite(HeapNode node)
    {
        var site = new CallSite { Label = SiteSeriesBuilder.SiteKey(node), Bytes = node.Bytes };
        site.Callers.AddRange(node.Children.Select(ToCallSite));
        return site;
    }
}
