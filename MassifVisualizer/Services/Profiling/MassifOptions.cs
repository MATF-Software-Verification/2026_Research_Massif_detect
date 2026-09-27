using System;

namespace MassifVisualizer.Services.Profiling;

public sealed record MassifOptions(
    string TimeUnit = "i",
    int DetailedFrequency = 5,
    int MaximumSnapshots = 100,
    double SignificanceThreshold = 1.0)
{
    public static MassifOptions Default { get; } = new();

    public void Validate()
    {
        if (TimeUnit is not ("i" or "ms" or "B"))
            throw new ArgumentOutOfRangeException(nameof(TimeUnit), "Time unit must be i, ms, or B.");
        if (DetailedFrequency < 1)
            throw new ArgumentOutOfRangeException(nameof(DetailedFrequency), "Detailed frequency must be at least 1.");
        if (MaximumSnapshots < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumSnapshots), "Maximum snapshots must be at least 1.");
        if (!double.IsFinite(SignificanceThreshold) || SignificanceThreshold is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(SignificanceThreshold), "Significance threshold must be between 0 and 100.");
    }
}
