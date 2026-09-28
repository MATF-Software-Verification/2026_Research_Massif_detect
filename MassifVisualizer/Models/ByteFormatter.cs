namespace MassifVisualizer.Models;

internal static class ByteFormatter
{
    internal static string Format(long b) => b switch
    {
        >= 1_048_576 => $"{b / 1_048_576.0:F2} MB",
        >= 1_024     => $"{b / 1_024.0:F1} KB",
        _            => $"{b} B"
    };
}
