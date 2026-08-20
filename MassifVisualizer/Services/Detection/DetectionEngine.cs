using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;
using MassifVisualizer.Services.Detection.Rules;

namespace MassifVisualizer.Services.Detection;

public static class DetectionEngine
{
    private static readonly IDetectionRule[] Rules = [new LeakRule()];

    public static List<Finding> Run(MassifProfile profile, DetectionThresholds? thresholds = null)
    {
        var ctx = AnalysisContext.Build(profile, thresholds ?? DetectionThresholds.Default);
        var findings = Rules.SelectMany(r => r.Analyze(ctx)).ToList();

        // cross-rule refinements go here

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ToList();
    }
}
