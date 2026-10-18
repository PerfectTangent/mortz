using System.Diagnostics;
using Godot;
using Mortz.Client.Content;

namespace Mortz.Client.Session;

/// <summary>Starts the dedicated server for a player-hosted game: the Debug build in the editor,
/// the bundled <c>server/</c> publish in an export.</summary>
public static class ServerLauncher
{
    public static ProcessStartInfo CreateStartInfo(int port, string adminPassword, string serverName,
        bool allowJoinInProgress = true)
    {
        bool editor = OS.HasFeature("editor");
        string executableName = OS.HasFeature("windows") ? "MortzServer.exe" : "MortzServer";
        string directory = editor
            ? ProjectSettings.GlobalizePath("res://")
            : Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "server");
        string executable = editor
            ? Path.Combine(directory, "Mortz.Server", "bin", "Debug", "net10.0", executableName)
            : Path.Combine(directory, executableName);
        ProcessStartInfo start = new(executable) { WorkingDirectory = directory, UseShellExecute = false };
        if (OS.HasFeature("linux"))
        {
            // A Steam server finds the steamclient.so shipped next to it only through this.
            string serverDirectory = Path.GetDirectoryName(executable)!;
            string? inherited = start.Environment.TryGetValue("LD_LIBRARY_PATH", out string? value) ? value : null;
            start.Environment["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(inherited)
                ? serverDirectory
                : $"{serverDirectory}:{inherited}";
        }
        List<string> args =
        [
            "--port", port.ToString(),
            "--content-root", ContentRoot.Resolve(),
            "--admin-password", adminPassword,
        ];
        if (serverName.Length > 0)
        {
            args.AddRange(["--server-name", serverName]);
        }
        args.Add(allowJoinInProgress ? "--allow-jip" : "--no-jip");
        foreach (string argument in args)
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
