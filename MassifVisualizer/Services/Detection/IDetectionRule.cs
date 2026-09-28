using System.Collections.Generic;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Detection;

public interface IDetectionRule
{
    string Id { get; }
    IEnumerable<Finding> Analyze(AnalysisContext ctx);
}
