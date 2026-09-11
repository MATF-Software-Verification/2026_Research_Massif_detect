using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services.Profiling;

/// ExitCode is the profiled program's own, so it is null when compilation failed and it never ran
public sealed record ProfileResult(MassifProfile? Profile, string SourcePath, string Output, int? ExitCode)
{
    public bool Ok => Profile != null;
}

public static class SourceProfiler
{
    public static async Task<ProfileResult> RunAsync(string sourcePath, CancellationToken token = default)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        var work = Directory.CreateTempSubdirectory("massifdetect");

        try
        {
            var binary = Path.Combine(work.FullName, Path.GetFileNameWithoutExtension(sourcePath));
            var build = await CompilerRunner.CompileAsync(sourcePath, binary, token);
            if (!build.Ok)
                return new ProfileResult(null, sourcePath, build.Output, null);

            var massifOut = Path.Combine(work.FullName, "massif.out");
            var run = await MassifRunner.RunAsync(binary, massifOut, token);

            // Valgrind passes the program's own exit code through, so a program that returns
            // non-zero is not a failure on our side. What matters is whether we got a file
            if (!File.Exists(massifOut))
                return new ProfileResult(null, sourcePath, run.Output, run.ExitCode);

            var profile = MassifParser.Parse(massifOut);

            // Otherwise this would be a path inside the temp folder
            profile.Command = Path.GetFileName(sourcePath);

            return new ProfileResult(profile, sourcePath, build.Output, run.ExitCode);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }
}
