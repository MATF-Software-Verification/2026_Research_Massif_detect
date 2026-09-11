using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection.Rules;

public class AtExitRule : IDetectionRule
{
    public string Id => "ATEXIT";

    public IEnumerable<Finding> Analyze(AnalysisContext ctx)
    {
        var th = ctx.Thresholds;
        if (ctx.Snapshots.Count < th.MinSnapshots || ctx.Peak <= 0)
            yield break;

        double finalHeap = ctx.H[^1];
        double finalShare = finalHeap / ctx.Peak;
        if (finalShare <= th.AtExitMinFinalShare)
            yield break;

        var finalSnapshot = ctx.Snapshots[^1];
        bool hasFinalTree = finalSnapshot.TreeType != TreeType.Empty;

        var candidates = new List<(string Site, long Bytes)>();
        if (hasFinalTree && finalSnapshot.MemHeapB > 0)
            candidates = SiteSeriesBuilder.SiteMap(finalSnapshot)
                .Where(kv => kv.Value > th.AtExitMinSiteShare * finalSnapshot.MemHeapB)
                .OrderByDescending(kv => kv.Value)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

        string? suspect = candidates.Count > 0 ? candidates[0].Site : null;

        const string caveat = "Massif does not track pointers, so it cannot tell memory the program " +
                              "could still free (it holds the pointer, it just never got round to it) " +
                              "from memory whose pointer was lost. That is Memcheck's \"still reachable\" " +
                              "vs \"definitely lost\" distinction, so this is a candidate, not a verdict.";

        var description = suspect != null
            ? $"The final snapshot still holds {finalShare * 100:F1} % of the peak heap. " +
              $"{candidates.Count} allocation site(s) each hold a meaningful share, " +
              $"the largest being {suspect}. {caveat}"
            : hasFinalTree
                ? $"The final snapshot still holds {finalShare * 100:F1} % of the peak heap, but no single " +
                  $"allocation site holds enough of it to be named. {caveat}"
                : $"The final snapshot still holds {finalShare * 100:F1} % of the peak heap, but it has no " +
                  $"allocation tree, so no site can be named. {caveat}";

        var finding = new Finding
        {
            RuleId = Id,
            Title = "Memory present at the final snapshot",
            Severity = Severity.Info,
            Description = description,
            Suggestion = suspect != null
                ? $"Review whether the allocation at {suspect} is intentionally held for the process lifetime. " +
                  "Run `valgrind --leak-check=full` to see whether it is " +
                  "definitely lost or merely still reachable."
                : "Run `valgrind --leak-check=full` to see whether the surviving memory is definitely lost " +
                  "or merely still reachable at exit.",
            SuspectSite = suspect,
            EvidenceSnapshotIndex = finalSnapshot.Index,
            RangeStartT = ctx.T[^1],
            RangeEndT = 1
        };

        finding.Evidence.Add($"Heap at final snapshot: {finalShare * 100:F1} % of peak — threshold {th.AtExitMinFinalShare * 100:F0} %");
        if (!hasFinalTree)
            finding.Evidence.Add("Attribution unavailable: the final snapshot has no allocation tree");
        foreach (var (site, bytes) in candidates.Take(5))
            finding.Evidence.Add($"Still present: {ByteFormatter.Format(bytes)} ({bytes * 100.0 / finalSnapshot.MemHeapB:F1} % of final heap) at {site}");

        yield return finding;
    }
}
