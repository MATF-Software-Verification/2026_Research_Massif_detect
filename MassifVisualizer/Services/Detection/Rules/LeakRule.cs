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
        double growth = ctx.H[^1] - ctx.H[0];
        double growthShare = growth / ctx.Peak;
        double retention = ctx.H[^1] / ctx.Peak;

        if (rho <= th.LeakMonotonicity ||
            growthShare <= th.LeakMinGrowthShare ||
            retention <= th.LeakFinalRetention)
            yield break;

        int n = ctx.Snapshots.Count;
        var attribution = AnalyzeAttribution(ctx, growth);
        string? suspectSite = attribution.Site;
        string caveat = "a program designed to accumulate until exit (e.g. loading a full dataset before " +
                        "processing it) looks identical to a leak from a single Massif run — this is a " +
                        "suspicion, not a verdict.";

        string attributionDescription;
        if (suspectSite != null)
            attributionDescription = $"The most likely suspect is {suspectSite}, which has the largest recorded growth.";
        else if (!attribution.TreesCoverRun)
            attributionDescription = "The available allocation trees do not cover enough of the run to name a suspect.";
        else
            attributionDescription = "No single allocation site accounts for the growth clearly enough to name a suspect.";

        string description = "Heap grew monotonically across the whole run, never released a significant amount, " +
                             $"and ended near its peak. {attributionDescription} Caveat: {caveat}";

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
            Description = description,
            Suggestion = suggestion,
            SuspectSite = suspectSite,
            EvidenceSnapshotIndex = ctx.Snapshots[^1].Index,
            RangeStartT = 0,
            RangeEndT = 1
        };

        finding.Evidence.Add($"Monotonicity (Spearman ρ): {rho:F2} — threshold {th.LeakMonotonicity:F2}");
        finding.Evidence.Add($"Net growth: {ByteFormatter.Format((long)growth)} ({growthShare * 100:F1} % of peak) — threshold {th.LeakMinGrowthShare * 100:F0} %");
        finding.Evidence.Add($"Final heap: {retention * 100:F1} % of peak — threshold {th.LeakFinalRetention * 100:F0} %");
        finding.Evidence.Add($"Snapshots analyzed: {n}");
        finding.Evidence.Add(attribution.CoverageEvidence);
        if (suspectSite != null)
            finding.Evidence.Add($"Largest recorded site growth: {ByteFormatter.Format((long)attribution.Growth)} " +
                                 $"({attribution.Growth * 100 / growth:F1} % of net growth) at {suspectSite}");
        else if (attribution.TreesCoverRun && attribution.Growth > 0)
            finding.Evidence.Add($"Largest recorded site growth: {attribution.Growth * 100 / growth:F1} % of net growth " +
                                 $"— threshold {th.LeakMinSiteGrowthShare * 100:F0} %; no site named");

        yield return finding;
    }

    private static (string? Site, double Growth, string CoverageEvidence, bool TreesCoverRun)
        AnalyzeAttribution(AnalysisContext ctx, double totalGrowth)
    {
        if (ctx.Detailed.Count < 2)
            return (null, 0, "Attribution unavailable: fewer than two snapshots contain allocation trees", false);

        double firstTreeT = NormalizedTimeOf(ctx, ctx.Detailed[0]);
        double lastTreeT = NormalizedTimeOf(ctx, ctx.Detailed[^1]);
        double edge = ctx.Thresholds.LeakAttributionEdgeWindow;
        bool treesCoverRun = firstTreeT <= edge && lastTreeT >= 1 - edge;

        string coverageEvidence = treesCoverRun
            ? $"Allocation-tree coverage: first tree at {firstTreeT * 100:F0} %, last tree at {lastTreeT * 100:F0} % of the run"
            : $"Attribution unavailable: first tree at {firstTreeT * 100:F0} %, last tree at {lastTreeT * 100:F0} % " +
              $"— required at most {edge * 100:F0} % and at least {(1 - edge) * 100:F0} %";

        if (!treesCoverRun || totalGrowth <= 0)
            return (null, 0, coverageEvidence, treesCoverRun);

        var suspect = ctx.Sites
            .Select(s => (s.Site, Growth: s.Last - s.First))
            .Where(s => s.Growth > 0)
            .OrderByDescending(s => s.Growth)
            .FirstOrDefault();

        string? site = suspect.Growth >= ctx.Thresholds.LeakMinSiteGrowthShare * totalGrowth
            ? suspect.Site
            : null;

        return (site, suspect.Growth, coverageEvidence, true);
    }

    private static double NormalizedTimeOf(AnalysisContext ctx, MassifSnapshot snapshot)
    {
        for (int i = 0; i < ctx.Snapshots.Count; i++)
            if (ReferenceEquals(ctx.Snapshots[i], snapshot))
                return ctx.T[i];

        throw new System.InvalidOperationException("Allocation-tree snapshot is not part of the profile.");
    }
}
