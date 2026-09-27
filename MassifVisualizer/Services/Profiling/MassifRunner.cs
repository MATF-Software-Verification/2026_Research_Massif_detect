using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MassifVisualizer.Services.Profiling;

public static class MassifRunner
{
    public static Task<ToolResult> RunAsync(string binaryPath, string outputPath, MassifOptions options,
                                            CancellationToken token = default)
    {
        options.Validate();

        string[] args =
        [
            "--tool=massif",
            $"--massif-out-file={outputPath}",
            $"--time-unit={options.TimeUnit}",
            $"--detailed-freq={options.DetailedFrequency.ToString(CultureInfo.InvariantCulture)}",
            $"--max-snapshots={options.MaximumSnapshots.ToString(CultureInfo.InvariantCulture)}",
            $"--threshold={options.SignificanceThreshold.ToString("0.0################", CultureInfo.InvariantCulture)}",
            "--quiet",
            binaryPath
        ];

        return ExternalTool.RunAsync("valgrind", args, Path.GetDirectoryName(binaryPath) ?? ".", token);
    }
}
