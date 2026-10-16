namespace Mortz.E2E.Tests.Harness;

/// <summary>The server's Debug build, the only one with the E2E control channel.</summary>
public static class ServerExecutable
{
    public static string Resolve()
    {
        string path = Path.Combine(RepoRoot.Path, "Mortz.Server", "bin", "Debug", "net10.0",
            OperatingSystem.IsWindows() ? "MortzServer.exe" : "MortzServer");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"Server build not found: {path}. Build Mortz.Server in Debug.", path);
    }
}
