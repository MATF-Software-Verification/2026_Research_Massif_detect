using System;
using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection.Rules;

public class LeakRule : IDetectionRule
{
    public string Id => "LEAK";

    public IEnumerable<Finding> Analyze(AnalysisContext ctx)
    {
        var th = ctx.Thresholds;
        if (ctx.Snapshots.Count < th.MinSnapshots || ctx.Peak <= 0)
            yield break;

        double rho = Statistics.Spearman(ctx.T, ctx.H);
        double dd = Statistics.MaxDrawdown(ctx.H, ctx.Peak);
        double retention = ctx.H[^1] / ctx.Peak;

        if (rho <= th.LeakMonotonicity || dd >= th.LeakMaxDrawdown || retention <= th.LeakFinalRetention)
            yield break;

        int n = ctx.Snapshots.Count;
        double mono = Clamp01((rho - th.LeakMonotonicity) / 0.10);
        double drop = Clamp01((th.LeakMaxDrawdown - dd) / 0.10);
        double reten = Clamp01((retention - th.LeakFinalRetention) / 0.20);
        double sample = Clamp01(n / 30.0);
        double confidence = (0.55 + 0.45 * new[] { mono, drop, reten }.Average()) * (0.7 + 0.3 * sample);

        string? suspectSite = FindSuspectSite(ctx);
        string caveat = "a program designed to accumulate until exit (e.g. loading a full dataset before " +
                        "processing it) looks identical to a leak from a single Massif run — this is a " +
                        "suspicion, not a verdict.";

        var description = suspectSite != null
            ? $"Heap grew monotonically across the whole run, never released a significant amount, and ended " +
              $"near its peak. The most likely suspect is {suspectSite}, whose allocations grew the same way. " +
              $"Caveat: {caveat}"
            : $"Heap grew monotonically across the whole run, never released a significant amount, and ended " +
              $"near its peak. No single allocation site accounts for the growth clearly enough to name a " +
              $"suspect. Caveat: {caveat}";

        var suggestion = suspectSite != null
            ? $"Add the matching free() before control leaves the scope of the allocation at {suspectSite}; " +
              "confirm with `valgrind --leak-check=full` (Memcheck distinguishes definitely lost from still " +
              "reachable, which Massif cannot)."
            : "Audit allocation sites for a missing free(); confirm with `valgrind --leak-check=full` " +
              "(Memcheck distinguishes definitely lost from still reachable, which Massif cannot).";

        var finding = new Finding
        {
            RuleId = Id,
            Title = "Suspected memory leak",
            Severity = Severity.Warning,
            Confidence = confidence,
            Description = description,
            Suggestion = suggestion,
            SuspectSite = suspectSite,
            EvidenceSnapshotIndex = ctx.Snapshots[^1].Index,
            RangeStartT = 0,
            RangeEndT = 1
        };

        finding.Evidence.Add($"Monotonicity (Spearman ρ): {rho:F2} — threshold {th.LeakMonotonicity:F2}");
        finding.Evidence.Add($"Largest release: {dd * 100:F1} % of peak — threshold {th.LeakMaxDrawdown * 100:F0} %");
        finding.Evidence.Add($"Final heap: {retention * 100:F1} % of peak — threshold {th.LeakFinalRetention * 100:F0} %");
        finding.Evidence.Add($"Snapshots analyzed: {n}");

        yield return finding;
    }

    private static string? FindSuspectSite(AnalysisContext ctx)
    {
        if (ctx.Detailed.Count < 3) return null;

        var siteT = ctx.SiteT();
        var suspect = ctx.Sites
            .Where(s => Statistics.Spearman(siteT, s.Bytes) > ctx.Thresholds.LeakSiteMonotonicity)
            .OrderByDescending(s => s.Last - s.First)
            .FirstOrDefault();

        return suspect?.Site;
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);
}
