using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MassifVisualizer.Services.Profiling;

public static class CompilerRunner
{
    private static readonly string[] Flags = ["-O0", "-g", "-Wall"];

    public static Task<ToolResult> CompileAsync(string sourcePath, string binaryPath,
                                                CancellationToken token = default)
    {
        var args = new List<string>(Flags) { "-o", binaryPath, sourcePath };
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? ".";

        return ExternalTool.RunAsync("gcc", args, workingDirectory, token);
    }
}
