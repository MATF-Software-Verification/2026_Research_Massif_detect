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

        const string caveat = "Massif cannot tell whether this memory is still reachable or has been leaked.";

        var description = suspect != null
            ? $"The last snapshot still contains {finalShare * 100:F1} % of the peak heap. " +
              $"The largest identified allocation site is {suspect}. {caveat}"
            : hasFinalTree
                ? $"The last snapshot still contains {finalShare * 100:F1} % of the peak heap. " +
                  $"No single allocation site holds enough of it to be identified. {caveat}"
                : $"The last snapshot still contains {finalShare * 100:F1} % of the peak heap. " +
                  $"It has no allocation tree, so no allocation site can be identified. {caveat}";

        var finding = new Finding
        {
            RuleId = Id,
            Title = "Memory present at the final snapshot",
            Severity = Severity.Info,
            Description = description,
            Suggestion = suspect != null
                ? $"Check whether memory allocated at {suspect} needs to remain allocated until the program ends. " +
                  "Run `valgrind --leak-check=full` to check for leaks."
                : "Check whether this memory needs to remain allocated until the program ends. " +
                  "Run `valgrind --leak-check=full` to check for leaks.",
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
