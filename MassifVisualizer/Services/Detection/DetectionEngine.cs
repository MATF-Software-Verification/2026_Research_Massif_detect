using System;
using System.Collections.Generic;
using System.Linq;
using MassifVisualizer.Models;
using MassifVisualizer.Services.Detection.Rules;

namespace MassifVisualizer.Services.Detection;

public static class DetectionEngine
{
    private static readonly IDetectionRule[] Rules = [new LeakRule(), new AtExitRule(), new SpikeRule(), new FragRule()];

    public static List<Finding> Run(MassifProfile profile, DetectionThresholds? thresholds = null)
    {
        var ctx = AnalysisContext.Build(profile, thresholds ?? DetectionThresholds.Default);
        var findings = Rules.SelectMany(r => r.Analyze(ctx)).ToList();

        AddCrossRuleEvidence(findings);

        return findings
            .OrderByDescending(f => f.Severity)
            .ToList();
    }

    private static void AddCrossRuleEvidence(List<Finding> findings)
    {
        var leak = findings.FirstOrDefault(f => f.RuleId == "LEAK");
        var atExit = findings.FirstOrDefault(f => f.RuleId == "ATEXIT");

        if (leak?.SuspectSite == null || atExit?.SuspectSite == null) return;
        if (!string.Equals(leak.SuspectSite, atExit.SuspectSite, StringComparison.Ordinal)) return;

        atExit.Evidence.Add("The LEAK rule also found sustained growth at this allocation site");
    }
}
