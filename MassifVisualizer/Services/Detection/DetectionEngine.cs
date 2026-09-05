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

        Escalate(findings);

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ToList();
    }

    /// Cross-rule refinements: two rules reaching the same conclusion by different routes is
    /// stronger evidence than either alone. A site that grew monotonically all run (LEAK) and
    /// was still holding memory at exit (ATEXIT) is the most reliable finding this engine makes.
    private static void Escalate(List<Finding> findings)
    {
        var leak = findings.FirstOrDefault(f => f.RuleId == "LEAK");
        var atExit = findings.FirstOrDefault(f => f.RuleId == "ATEXIT");

        if (leak?.SuspectSite == null || atExit?.SuspectSite == null) return;
        if (!string.Equals(leak.SuspectSite, atExit.SuspectSite, StringComparison.Ordinal)) return;

        atExit.Severity = Severity.Critical;
        atExit.Confidence = Math.Min(1.0, atExit.Confidence * 1.15);
        atExit.Description = $"Two independent checks agree on {atExit.SuspectSite}: it grew monotonically " +
                             "for the whole run, and it was still holding memory when the program exited. " +
                             "Neither signal depends on the other, which makes this the strongest leak " +
                             "evidence the analysis can produce. " + atExit.Description;
        atExit.Evidence.Add("Confirmed by LEAK: the same site also grew monotonically across the run");
    }
}
