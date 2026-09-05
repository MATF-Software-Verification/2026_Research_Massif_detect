using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection;

public class AnalysisContext
{
    public required MassifProfile Profile { get; init; }
    public required DetectionThresholds Thresholds { get; init; }
    public required IReadOnlyList<MassifSnapshot> Snapshots { get; init; }
    public required IReadOnlyList<MassifSnapshot> Detailed { get; init; }
    public required double[] T { get; init; }
    public required double[] H { get; init; }
    public required double[] E { get; init; }
    public required double Peak { get; init; }
    public required int PeakIndex { get; init; }

    private readonly Dictionary<MassifSnapshot, int> _positionOf;
    private List<SiteSeries>? _sites;

    public List<SiteSeries> Sites => _sites ??= SiteSeriesBuilder.Build(Detailed);

    public AnalysisContext()
    {
        _positionOf = [];
    }

    public int PositionOf(MassifSnapshot snap) => _positionOf[snap];

    public double[] SiteT() => Detailed.Select(s => T[PositionOf(s)]).ToArray();

    public long TimeAt(double t)
    {
        var snaps = Snapshots;
        if (snaps.Count == 0) return 0;
        if (snaps.Count == 1) return snaps[0].Time;

        long t0 = snaps[0].Time;
        long tn = snaps[^1].Time;
        return t0 + (long)(t * (tn - t0));
    }

    public static AnalysisContext Build(MassifProfile profile, DetectionThresholds thresholds)
    {
        var snapshots = profile.Snapshots;
        var detailed = snapshots.Where(s => s.TreeType != TreeType.Empty).ToList();

        var times = snapshots.Select(s => s.Time).ToList();
        var t = Statistics.Normalize(times);
        var h = snapshots.Select(s => (double)s.MemHeapB).ToArray();
        var e = snapshots.Select(s => (double)s.MemHeapExtraB).ToArray();

        double peak = h.Length > 0 ? h.Max() : 0;
        int peakIndex = h.Length > 0 ? System.Array.IndexOf(h, peak) : -1;

        var ctx = new AnalysisContext
        {
            Profile = profile,
            Thresholds = thresholds,
            Snapshots = snapshots,
            Detailed = detailed,
            T = t,
            H = h,
            E = e,
            Peak = peak,
            PeakIndex = peakIndex
        };

        for (int i = 0; i < snapshots.Count; i++)
            ctx._positionOf[snapshots[i]] = i;

        return ctx;
    }
}
