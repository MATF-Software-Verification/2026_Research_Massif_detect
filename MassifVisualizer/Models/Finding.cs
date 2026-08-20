using System.Collections.Generic;

namespace MassifVisualizer.Models;

public enum Severity { Info, Warning, Critical }

public class Finding
{
    public string RuleId { get; init; } = "";
    public string Title { get; init; } = "";
    public Severity Severity { get; init; }
    public double Confidence { get; init; }
    public string Description { get; init; } = "";
    public string Suggestion { get; init; } = "";
    public List<string> Evidence { get; } = new();
    public string? SuspectSite { get; init; }
    public int? EvidenceSnapshotIndex { get; init; }
    public double RangeStartT { get; init; }
    public double RangeEndT { get; init; } = 1.0;

    public string ConfidenceDisplay => $"{Confidence * 100:F0}%";
}
