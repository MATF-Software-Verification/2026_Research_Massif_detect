using System;
using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services;

public static class HotFunctionAnalyzer
{
    public static List<HotFunction> Analyze(MassifProfile profile)
    {
        var peak = profile.PeakSnapshot;
        if (peak == null || peak.HeapTree.Count == 0) return [];

        var root = peak.HeapTree.FirstOrDefault();
        if (root == null || root.Children.Count == 0) return [];

        long peakBytes = peak.MemHeapB;

        return root.Children
            .Where(n => !n.Label.StartsWith("in ", StringComparison.Ordinal))
            .OrderByDescending(n => n.Bytes)
            .Select((node, idx) =>
            {
                var hf = new HotFunction
                {
                    Rank = idx + 1,
                    Label = node.Label,
                    TotalBytes = node.Bytes,
                    PeakPercent = peakBytes > 0 ? node.Bytes * 100.0 / peakBytes : 0
                };
                hf.CallChain.AddRange(node.Children.Select(ToCallSite));
                return hf;
            })
            .ToList();
    }

    private static CallSite ToCallSite(HeapNode node)
    {
        var site = new CallSite { Label = node.Label, Bytes = node.Bytes };
        site.Callers.AddRange(node.Children.Select(ToCallSite));
        return site;
    }
}
