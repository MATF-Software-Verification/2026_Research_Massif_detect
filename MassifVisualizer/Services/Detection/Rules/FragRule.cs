using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection.Rules;

public class FragRule : IDetectionRule
{
    public string Id => "FRAG";

    public IEnumerable<Finding> Analyze(AnalysisContext ctx)
    {
        var th = ctx.Thresholds;
        if (ctx.Snapshots.Count < th.MinSnapshots || ctx.Peak <= 0)
            yield break;

        // Use the median instead of the mean because small heaps can have unusually high ratios.
        // The median ignores this startup noise and other short-lived spikes.

        var ratios = Enumerable.Range(0, ctx.H.Length)
            .Where(i => ctx.H[i] > 0)
            .Select(i => ctx.E[i] / ctx.H[i])
            .ToList();
        if (ratios.Count == 0) yield break;

        double median = Statistics.Median(ratios);

        if (median <= th.FragMedianRatio) yield break;

        var finding = new Finding
        {
            RuleId = Id,
            Title = "High allocator overhead",
            Severity = Severity.Warning,
            Description = $"Across most of the run, bookkeeping accounted for {median * 100:F0} % as much memory " +
                          "as the useful data itself. " +
                          "That ratio is only reachable when the program allocates a very large number of very " +
                          "small blocks, so a substantial share of its memory is allocator tax rather than data. " +
                          "Note that mem_heap_extra_B is Massif's own estimate, derived from --heap-admin " +
                          "(8 bytes per block by default) plus alignment rounding — it is indicative, not measured.",
            Suggestion = "Allocate in bigger units: an arena or bump allocator for many short-lived objects, " +
                         "reserve() up front where the size is known, or batch small records into blocks " +
                         "instead of one allocation each.",
            EvidenceSnapshotIndex = ctx.Snapshots[ctx.PeakIndex].Index,
            RangeStartT = 0,
            RangeEndT = 1
        };

        finding.Evidence.Add($"Median overhead ratio: {median:F2} — threshold {th.FragMedianRatio:F2}");
        finding.Evidence.Add($"Peak useful heap: {ByteFormatter.Format((long)ctx.Peak)}, overhead at peak: {ByteFormatter.Format((long)ctx.E[ctx.PeakIndex])}");

        yield return finding;
    }
}
