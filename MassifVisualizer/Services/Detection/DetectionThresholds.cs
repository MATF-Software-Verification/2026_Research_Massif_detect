namespace MassifVisualizer.Services.Detection;

public class DetectionThresholds
{
    public int MinSnapshots { get; init; } = 10;
    public double LeakMonotonicity { get; init; } = 0.90;
    public double LeakMinGrowthShare { get; init; } = 0.20;
    public double LeakFinalRetention { get; init; } = 0.80;
    public double LeakMinSiteGrowthShare { get; init; } = 0.10;
    public double LeakAttributionEdgeWindow { get; init; } = 0.25;

    public double AtExitMinFinalShare { get; init; } = 0.10;
    public double AtExitMinSiteShare { get; init; } = 0.10;

    public double FragMedianRatio { get; init; } = 0.30;

    public double SpikeRelativeJump { get; init; } = 0.50;
    public double SpikeAbsoluteJump { get; init; } = 0.10;
    public double SpikeRecoveryWindow { get; init; } = 0.20;
    public double SpikeRecoveryFraction { get; init; } = 0.25;
    public double SpikeMinAttributionShare { get; init; } = 0.50;

    public static DetectionThresholds Default { get; } = new();
}
