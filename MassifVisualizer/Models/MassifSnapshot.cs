using System.Collections.Generic;

namespace MassifVisualizer.Models;

public enum TreeType { Empty, Detailed, Peak }

public class MassifSnapshot
{
    public int Index { get; set; }
    public long Time { get; set; }
    public long MemHeapB { get; set; }
    public long MemHeapExtraB { get; set; }
    public long MemStacksB { get; set; }
    public TreeType TreeType { get; set; }
    public List<HeapNode> HeapTree { get; } = new();

    public long TotalB => MemHeapB + MemHeapExtraB + MemStacksB;

    public string TreeLabel => TreeType switch
    {
        TreeType.Peak     => "PEAK",
        TreeType.Detailed => "detail",
        _                 => ""
    };

    public string HeapDisplay => ByteFormatter.Format(MemHeapB);
}
