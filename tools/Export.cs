using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Mortz.Tools;

public static class Export
{
    public static void Run(string[] args)
    {
        string selection = "all";
        bool debug = false;
        bool requireOfficial = false;
        bool? steam = null;
        bool? linux = null;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "client" or "server" or "all": selection = arg; break;
                case "--debug": debug = true; break;
                case "--require-official": requireOfficial = true; break;
                case "--linux" when linux != false: linux = true; break;
                case "--windows" when linux != true: linux = false; break;
                case "--steam" when steam != false: steam = true; break;
                case "--standalone" when steam != true: steam = false; break;
                default: throw new Exception($"unexpected or conflicting argument '{arg}'");
            }
        }

        string root = Program.RepoRoot();
        OfficialOverlay.Validate(root, requireOfficial);
        string godot = ResolveGodot(root);
        string flavor = steam == true ? "steam" : "standalone";
        string[] platforms = linux switch
        {
            true => ["linux"],
            false => ["windows"],
            null => ["windows", "linux"]
        };

        foreach (string platform in platforms)
        {
            // A client always carries a complete, independently runnable server.
            PublishServer(root, flavor, platform, debug);
            if (selection == "server")
                continue;

            ExportRole(root, godot, flavor, platform, "client", debug);
            string client = PackageDirectory(root, flavor, platform, "client");
            CopyTree(PackageDirectory(root, flavor, platform, "server"), Path.Combine(client, "server"),
                relative => relative != "content"); // remove content/ from the bundled server executable
            if (flavor == "standalone")
                AssertStandalone(client);
        }
        Console.WriteLine("done");
        if (OperatingSystem.IsWindows() && linux != false)
            Console.WriteLine("note: chmod +x the .x86_64 executables after copying to Linux");
    }

    public static string PackageDirectory(string root, string flavor, string platform, string role) =>
        Path.Combine(root, "build", flavor, platform, role);

    private static void PublishServer(string root, string flavor, string platform, bool debug)
    {
        string outDir = PackageDirectory(root, flavor, platform, "server");
        CleanBuildDir(outDir);
        string runtime = platform == "linux" ? "linux-x64" : "win-x64";
        Console.WriteLine($"==> publishing {flavor}/{platform}/server");
        // GnsRuntime picks the GNS binary for the target, not for the machine building it.
        RunProcess("dotnet", ["publish", Path.Combine(root, "Mortz.Server", "Mortz.Server.csproj"),
            "-c", debug ? "Debug" : "Release", "-r", runtime, "--self-contained",
            $"-p:MortzFlavor={(flavor == "steam" ? "Steam" : "Standalone")}", $"-p:GnsRuntime={runtime}",
            "-o", outDir], root);
        string exePath = Path.Combine(outDir, platform == "windows" ? "MortzServer.exe" : "MortzServer");
        if (!File.Exists(exePath))
            throw new Exception($"publish did not produce {exePath}");
        MirrorContent(Path.Combine(root, "content"), Path.Combine(outDir, "content"));
        if (flavor == "steam")
        {
            File.WriteAllText(Path.Combine(outDir, "steam_appid.txt"), "5016960\n");
            if (platform == "linux")
                WriteSteamLauncher(outDir);
        }
        if (flavor == "standalone")
            AssertStandalone(outDir);
        else if (!File.Exists(Path.Combine(outDir, platform == "linux" ? "libsteam_api.so" : "steam_api64.dll")))
            throw new Exception($"Steam server package has no Steam API library: {outDir}");
    }

    /// <summary>Writes a launcher that puts the package on LD_LIBRARY_PATH, so libsteam_api finds
    /// the bundled steamclient.so.</summary>
    private static void WriteSteamLauncher(string outDir)
    {
        string launcher = Path.Combine(outDir, "mortz-server.sh");
        File.WriteAllText(launcher, """
            #!/bin/sh
            cd "$(dirname "$0")" || exit 1
            LD_LIBRARY_PATH="$PWD${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" exec ./MortzServer "$@"

            """);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(launcher, File.GetUnixFileMode(launcher) |
                                           UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static void ExportRole(string root, string godot, string flavor, string platform, string role, bool debug)
    {
        string stage = Path.Combine(root, "build", ".staging", flavor, platform, role);
        ExportStage.Synchronize(root, stage, relative => IncludeInStage(relative, flavor));
        string props = $"""
            <Project>
              <PropertyGroup>
                <MortzFlavor>{(flavor == "steam" ? "Steam" : "Standalone")}</MortzFlavor>
              </PropertyGroup>
            </Project>
            """;
        string propsPath = Path.Combine(stage, "Mortz.Export.props");
        if (!File.Exists(propsPath) || File.ReadAllText(propsPath) != props)
        {
            File.WriteAllText(propsPath, props);
        }
        if (flavor == "standalone")
            AssertStandalone(stage);
        PrepareProject(godot, stage, role);

        string outDir = PackageDirectory(root, flavor, platform, role);
        CleanBuildDir(outDir);
        string exePath = Path.Combine(outDir, "Mortz" + (platform == "windows" ? ".exe" : ".x86_64"));
        string preset = $"Mortz Client{(platform == "linux" ? " Linux" : "")}";
        Console.WriteLine($"==> exporting {flavor}/{platform}/{role}");
        // for some reason, exported godot games can lose [Export] Type[] variables and everything breaks. This fixes it by
        // doing a build before exporting the project
        RunProcess("dotnet", ["build", Path.Combine(stage, "Mortz.csproj"), "-c", "Debug"], stage);
        string exportCache = Path.Combine(stage, ".godot", "exported");
        if (Directory.Exists(exportCache))
            Directory.Delete(exportCache, recursive: true);
        RunProcess(godot, ["--headless", "--path", stage, "--editor", "--import"], stage);
        RunProcess(godot, ["--headless", "--path", stage, debug ? "--export-debug" : "--export-release", preset, exePath], stage);
        if (!File.Exists(exePath))
            throw new Exception($"export did not produce {exePath}");
        // Godot can emit an executable and exit 0 after its .NET publish failed.
        if (!Directory.EnumerateFiles(outDir, "Mortz.dll", SearchOption.AllDirectories).Any())
            throw new Exception($"export has no Mortz.dll managed payload: {outDir}; check Godot's MSBuild log");
        if (platform == "linux" && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(exePath, File.GetUnixFileMode(exePath) |
                                         UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        MirrorContent(Path.Combine(root, "content"), Path.Combine(outDir, "content"));
        if (flavor == "standalone")
            AssertStandalone(outDir);
        else
            AssertSteamClient(outDir);
    }

    private static bool IncludeInStage(string relative, string flavor)
    {
        string path = relative.Replace('\\', '/');
        string[] parts = path.Split('/');
        if (parts.Any(part => part is "bin" or "obj" || (part.StartsWith('.') && part != ".gdignore")))
            return false;
        if (parts[0] is "build" or "tools" or "plans" or "docs" || parts[0].EndsWith(".Tests"))
            return false;
        if (path is "AGENTS.md" or "Mortz.Export.props" or "server.json" or "export_credentials.cfg" ||
            path.EndsWith(".user") || path.StartsWith("official/source/"))
            return false;
        if (path == "official/source")
            return false;
        if (flavor == "standalone" && (path == "src/Platform/Steam" || path.StartsWith("src/Platform/Steam/")))
            return false;
        // The server's Steam libraries, the client gets its own through GodotSteam.
        if (path == "native/steam" || path.StartsWith("native/steam/"))
            return false;
        if (parts[0] != "addons" || parts.Length < 2)
            return true;

        string addon = parts[1];
        if (addon.StartsWith("godotsteam") || addon.StartsWith("steam-"))
        {
            if (flavor == "standalone")
                return false;
            if (addon == "godotsteam_csharpbindings")
                return true;
            return addon == "godotsteam";
        }
        return addon != "STEAM.md";
    }

    private static void PrepareProject(string godot, string stage, string role)
    {
        string temporary = Directory.CreateTempSubdirectory("mortz-export-").FullName;
        try
        {
            string script = Path.Combine(temporary, "prepare.gd");
            File.WriteAllText(script, """
                extends SceneTree

                func _initialize():
                    var path = OS.get_cmdline_user_args()[0]
                    var config = ConfigFile.new()
                    if config.load(path) != OK:
                        quit(1)
                        return
                    config.set_value("editor_plugins", "enabled", PackedStringArray())
                    if OS.get_cmdline_user_args()[1] == "client":
                        config.set_value("steam", "initialization/app_data/app_id", 0)
                    quit(0 if config.save(path) == OK else 1)
                """);
            // Editor plugins are unrelated to exports and can race threaded font imports.
            RunProcess(godot, ["--headless", "--path", temporary, "--script", script, "--",
                Path.Combine(stage, "project.godot"), role], temporary);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void CopyTree(string source, string dest, Func<string, bool>? include = null, string relative = "")
    {
        Directory.CreateDirectory(dest);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            string name = Path.GetFileName(entry);
            string path = Path.Combine(relative, name);
            if (include is not null && !include(path))
                continue;
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new Exception($"export input must not be a symbolic link: {entry}");
            string target = Path.Combine(dest, name);
            if (Directory.Exists(entry))
                CopyTree(entry, target, include, path);
            else
                File.Copy(entry, target, overwrite: true);
        }
    }

    private static void AssertStandalone(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (relative.StartsWith("src/Platform/Steam/") || relative.StartsWith("addons/godotsteam") ||
                name.Contains("godotsteam", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("steam_api", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("libsteam", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("steamclient", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("steamwebrtc", StringComparison.OrdinalIgnoreCase) ||
                name is "steam_appid.txt" or "tier0_s64.dll" or "vstdlib_s64.dll")
                throw new Exception($"Steam dependency in standalone output: {file}");
            if (name != "Mortz.dll")
                continue;
            using FileStream stream = File.OpenRead(file);
            using PEReader assembly = new PEReader(stream);
            MetadataReader metadata = assembly.GetMetadataReader();
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle);
                string typeNamespace = metadata.GetString(type.Namespace);
                if (typeNamespace.StartsWith("Mortz.Platform.Steam") || typeNamespace == "GDExtension.Wrappers")
                    throw new Exception($"Steam implementation compiled into standalone assembly: {file}");
            }
        }
    }

    private static void AssertSteamClient(string directory)
    {
        int nativeAddons = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Count(file => Path.GetFileName(file).StartsWith("libgodotsteam") &&
                           Path.GetExtension(file) is ".dll" or ".so");
        if (nativeAddons != 1)
            throw new Exception($"expected exactly one GodotSteam client addon in {directory}");
    }

    // A mismatched editor rewrites Mortz.csproj and exits 0.
    private static string ResolveGodot(string root)
    {
        string csproj = Path.Combine(root, "Mortz.csproj");
        Match match = Regex.Match(File.ReadAllText(csproj), @"Sdk=""Godot\.NET\.Sdk/([\d.]+)""");
        if (!match.Success)
            throw new Exception($"no Godot.NET.Sdk version found in {csproj}");
        string want = match.Groups[1].Value;

        string godot = Environment.GetEnvironmentVariable("GODOT_PATH") ?? FindGodot(want);
        if (!File.Exists(godot))
            throw new Exception($"Godot not found at {godot} (set GODOT_PATH to override)");

        string reported = RunCapture(godot, ["--version"]).Trim();
        if (NormalizeVersion(reported) != NormalizeVersion(want))
            throw new Exception($"{godot} reports {reported}, but Mortz.csproj builds against {want}");
        return godot;
    }

    private static string FindGodot(string version)
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("GODOT_ROOT") is null)
            return ToolPath.Resolve("GODOT_PATH", "godot");
        string installs = Environment.GetEnvironmentVariable("GODOT_ROOT") ?? @"E:\filax\Godot";
        string dir = Path.Combine(installs, version);
        if (!Directory.Exists(dir))
            throw new Exception($"no Godot {version} install under {installs} (set GODOT_PATH to override)");
        string[] hits = Directory.GetFiles(dir, OperatingSystem.IsWindows() ? "*_console.exe" : "*.x86_64");
        if (hits.Length != 1)
            throw new Exception($"expected one Godot executable in {dir}, found {hits.Length}");
        return hits[0];
    }

    // Godot reports 4.7.0 as "4.7.stable.mono".
    private static string NormalizeVersion(string version)
    {
        List<string> parts = Regex.Match(version, @"^\d+(\.\d+)*").Value.Split('.').ToList();
        while (parts.Count < 3)
        {
            parts.Add("0");
        }
        return string.Join('.', parts);
    }

    // The game reads raw content files, not Godot's .import metadata.
    private static void MirrorContent(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        HashSet<string> wanted = new HashSet<string>();
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file) == ".import")
                continue;
            string rel = Path.GetRelativePath(source, file);
            wanted.Add(rel);
            string target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        foreach (string file in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
        {
            if (!wanted.Contains(Path.GetRelativePath(dest, file)))
                File.Delete(file);
        }
        // Deepest first, so emptied parents empty out too.
        foreach (string subDir in Directory.EnumerateDirectories(dest, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                Directory.Delete(subDir);
        }
    }

    private static string RunCapture(string exe, string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        using Process proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(exe)} {string.Join(' ', args)} failed (exit {proc.ExitCode})");
        return output;
    }

    // A running build is reported here before Godot hangs on its final rename.
    private static void CleanBuildDir(string buildDir)
    {
        Directory.CreateDirectory(buildDir);
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(buildDir))
            {
                Directory.Delete(dir, recursive: true);
            }
            foreach (string file in Directory.EnumerateFiles(buildDir))
            {
                if (Path.GetFileName(file) != ".gdignore")
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new Exception($"cleaning {buildDir} failed ({e.Message.Trim()}), close any running build and retry");
        }
        Console.WriteLine($"==> cleaned {buildDir}");
    }

    private static void RunProcess(string exe, string[] args, string workingDirectory)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = workingDirectory };
        // Build servers keep Godot's stdout pipe open after the export finishes.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        psi.Environment["UseSharedCompilation"] = "false";
        psi.Environment["HUSKY"] = "0";
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        using Process proc = Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(exe)} failed (exit {proc.ExitCode})");
    }
}
