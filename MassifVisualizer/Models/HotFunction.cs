using System.Collections.Generic;

namespace MassifVisualizer.Models;

public class CallSite
{
    public string Label { get; set; } = "";
    public long Bytes { get; set; }
    public List<CallSite> Callers { get; } = new();
}

public class HotFunction
{
    public int Rank { get; set; }
    public string Label { get; set; } = "";
    public long TotalBytes { get; set; }
    public double PeakPercent { get; set; }
    public List<CallSite> CallChain { get; } = new();

    public string TotalDisplay => ByteFormatter.Format(TotalBytes);
    public string PeakPercentDisplay => $"{PeakPercent:F1}%";
}
