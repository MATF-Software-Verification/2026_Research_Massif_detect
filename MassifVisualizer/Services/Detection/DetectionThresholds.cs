namespace MassifVisualizer.Services.Detection;

public class DetectionThresholds
{
    public int MinSnapshots { get; init; } = 10;
    public double LeakMonotonicity { get; init; } = 0.90;
    public double LeakMaxDrawdown { get; init; } = 0.10;
    public double LeakFinalRetention { get; init; } = 0.80;
    public double LeakSiteMonotonicity { get; init; } = 0.90;

    public double AtExitMinFinalShare { get; init; } = 0.05;
    public double AtExitMaxTreeDrift { get; init; } = 0.10;
    public double AtExitMinSiteShare { get; init; } = 0.05;

    public double FragMedianRatio { get; init; } = 0.30;
    public double FragOverheadSlope { get; init; } = 0.50;

    public double SpikeRelativeJump { get; init; } = 0.50;
    public double SpikeAbsoluteJump { get; init; } = 0.10;
    public double SpikeRecoveryWindow { get; init; } = 0.20;
    public double SpikeRecoveryBand { get; init; } = 0.10;
    public double SpikeRecoveryFraction { get; init; } = 0.25;
    public double SpikeMinAttributionShare { get; init; } = 0.50;
    public double SpikeMinAttributionCoverage { get; init; } = 0.50;
    public double SpikeMaxAttributionCoverage { get; init; } = 1.50;
    public int SpikeContributorsListed { get; init; } = 3;

    public static DetectionThresholds Default { get; } = new();
}
