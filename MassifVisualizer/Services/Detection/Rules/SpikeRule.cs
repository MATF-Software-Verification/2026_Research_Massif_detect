using System;
using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection.Rules;

/// A sudden, material increase in heap usage. Consecutive jumps are treated as one event,
/// and the following part of the run determines whether that event was temporary.
public class SpikeRule : IDetectionRule
{
    public string Id => "SPIKE";

    private enum Recovery { Released, Retained, Unknown }

    public IEnumerable<Finding> Analyze(AnalysisContext ctx)
    {
        var th = ctx.Thresholds;
        if (ctx.Snapshots.Count < th.MinSnapshots || ctx.Peak <= 0)
            yield break;

        var jumps = new List<int>();
        for (int i = 1; i < ctx.H.Length; i++)
        {
            double rise = ctx.H[i] - ctx.H[i - 1];
            if (rise > Math.Max(th.SpikeRelativeJump * ctx.H[i - 1], th.SpikeAbsoluteJump * ctx.Peak))
                jumps.Add(i);
        }

        foreach (var (start, end) in MergeRuns(jumps))
            yield return BuildFinding(ctx, start, end);
    }

    private static List<(int Start, int End)> MergeRuns(List<int> jumps)
    {
        var runs = new List<(int, int)>();
        for (int i = 0; i < jumps.Count; i++)
        {
            int start = jumps[i];
            while (i + 1 < jumps.Count && jumps[i + 1] == jumps[i] + 1) i++;
            runs.Add((start, jumps[i]));
        }
        return runs;
    }

    private static Finding BuildFinding(AnalysisContext ctx, int start, int end)
    {
        var th = ctx.Thresholds;
        double before = ctx.H[start - 1];
        double after = ctx.H[end];
        double rise = after - before;

        var (recovery, retained) = ClassifyRecovery(ctx, end, before, rise);
        var (suspect, suspectGrowth) = Attribute(ctx, start, end, rise);

        string shape = recovery switch
        {
            Recovery.Released => "The heap later gave back at least 75% of this increase, so it looks like a temporary buffer.",
            Recovery.Retained => "Later snapshots were available, but the heap did not give back at least 75% of this increase.",
            _ => "There are no later snapshots inside the recovery window, so the outcome cannot be classified."
        };

        string attribution = suspect != null
            ? $"The immediately adjacent allocation trees show that {suspect} accounts for " +
              $"{suspectGrowth * 100.0 / rise:F0} % of the rise (+{ByteFormatter.Format(suspectGrowth)})."
            : "The immediately adjacent snapshots do not provide enough tree evidence to name one allocation site.";

        string relativeRise = before > 0
            ? $"{rise / before * 100:F0} % over the previous snapshot"
            : "from an empty heap";

        var finding = new Finding
        {
            RuleId = "SPIKE",
            Title = recovery switch
            {
                Recovery.Released => "Transient allocation spike",
                Recovery.Retained => "Step increase in heap usage",
                _ => "Allocation spike with unknown outcome"
            },
            Severity = recovery == Recovery.Retained ? Severity.Warning : Severity.Info,
            Description = $"Heap rose by {ByteFormatter.Format((long)rise)} ({relativeRise}, " +
                          $"{rise / ctx.Peak * 100:F0} % of peak) " +
                          $"{(start == end ? $"at snapshot #{ctx.Snapshots[start].Index}" : $"across snapshots #{ctx.Snapshots[start].Index}-#{ctx.Snapshots[end].Index}")}. " +
                          $"{shape} {attribution}",
            Suggestion = recovery == Recovery.Retained
                ? "Consider processing the data in chunks or placing an explicit bound on how much remains resident."
                : "If this temporary allocation sets an excessive peak, consider processing the data in smaller chunks.",
            SuspectSite = suspect,
            EvidenceSnapshotIndex = ctx.Snapshots[end].Index,
            RangeStartT = ctx.T[start - 1],
            RangeEndT = ctx.T[end]
        };

        finding.Evidence.Add($"Jump: {ByteFormatter.Format((long)before)} → {ByteFormatter.Format((long)after)} (+{ByteFormatter.Format((long)rise)})");
        finding.Evidence.Add(before > 0
            ? $"Relative rise: {rise / before * 100:F0} % — threshold {th.SpikeRelativeJump * 100:F0} %"
            : "Relative rise: previous heap was zero");
        finding.Evidence.Add($"Share of peak: {rise / ctx.Peak * 100:F0} % — threshold {th.SpikeAbsoluteJump * 100:F0} %");
        finding.Evidence.Add(recovery switch
        {
            Recovery.Released => $"Recovery: heap gave back {(1 - retained) * 100:F0} % of the rise within {th.SpikeRecoveryWindow * 100:F0} % of the run",
            Recovery.Retained => $"Recovery: heap gave back {(1 - retained) * 100:F0} % of the rise within {th.SpikeRecoveryWindow * 100:F0} % of the run — needs {(1 - th.SpikeRecoveryFraction) * 100:F0} %",
            _ => $"Recovery: no later snapshot falls within {th.SpikeRecoveryWindow * 100:F0} % of the run"
        });

        if (suspect != null)
            finding.Evidence.Add($"Largest adjacent-tree increase: {suspect} (+{ByteFormatter.Format(suspectGrowth)}, {suspectGrowth * 100.0 / rise:F0} % of the rise)");

        return finding;
    }

    private static (Recovery Verdict, double RetainedFraction) ClassifyRecovery(
        AnalysisContext ctx, int end, double before, double rise)
    {
        double deadline = ctx.T[end] + ctx.Thresholds.SpikeRecoveryWindow;
        var later = Enumerable.Range(end + 1, ctx.H.Length - end - 1)
            .Where(i => ctx.T[i] <= deadline)
            .ToList();

        if (later.Count == 0) return (Recovery.Unknown, 1);

        double lowest = later.Min(i => ctx.H[i]);
        double retained = Math.Clamp((lowest - before) / rise, 0, 1);
        return (retained <= ctx.Thresholds.SpikeRecoveryFraction ? Recovery.Released : Recovery.Retained,
                retained);
    }

    private static (string? Site, long Growth) Attribute(AnalysisContext ctx, int start, int end, double rise)
    {
        var beforeSnapshot = ctx.Snapshots[start - 1];
        var afterSnapshot = ctx.Snapshots[end];
        if (beforeSnapshot.TreeType == TreeType.Empty || afterSnapshot.TreeType == TreeType.Empty)
            return (null, 0);

        var before = SiteSeriesBuilder.SiteMap(beforeSnapshot);
        var after = SiteSeriesBuilder.SiteMap(afterSnapshot);
        var largest = after
            .Select(kv => (Site: kv.Key, Growth: kv.Value - before.GetValueOrDefault(kv.Key)))
            .Where(x => x.Growth > 0)
            .OrderByDescending(x => x.Growth)
            .FirstOrDefault();

        return largest.Growth >= ctx.Thresholds.SpikeMinAttributionShare * rise
            ? largest
            : (null, 0);
    }
}
