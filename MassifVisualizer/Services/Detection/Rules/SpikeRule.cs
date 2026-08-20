using System;
using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection.Rules;

/// A single event that suddenly claims a large block -- reading a whole file, duplicating a
/// big structure, a resize that copies everything. Not necessarily a bug, but it is where the
/// program will fall over on a smaller machine, and usually where streaming beats slurping.
public class SpikeRule : IDetectionRule
{
    public string Id => "SPIKE";

    public IEnumerable<Finding> Analyze(AnalysisContext ctx)
    {
        var th = ctx.Thresholds;
        if (ctx.Snapshots.Count < th.MinSnapshots || ctx.Peak <= 0)
            yield break;

        // Both tests must pass. Relative alone is useless early on (16 B -> 48 B is +200%
        // and irrelevant); absolute alone would flag steady growth. Together: dramatic AND big.
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

    /// Successive snapshots that each jump are one event (a doubling loop, say), not several.
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

        bool transient = ReturnedToBaseline(ctx, end, before);
        var (suspect, delta, exact) = Attribute(ctx, start, end);

        string shape = transient
            ? "The heap fell back to roughly its pre-spike level shortly afterwards, so this looks like a " +
              "temporary buffer. It still sets the program's peak memory, but it is probably not a defect."
            : "The heap did not come back down afterwards, so the program kept this memory. That is either a " +
              "legitimate working set or something large that was loaded and never released.";

        string attribution = suspect == null
            ? "No allocation tree close enough to the jump to attribute it to a call site."
            : $"Comparing the nearest allocation trees on either side, the site that grew most is {suspect} " +
              $"(+{ByteFormatter.Format(delta)})." +
              (exact ? "" : " Those trees are some distance from the jump itself, so this attribution is " +
                            "best-effort -- Massif only records a tree every few snapshots.");

        var finding = new Finding
        {
            RuleId = "SPIKE",
            Title = transient ? "Transient allocation spike" : "Step increase in heap usage",
            Severity = transient ? Severity.Info : Severity.Warning,
            Confidence = Confidence(ctx, rise, suspect != null, exact),
            Description = $"Heap rose by {ByteFormatter.Format((long)rise)} " +
                          $"({rise / before * 100:F0} % over the previous snapshot, {rise / ctx.Peak * 100:F0} % of peak) " +
                          $"{(start == end ? $"at snapshot #{ctx.Snapshots[start].Index}" : $"across snapshots #{ctx.Snapshots[start].Index}-#{ctx.Snapshots[end].Index}")}. " +
                          $"{shape} {attribution}",
            Suggestion = transient
                ? "If this buffer only exists briefly, consider whether it needs to be materialised in full at " +
                  "all -- processing in chunks would lower the program's peak memory."
                : "Consider streaming or chunking this data instead of loading it whole (or mmap for a file). " +
                  "If it must stay resident, make sure the size is bounded by something other than the input.",
            SuspectSite = suspect,
            EvidenceSnapshotIndex = ctx.Snapshots[end].Index,
            RangeStartT = ctx.T[start - 1],
            RangeEndT = ctx.T[end]
        };

        finding.Evidence.Add($"Jump: {ByteFormatter.Format((long)before)} → {ByteFormatter.Format((long)after)} (+{ByteFormatter.Format((long)rise)})");
        finding.Evidence.Add($"Relative rise: {rise / before * 100:F0} % — threshold {th.SpikeRelativeJump * 100:F0} %");
        finding.Evidence.Add($"Share of peak: {rise / ctx.Peak * 100:F0} % — threshold {th.SpikeAbsoluteJump * 100:F0} %");
        finding.Evidence.Add($"Classified: {(transient ? "transient (heap recovered)" : "step (heap retained)")}");
        if (suspect != null)
            finding.Evidence.Add($"Largest growing site across the jump: {suspect} (+{ByteFormatter.Format(delta)})");

        return finding;
    }

    private static bool ReturnedToBaseline(AnalysisContext ctx, int end, double before)
    {
        double deadline = ctx.T[end] + ctx.Thresholds.SpikeRecoveryWindow;
        double band = ctx.Thresholds.SpikeRecoveryBand * ctx.Peak;

        for (int i = end + 1; i < ctx.H.Length && ctx.T[i] <= deadline; i++)
            if (ctx.H[i] - before <= band) return true;

        return false;
    }

    /// Diff the nearest recorded allocation trees either side of the jump. Sampling hurts most
    /// here: trees are sparse, so "nearest" can be far away. Reported honestly as best-effort.
    private static (string? Site, long Delta, bool Exact) Attribute(AnalysisContext ctx, int start, int end)
    {
        var beforeSnap = ctx.Detailed.LastOrDefault(s => ctx.PositionOf(s) <= start - 1);
        var afterSnap = ctx.Detailed.FirstOrDefault(s => ctx.PositionOf(s) >= end);
        if (beforeSnap == null || afterSnap == null) return (null, 0, false);

        var pre = SiteSeriesBuilder.SiteMap(beforeSnap);
        var post = SiteSeriesBuilder.SiteMap(afterSnap);

        var grown = post
            .Select(kv => (Site: kv.Key, Delta: kv.Value - pre.GetValueOrDefault(kv.Key)))
            .Where(x => x.Delta > 0)
            .OrderByDescending(x => x.Delta)
            .FirstOrDefault();

        if (grown.Site == null) return (null, 0, false);

        bool exact = ctx.PositionOf(beforeSnap) == start - 1 && ctx.PositionOf(afterSnap) == end;
        return (grown.Site, grown.Delta, exact);
    }

    private static double Confidence(AnalysisContext ctx, double rise, bool attributed, bool exact)
    {
        double size = Math.Clamp(rise / ctx.Peak / 0.5, 0, 1);
        double c = 0.55 + 0.35 * size;
        if (attributed) c += exact ? 0.10 : 0.05;
        return Math.Clamp(c, 0, 1);
    }
}
