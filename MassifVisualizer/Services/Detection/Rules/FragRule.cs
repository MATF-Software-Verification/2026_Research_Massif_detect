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
            Description = $"The median allocator overhead was {median * 100:F0} % of the useful heap. " +
                          "Many small allocations may contribute to this overhead. " +
                          "Massif estimates this value from per-block overhead and alignment; it is not a direct measurement.",
            Suggestion = "Check for many small allocations. If possible, group them into larger blocks.",
            EvidenceSnapshotIndex = ctx.Snapshots[ctx.PeakIndex].Index,
            RangeStartT = 0,
            RangeEndT = 1
        };

        finding.Evidence.Add($"Median overhead ratio: {median:F2} — threshold {th.FragMedianRatio:F2}");
        finding.Evidence.Add($"Peak useful heap: {ByteFormatter.Format((long)ctx.Peak)}, overhead at peak: {ByteFormatter.Format((long)ctx.E[ctx.PeakIndex])}");

        yield return finding;
    }
}
