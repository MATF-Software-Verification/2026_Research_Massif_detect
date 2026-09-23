using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MassifVisualizer.Services.Profiling;

public sealed record ToolResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

public static class ExternalTool
{
    /// gcc and Valgrind print their messages to stderr, so both streams go into one Output
    public static async Task<ToolResult> RunAsync(string fileName, IEnumerable<string> args,
                                                  string workingDirectory, CancellationToken token)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = info };

        try
        {
            process.Start();
        }
        catch (SystemException ex)
        {
            return new ToolResult(-1, $"{fileName}: {ex.Message}");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);

        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new ToolResult(process.ExitCode, (await stdout + await stderr).Trim());
    }
}
