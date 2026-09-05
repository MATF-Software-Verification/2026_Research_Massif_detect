using System;
using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection.Rules;

/// Complement of LEAK: LEAK asks whether the heap grew all run, ATEXIT asks what was
/// still live when the program ended. A big object allocated once at startup and never
/// freed leaves a flat curve, so LEAK stays silent while this rule catches it.
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

        // Attribution needs a tree, and the last snapshot carrying one is not necessarily
        // the last snapshot. If the heap moved after it, its tree misrepresents the final
        // state -- report the global result rather than naming the wrong culprit.
        var lastTree = ctx.Detailed.LastOrDefault();
        double drift = lastTree != null ? Math.Abs(lastTree.MemHeapB - finalHeap) / ctx.Peak : 1.0;
        bool representative = lastTree != null && drift < th.AtExitMaxTreeDrift;

        var candidates = new List<(string Site, long Bytes)>();
        if (representative && lastTree!.MemHeapB > 0)
            candidates = SiteSeriesBuilder.SiteMap(lastTree)
                .Where(kv => kv.Value > th.AtExitMinSiteShare * lastTree.MemHeapB)
                .OrderByDescending(kv => kv.Value)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

        string? suspect = candidates.Count > 0 ? candidates[0].Site : null;

        double sample = Math.Clamp(ctx.Snapshots.Count / 30.0, 0, 1);
        double confidence = (0.5 + 0.5 * Math.Clamp(finalShare, 0, 1)) * (0.7 + 0.3 * sample);
        if (!representative) confidence *= 0.7;

        const string caveat = "Massif does not track pointers, so it cannot tell memory the program " +
                              "could still free (it holds the pointer, it just never got round to it) " +
                              "from memory whose pointer was lost. That is Memcheck's \"still reachable\" " +
                              "vs \"definitely lost\" distinction, so this is a candidate, not a verdict.";

        var description = suspect != null
            ? $"The program ended still holding {finalShare * 100:F1} % of its peak heap. " +
              $"{candidates.Count} allocation site(s) account for most of what was never released, " +
              $"the largest being {suspect}. {caveat}"
            : representative
                ? $"The program ended still holding {finalShare * 100:F1} % of its peak heap, but no single " +
                  $"allocation site holds enough of it to be named. {caveat}"
                : $"The program ended still holding {finalShare * 100:F1} % of its peak heap. The last snapshot " +
                  $"carrying an allocation tree differs from the final heap by {drift * 100:F1} % of peak, so it " +
                  $"cannot be trusted to describe the final state and no site is named. {caveat}";

        var finding = new Finding
        {
            RuleId = Id,
            Title = "Memory still allocated at exit",
            Severity = Severity.Warning,
            Confidence = confidence,
            Description = description,
            Suggestion = suspect != null
                ? $"Free the allocation at {suspect} before the program exits, or confirm it is intentionally " +
                  "held for the process lifetime. Run `valgrind --leak-check=full` to see whether it is " +
                  "definitely lost or merely still reachable."
                : "Run `valgrind --leak-check=full` to see whether the surviving memory is definitely lost " +
                  "or merely still reachable at exit.",
            SuspectSite = suspect,
            EvidenceSnapshotIndex = (lastTree ?? ctx.Snapshots[^1]).Index,
            RangeStartT = lastTree != null ? ctx.T[ctx.PositionOf(lastTree)] : 0,
            RangeEndT = 1
        };

        finding.Evidence.Add($"Heap at exit: {finalShare * 100:F1} % of peak — threshold {th.AtExitMinFinalShare * 100:F0} %");
        finding.Evidence.Add($"Final tree drift: {drift * 100:F1} % of peak — threshold {th.AtExitMaxTreeDrift * 100:F0} %"
                             + (representative ? "" : " (too large; attribution suppressed)"));
        foreach (var (site, bytes) in candidates.Take(5))
            finding.Evidence.Add($"Never freed: {ByteFormatter.Format(bytes)} ({bytes * 100.0 / lastTree!.MemHeapB:F1} % of final heap) at {site}");

        yield return finding;
    }
}
