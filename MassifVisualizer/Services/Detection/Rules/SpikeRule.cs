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

    /// What the heap did in the window after the spike. Unobservable is a real answer, not a
    /// failure: if the run ended too soon afterwards there is nothing to judge, and saying
    /// "the program kept it" would be asserting more than the data supports.
    private enum Recovery { Released, Retained, Unobservable }

    /// Which signal the verdict rests on -- reported, because the global one is not specific
    /// to the named function and other sites can mask it.
    private enum Basis { Site, Global }

    /// How far the tree diff can be trusted. Only Confident names a site: the other three all
    /// mean "something grew, but blaming one line of code here would be a guess".
    private enum Blame { None, Confident, Diluted, Contaminated }

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

        // Attribution first: the recovery test is about whether *this site* gave its bytes
        // back, so it needs to know who the site is before it can measure anything.
        var att = Attribute(ctx, start, end, rise);
        var (recovery, retained, basis) = ClassifyRecovery(ctx, end, before, rise, att);

        string shape = recovery switch
        {
            Recovery.Released => "The heap fell back to roughly its pre-spike level shortly afterwards, so this " +
                                 "looks like a temporary buffer. It still sets the program's peak memory, but it " +
                                 "is probably not a defect.",
            Recovery.Retained => "The heap did not come back down afterwards, so the program kept this memory. " +
                                 "That is either a legitimate working set or something large that was loaded and " +
                                 "never released.",
            _                 => "The spike lands on the last snapshot of the run, so there is nothing recorded " +
                                 "afterwards to say whether the memory was released. Profiling a run that does " +
                                 "more work after this point would settle it."
        };

        string attribution = att.Quality switch
        {
            Blame.Confident =>
                $"Comparing the nearest allocation trees on either side, {att.Site} accounts for " +
                $"{att.Share * 100:F0} % of the rise (+{ByteFormatter.Format(att.Delta)})." +
                (att.Exact ? "" : " Those trees are some distance from the jump itself, so this attribution is " +
                                  "best-effort -- Massif only records a tree every few snapshots."),

            Blame.Diluted =>
                $"No single site is responsible: the largest, {att.Top[0].Site}, accounts for only " +
                $"{att.Share * 100:F0} % of the rise. The {(att.Growers > att.Top.Count ? $"{att.Growers} sites sharing it are led by" : "sites sharing it are")} " +
                string.Join(", ", att.Top.Select(x => $"{x.Site} (+{ByteFormatter.Format(x.Delta)})")) + ".",

            Blame.Contaminated when att.Coverage < th.SpikeMinAttributionCoverage =>
                $"The nearest allocation trees do not explain the jump -- the sites that grew between them add up " +
                $"to only {att.Coverage * 100:F0} % of the rise -- so naming a call site here would be a guess. " +
                "Re-profiling with --detailed-freq=1 records a tree at every snapshot and makes attribution possible.",

            Blame.Contaminated =>
                "The nearest allocation trees either side of the jump span far more activity than the jump " +
                $"itself -- the sites that grew between them add up to {att.Coverage * 100:F0} % of the rise -- so " +
                "naming a call site here would be a guess. Re-profiling with --detailed-freq=1 records a tree at " +
                "every snapshot and makes attribution possible.",

            _ => "No allocation tree close enough to the jump to attribute it to a call site."
        };

        var finding = new Finding
        {
            RuleId = "SPIKE",
            Title = recovery switch
            {
                Recovery.Released => "Transient allocation spike",
                Recovery.Retained => "Step increase in heap usage",
                _                 => "Allocation spike at end of run (outcome unknown)"
            },
            Severity = recovery == Recovery.Retained ? Severity.Warning : Severity.Info,
            Confidence = Confidence(ctx, rise, recovery, att),
            Description = $"Heap rose by {ByteFormatter.Format((long)rise)} " +
                          $"({rise / before * 100:F0} % over the previous snapshot, {rise / ctx.Peak * 100:F0} % of peak) " +
                          $"{(start == end ? $"at snapshot #{ctx.Snapshots[start].Index}" : $"across snapshots #{ctx.Snapshots[start].Index}-#{ctx.Snapshots[end].Index}")}. " +
                          $"{shape} {attribution}",
            Suggestion = recovery == Recovery.Retained
                ? "Consider streaming or chunking this data instead of loading it whole (or mmap for a file). " +
                  "If it must stay resident, make sure the size is bounded by something other than the input."
                : "If this buffer only exists briefly, consider whether it needs to be materialised in full at " +
                  "all -- processing in chunks would lower the program's peak memory.",
            SuspectSite = att.Site,
            EvidenceSnapshotIndex = ctx.Snapshots[end].Index,
            RangeStartT = ctx.T[start - 1],
            RangeEndT = ctx.T[end]
        };

        finding.Evidence.Add($"Jump: {ByteFormatter.Format((long)before)} → {ByteFormatter.Format((long)after)} (+{ByteFormatter.Format((long)rise)})");
        finding.Evidence.Add($"Relative rise: {rise / before * 100:F0} % — threshold {th.SpikeRelativeJump * 100:F0} %");
        finding.Evidence.Add($"Share of peak: {rise / ctx.Peak * 100:F0} % — threshold {th.SpikeAbsoluteJump * 100:F0} %");
        finding.Evidence.Add(recovery == Recovery.Unobservable
            ? "Recovery: the spike lands on the final snapshot, so there is nothing after it to judge"
            : basis == Basis.Site
                ? $"Recovery: {att.Site} gave back {(1 - retained) * 100:F0} % of its own growth within {th.SpikeRecoveryWindow * 100:F0} % of the run — needs {(1 - th.SpikeRecoveryFraction) * 100:F0} %"
                : $"Recovery: total heap gave back {(1 - retained) * 100:F0} % of the rise within {th.SpikeRecoveryWindow * 100:F0} % of the run — needs {(1 - th.SpikeRecoveryFraction) * 100:F0} % "
                  + (att.Quality == Blame.Confident
                      ? "(measured on the overall curve, not this site — no allocation tree inside the window, so another site could be masking it)"
                      : "(measured on the overall curve — no one site could be blamed for the jump, so another site could be masking it)"));

        if (att.Quality != Blame.None)
        {
            finding.Evidence.Add($"Attribution coverage: sites growing across the jump total {att.Coverage * 100:F0} % of the rise" +
                (att.Exact
                    ? $" — trees sit exactly either side of the jump, so only the {th.SpikeMinAttributionCoverage * 100:F0} % floor applies"
                    : $" — must stay between {th.SpikeMinAttributionCoverage * 100:F0} % and {th.SpikeMaxAttributionCoverage * 100:F0} % for the trees to describe the jump"));
            finding.Evidence.Add($"Largest contributor: {att.Top[0].Site} (+{ByteFormatter.Format(att.Top[0].Delta)}) — " +
                                 $"{att.Share * 100:F0} % of the rise, needs {th.SpikeMinAttributionShare * 100:F0} % to be named as the culprit");
        }

        return finding;
    }

    /// Judge recovery against the spike itself, not against peak. A bar fixed at a share of
    /// peak is nearly as large as a spike that only just cleared the detection threshold, so
    /// such a spike would count as "recovered" while the program still held all of it.
    ///
    /// Measured on the suspect's own byte series where possible. The global curve cannot answer
    /// the question being asked: if one site allocates and an unrelated one frees the same
    /// amount, the total returns to baseline while the suspect still holds everything, and the
    /// spike would be called transient. Falls back to the global curve when there is no
    /// confident suspect or no tree recorded inside the window, and says so.
    ///
    /// On the global path, Massif records a snapshot whenever the heap changes, so a window
    /// with no snapshots means the level did not move -- absence of samples is evidence the
    /// level persisted. Only a spike on the very last snapshot is truly unobservable.
    private static (Recovery Verdict, double RetainedFraction, Basis Basis) ClassifyRecovery(
        AnalysisContext ctx, int end, double before, double rise, Attribution att)
    {
        if (end >= ctx.H.Length - 1) return (Recovery.Unobservable, 1, Basis.Global);

        double deadline = ctx.T[end] + ctx.Thresholds.SpikeRecoveryWindow;

        if (att.Quality == Blame.Confident && att.Delta > 0)
        {
            long lowestSite = long.MaxValue;
            foreach (var snap in ctx.Detailed)
            {
                int pos = ctx.PositionOf(snap);
                if (pos <= end || ctx.T[pos] > deadline) continue;
                lowestSite = Math.Min(lowestSite, SiteSeriesBuilder.SiteMap(snap).GetValueOrDefault(att.Site!));
            }

            if (lowestSite != long.MaxValue)
            {
                double keptSite = Math.Clamp((lowestSite - att.SiteBefore) / (double)att.Delta, 0, 1);
                return (keptSite <= ctx.Thresholds.SpikeRecoveryFraction ? Recovery.Released : Recovery.Retained,
                        keptSite, Basis.Site);
            }
        }

        double band = Math.Min(ctx.Thresholds.SpikeRecoveryBand * ctx.Peak,
                               ctx.Thresholds.SpikeRecoveryFraction * rise);

        double lowest = ctx.H[end];
        foreach (var i in Enumerable.Range(end + 1, ctx.H.Length - end - 1))
        {
            if (ctx.T[i] > deadline) break;
            lowest = Math.Min(lowest, ctx.H[i]);
        }

        double kept = rise > 0 ? Math.Clamp((lowest - before) / rise, 0, 1) : 1;
        return (lowest - before <= band ? Recovery.Released : Recovery.Retained, kept, Basis.Global);
    }

    /// What the tree diff says about who caused the jump, and how much of that to believe.
    private sealed record Attribution(
        Blame Quality,
        string? Site,
        long Delta,
        long SiteBefore,
        bool Exact,
        double Coverage,
        double Share,
        int Growers,
        List<(string Site, long Delta)> Top)
    {
        public static readonly Attribution Nothing = new(Blame.None, null, 0, 0, false, 0, 0, 0, []);
    }

    /// Diff the nearest recorded allocation trees either side of the jump, then decide whether
    /// that diff actually describes the jump. Two ways it can fail to:
    ///
    ///   coverage -- how much the growing sites add up to, relative to the rise. With sampled
    ///   trees the diff also contains everything that happened between the tree and the jump,
    ///   which can dwarf the jump itself and hand the blame to a completely unrelated function.
    ///   Not applied when the trees sit exactly either side: there the diff *is* the jump, and
    ///   coverage above 1 only means some other site freed while this one allocated.
    ///
    ///   share -- how much of the rise the biggest grower owns. A jump split evenly between
    ///   four sites has no culprit, and naming the one that happens to be a few bytes ahead
    ///   would point the reader at the wrong line.
    private static Attribution Attribute(AnalysisContext ctx, int start, int end, double rise)
    {
        var th = ctx.Thresholds;
        var beforeSnap = ctx.Detailed.LastOrDefault(s => ctx.PositionOf(s) <= start - 1);
        var afterSnap = ctx.Detailed.FirstOrDefault(s => ctx.PositionOf(s) >= end);
        if (beforeSnap == null || afterSnap == null || rise <= 0) return Attribution.Nothing;

        var pre = SiteSeriesBuilder.SiteMap(beforeSnap);
        var post = SiteSeriesBuilder.SiteMap(afterSnap);

        var grown = post
            .Select(kv => (Site: kv.Key, Delta: kv.Value - pre.GetValueOrDefault(kv.Key)))
            .Where(x => x.Delta > 0)
            .OrderByDescending(x => x.Delta)
            .ToList();
        if (grown.Count == 0) return Attribution.Nothing;

        bool exact = ctx.PositionOf(beforeSnap) == start - 1 && ctx.PositionOf(afterSnap) == end;
        double coverage = grown.Sum(x => x.Delta) / rise;
        double share = grown[0].Delta / rise;
        var top = grown.Take(th.SpikeContributorsListed).ToList();

        // Two-sided: the growing sites must add up to roughly the jump. Far above it means the
        // trees span activity that is not the jump; far below it means they fail to explain the
        // jump at all. Either way the biggest grower need not be the culprit.
        //
        // The upper bound is skipped when the trees sit exactly either side, because there the
        // sum of positive deltas cannot be below the rise -- anything above 1 is just some other
        // site freeing while this one allocated, which is a real finding, not noise. The lower
        // bound still applies: SiteMap ignores Massif's "in N places" aggregates, so a jump can
        // go unexplained even with a tree on the exact snapshot.
        if (coverage < th.SpikeMinAttributionCoverage ||
            (!exact && coverage > th.SpikeMaxAttributionCoverage))
            return new Attribution(Blame.Contaminated, null, 0, 0, exact, coverage, share, grown.Count, top);

        if (share < th.SpikeMinAttributionShare)
            return new Attribution(Blame.Diluted, null, 0, 0, exact, coverage, share, grown.Count, top);

        return new Attribution(Blame.Confident, grown[0].Site, grown[0].Delta,
                               pre.GetValueOrDefault(grown[0].Site), exact, coverage, share, grown.Count, top);
    }

    private static double Confidence(AnalysisContext ctx, double rise, Recovery recovery, Attribution att)
    {
        double size = Math.Clamp(rise / ctx.Peak / 0.5, 0, 1);
        double c = 0.55 + 0.35 * size;
        c += att.Quality switch
        {
            Blame.Confident    => att.Exact ? 0.10 : 0.05,
            Blame.Contaminated => -0.05,   // the spike is real; the trees describing it are not
            _                  => 0.0
        };
        if (recovery == Recovery.Unobservable) c *= 0.6;   // we saw the spike, not its outcome
        return Math.Clamp(c, 0, 1);
    }
}
