namespace MassifVisualizer.Services.Detection;

public class DetectionThresholds
{
    public int MinSnapshots { get; init; } = 10;
    public double LeakMonotonicity { get; init; } = 0.90;
    public double LeakMaxDrawdown { get; init; } = 0.10;
    public double LeakFinalRetention { get; init; } = 0.80;
    public double LeakSiteMonotonicity { get; init; } = 0.90;

    public static DetectionThresholds Default { get; } = new();
}
