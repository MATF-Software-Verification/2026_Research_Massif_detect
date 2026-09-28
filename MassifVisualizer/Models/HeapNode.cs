using System.Collections.Generic;

namespace MassifVisualizer.Models;

public class HeapNode
{
    public long Bytes { get; set; }
    public string Label { get; set; } = "";
    public List<HeapNode> Children { get; } = new();
    public int Depth { get; set; }

    public string DisplayText => Depth == 0
        ? $"{ByteFormatter.Format(Bytes)} — {Label}"
        : $"{new string(' ', Depth * 2)}{ByteFormatter.Format(Bytes)} — {Label}";
}
