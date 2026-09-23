using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MassifVisualizer.Services.Profiling;

public static class MassifRunner
{
    public static Task<ToolResult> RunAsync(string binaryPath, string outputPath,
                                            CancellationToken token = default)
    {
        string[] args =
        [
            "--tool=massif",
            $"--massif-out-file={outputPath}",
			// Without this we only get a call tree every tenth snapshot
            "--detailed-freq=5",
            "--quiet",
            binaryPath
        ];

        return ExternalTool.RunAsync("valgrind", args, Path.GetDirectoryName(binaryPath) ?? ".", token);
    }
}
