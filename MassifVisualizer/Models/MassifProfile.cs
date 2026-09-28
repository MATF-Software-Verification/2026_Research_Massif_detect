using System.Collections.Generic;
using System.Linq;

namespace MassifVisualizer.Models;

public class MassifProfile
{
    public string Command { get; set; } = "";
    public string Description { get; set; } = "";
    public string TimeUnit { get; set; } = "i";
    public List<MassifSnapshot> Snapshots { get; } = new();

    public MassifSnapshot? PeakSnapshot => Snapshots.FirstOrDefault(s => s.TreeType == TreeType.Peak)
        ?? Snapshots.OrderByDescending(s => s.MemHeapB).FirstOrDefault();

    public long MaxHeap => Snapshots.Count > 0 ? Snapshots.Max(s => s.MemHeapB) : 0;
}
