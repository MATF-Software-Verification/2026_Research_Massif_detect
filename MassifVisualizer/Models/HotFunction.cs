using System.Collections.Generic;

namespace MassifVisualizer.Models;

public class CallSite
{
    public string Label { get; set; } = "";
    public long Bytes { get; set; }
    public List<CallSite> Callers { get; } = new();
}

/// One line inside a function where it allocates. Most functions have just the one.
public class AllocationSite
{
    public string Location { get; set; } = "";
    public long Bytes { get; set; }

    public string BytesDisplay => ByteFormatter.Format(Bytes);
}

public class HotFunction
{
    public int Rank { get; set; }
    public string Label { get; set; } = "";
    public long PeakBytes { get; set; }
    public int PeakSnapshotIndex { get; set; }
    public long FinalBytes { get; set; }
    public double SharePercent { get; set; }
    public List<AllocationSite> Sites { get; } = new();
    public List<CallSite> CallChain { get; } = new();

    public string PeakDisplay => ByteFormatter.Format(PeakBytes);
    public string FinalDisplay => ByteFormatter.Format(FinalBytes);
    public string SharePercentDisplay => $"{SharePercent:F1}%";
}
