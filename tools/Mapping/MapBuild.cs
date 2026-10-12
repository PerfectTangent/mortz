using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Mortz.Tools.Mapping;

public static partial class MapBuild
{
    private static readonly string[] _pluginPath = ["tools", "Mapping", "Krita"];

    public static int Run(string root, string? target)
    {
        string executable = Environment.GetEnvironmentVariable("KRITARUNNER") ?? "kritarunner";
        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        string plugin = Path.Combine([root, .. _pluginPath]);
        string? pythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
        startInfo.Environment["PYTHONPATH"] = string.IsNullOrEmpty(pythonPath)
            ? plugin
            : plugin + Path.PathSeparator + pythonPath;
        // Without it kritarunner loses Python's stdout.
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        foreach (string arg in (string[])["-s", "mortz.build", root, .. target == null ? [] : (string[])[target]])
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)
                                ?? throw new Exception($"failed to start {executable} (set KRITARUNNER to its path)");
        // Drained so Krita never blocks on a full pipe; its stderr is Qt noise.
        process.BeginErrorReadLine();
        Match? summary = null;
        while (process.StandardOutput.ReadLine() is string line)
        {
            Console.WriteLine(line);
            summary = Summary().Match(line) is { Success: true } match ? match : summary;
        }
        process.WaitForExit();

        // kritarunner sometimes aborts on shutdown after the work is done, so its exit code
        // means nothing. The summary line is the result.
        if (summary == null)
            throw new Exception("kritarunner stopped before the build finished");
        return summary.Groups[2].Value == "0" ? 0 : 1;
    }

    [GeneratedRegex(@"^mortz-build: (\d+) built, (\d+) failed$")]
    private static partial Regex Summary();
}
