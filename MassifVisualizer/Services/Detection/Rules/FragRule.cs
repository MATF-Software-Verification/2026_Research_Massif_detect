using System;
using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection.Rules;

/// Death by a thousand cuts: a program making an enormous number of tiny allocations.
/// Every block carries a fixed tax -- a header stored beside it and the size rounded up.
/// Negligible on big blocks; on millions of small ones the tax can outweigh the payload.
/// This is the one signal Massif hands us directly, splitting useful bytes (mem_heap_B)
/// from bookkeeping (mem_heap_extra_B), so nothing has to be inferred.
public class FragRule : IDetectionRule
{
    public string Id => "FRAG";

    public IEnumerable<Finding> Analyze(AnalysisContext ctx)
    {
        var th = ctx.Thresholds;
        if (ctx.Snapshots.Count < th.MinSnapshots || ctx.Peak <= 0)
            yield break;

        // Median, not mean: while the heap is still tiny the ratio is meaninglessly high
        // (three 8-byte allocations can carry more admin than content). The median discards
        // that startup phase, and short anomalies, without any special-casing.
        var ratios = Enumerable.Range(0, ctx.H.Length)
            .Where(i => ctx.H[i] > 0)
            .Select(i => ctx.E[i] / ctx.H[i])
            .ToList();
        if (ratios.Count == 0) yield break;

        double median = Statistics.Median(ratios);

        // Independent second signal: as useful bytes arrive, how much overhead do they drag
        // in? A slope of 0.5 means every content byte costs half a byte of tax, only possible
        // if the new content is arriving in very small pieces. Catches the case where the
        // overall median is respectable but recent growth is pathologically fine-grained.
        var (slope, _) = Statistics.LinearFit(ctx.H, ctx.E);

        bool byMedian = median > th.FragMedianRatio;
        bool bySlope = slope > th.FragOverheadSlope;
        if (!byMedian && !bySlope) yield break;

        double confidence = byMedian
            ? Math.Clamp(0.55 + 0.45 * Math.Clamp((median - th.FragMedianRatio) / th.FragMedianRatio, 0, 1), 0, 1)
            : 0.5;   // slope alone is the weaker of the two signals
        if (byMedian && bySlope) confidence = Math.Min(1.0, confidence + 0.10);

        var finding = new Finding
        {
            RuleId = Id,
            Title = "High allocator overhead (many small allocations)",
            Severity = Severity.Warning,
            Confidence = confidence,
            Description = $"Across most of the run, bookkeeping accounted for {median * 100:F0} % as much memory " +
                          $"as the useful data itself{(bySlope ? $", and each additional byte of content brought {slope:F2} bytes of overhead with it" : "")}. " +
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

        finding.Evidence.Add($"Median overhead ratio: {median:F2} — threshold {th.FragMedianRatio:F2}{(byMedian ? "" : " (not met)")}");
        finding.Evidence.Add($"Overhead per byte of growth: {slope:F2} — threshold {th.FragOverheadSlope:F2}{(bySlope ? "" : " (not met)")}");
        finding.Evidence.Add($"Peak useful heap: {ByteFormatter.Format((long)ctx.Peak)}, overhead at peak: {ByteFormatter.Format((long)ctx.E[ctx.PeakIndex])}");

        yield return finding;
    }
}
