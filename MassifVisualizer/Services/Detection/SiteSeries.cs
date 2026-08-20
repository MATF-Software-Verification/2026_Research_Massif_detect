using System;
using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection;

public class SiteSeries
{
    public string Site { get; init; } = "";
    public double[] Bytes { get; init; } = [];

    public double First => Bytes.Length > 0 ? Bytes[0] : 0;
    public double Last => Bytes.Length > 0 ? Bytes[^1] : 0;
    public double Max => Bytes.Length > 0 ? Bytes.Max() : 0;
}

public static class SiteSeriesBuilder
{
    public static List<SiteSeries> Build(IReadOnlyList<MassifSnapshot> detailed)
    {
        var sites = new List<string>();
        var perSnapshot = new List<Dictionary<string, long>>();

        foreach (var snap in detailed)
        {
            var map = SiteMap(snap);
            foreach (var key in map.Keys)
                if (!sites.Contains(key)) sites.Add(key);
            perSnapshot.Add(map);
        }

        return sites.Select(site => new SiteSeries
        {
            Site = site,
            Bytes = perSnapshot.Select(m => (double)m.GetValueOrDefault(site)).ToArray()
        }).ToList();
    }

    /// Allocation sites recorded by one snapshot: the depth-1 children of the tree root,
    /// which is where Massif records the direct callers of malloc. The "in N places, below
    /// threshold" aggregates are not sites, so they are skipped -- meaning the values do
    /// not sum to MemHeapB.
    public static Dictionary<string, long> SiteMap(MassifSnapshot snap)
    {
        var map = new Dictionary<string, long>();
        var root = snap.HeapTree.FirstOrDefault();
        if (root == null) return map;

        foreach (var child in root.Children.Where(n => !n.Label.StartsWith("in ", StringComparison.Ordinal)))
        {
            var key = SiteKey(child);
            map[key] = map.GetValueOrDefault(key) + child.Bytes;
        }

        return map;
    }

    public static string SiteKey(HeapNode node)
    {
        var label = node.Label;
        var colonIdx = label.IndexOf(':');
        if (label.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && colonIdx > 0)
            return label[(colonIdx + 1)..].Trim();
        return label;
    }
}
