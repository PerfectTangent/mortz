using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// Kills leftovers of provably dead runs only: our marker, a different run id, one of our
/// binaries, and a run lock nobody holds. A concurrent run holds its lock, so it's left alone.
/// </summary>
public static class StaleProcessSweep
{
    public const string MARKER = "--e2e-run";

    public static IReadOnlyList<string> Run(string currentRunId, IReadOnlyList<string> executables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentRunId);
        if (!OperatingSystem.IsWindows())
            return ["sweep skipped: not Windows"];
        return SweepWindows(currentRunId, executables);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> SweepWindows(string currentRunId, IReadOnlyList<string> executables)
    {
        List<string> actions = [];
        string[] ours = [.. executables.Select(Path.GetFullPath)];
        using ManagementObjectSearcher searcher = new(
            "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process " +
            $"WHERE CommandLine LIKE '%{MARKER}%'");
        foreach (ManagementBaseObject row in searcher.Get())
        {
            using ManagementBaseObject candidate = row;
            string? commandLine = candidate["CommandLine"] as string;
            string? executable = candidate["ExecutablePath"] as string;
            if (commandLine == null || executable == null)
                continue;
            if (!ours.Contains(Path.GetFullPath(executable), StringComparer.OrdinalIgnoreCase))
                continue;
            if (RunIdOf(commandLine) is not string runId || runId == currentRunId)
                continue;
            if (RunLock.IsLive(runId))
            {
                actions.Add($"left run {runId} alone: its lock is held");
                continue;
            }
            actions.Add(Kill(Convert.ToInt32(candidate["ProcessId"]), runId));
            RunLock.Forget(runId);
        }
        return actions;
    }

    private static string Kill(int processId, string runId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            return $"killed stale pid {processId} from dead run {runId}";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                Win32Exception)
        {
            return $"could not kill stale pid {processId} from dead run {runId}: {exception.Message}";
        }
    }

    /// <summary>The token after the marker, or null when the command line does
    /// not actually carry one.</summary>
    public static string? RunIdOf(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        string[] tokens = commandLine.Split(
            [' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Trim('"') == MARKER)
                return tokens[i + 1].Trim('"');
        }
        return null;
    }
}
