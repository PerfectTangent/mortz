using System.Diagnostics;
using System.Text;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

/// <summary>
/// A clean compile doesn't prove the E2E code was stripped, so this builds the real
/// configurations and greps the assembly bytes for the type names.
/// </summary>
public sealed class ExportStripTests
{
    /// <summary>Every type that carries the control channel or its composition.</summary>
    private static readonly string[] _strippedTypes =
    [
        "E2EDriver",
        "E2EDispatcher",
        "E2ELaunch",
        "IE2EHandler",
        "IE2EResponder",
        "ClientE2EHandler",
        "ClientE2ERoot",
        "IE2EClientBridge",
        "NullE2EClientBridge",
        "E2EMatchProbe",
        "BotInputScript",
    ];

    [Theory]
    [InlineData("ExportRelease")]
    [InlineData("ExportDebug")]
    public void AnExportedAssemblyContainsNoE2ECode(string configuration)
    {
        string output = Build(configuration);
        string assembly = Path.Combine(output, "Mortz.dll");
        Assert.True(File.Exists(assembly), $"{assembly} was not produced");

        string metadata = Encoding.ASCII.GetString(File.ReadAllBytes(assembly));
        string[] survivors = _strippedTypes
            .Where(type => metadata.Contains(type, StringComparison.Ordinal))
            .ToArray();

        Assert.True(survivors.Length == 0,
            $"{configuration} still contains E2E types: {string.Join(", ", survivors)}");
    }

    [Theory]
    [InlineData("ExportRelease")]
    [InlineData("ExportDebug")]
    public void AnExportedBuildDoesNotShipTheProtocolAssembly(string configuration)
    {
        string output = Build(configuration);

        Assert.False(File.Exists(Path.Combine(output, "Mortz.E2E.Protocol.dll")),
            $"{configuration} shipped Mortz.E2E.Protocol.dll");
    }

    [Fact]
    public void TheEditorBuildStillContainsThemSoTheCheckCanFail()
    {
        // Without this, a typo in a type name would make every assertion above
        // pass for the wrong reason.
        string assembly = Path.Combine(
            RepoRoot.Path, ".godot", "mono", "temp", "bin", "Debug", "Mortz.dll");
        Assert.True(File.Exists(assembly), $"{assembly} was not produced");

        string metadata = Encoding.ASCII.GetString(File.ReadAllBytes(assembly));
        string[] missing = _strippedTypes
            .Where(type => !metadata.Contains(type, StringComparison.Ordinal))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"the Debug build is missing types this test claims to look for: " +
            $"{string.Join(", ", missing)}");
    }

    /// <summary>The same check for the dedicated server, where MORTZ_E2E is Debug only.</summary>
    private static readonly string[] _serverStrippedTypes =
    [
        "E2EDispatcher",
        "ServerE2EHandler",
        "ServerE2EEvents",
        "E2EMatchControl",
    ];

    [Fact]
    public void AReleaseServerContainsNoE2ECode()
    {
        string output = BuildServer("Release");
        string metadata = Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(output, "MortzServer.dll")));

        Assert.DoesNotContain(_serverStrippedTypes, type => metadata.Contains(type, StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(output, "Mortz.E2E.Protocol.dll")));
    }

    [Fact]
    public void TheDebugServerStillContainsThemSoTheCheckCanFail()
    {
        string output = BuildServer("Debug");
        string metadata = Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(output, "MortzServer.dll")));

        Assert.All(_serverStrippedTypes, type => Assert.Contains(type, metadata, StringComparison.Ordinal));
    }

    private static string BuildServer(string configuration)
    {
        Run("build", "Mortz.Server/Mortz.Server.csproj", "-c", configuration, "--nologo", "-v:quiet");
        return Path.Combine(RepoRoot.Path, "Mortz.Server", "bin", configuration, "net10.0");
    }

    private static string Build(string configuration)
    {
        Run("build", "Mortz.csproj", "-c", configuration, "--nologo", "-v:quiet");
        return Path.Combine(RepoRoot.Path, ".godot", "mono", "temp", "bin", configuration);
    }

    private static void Run(params string[] arguments)
    {
        ProcessStartInfo info = new("dotnet")
        {
            WorkingDirectory = RepoRoot.Path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process build = Process.Start(info)
            ?? throw new E2EProcessException("Could not start dotnet build.");
        string output = build.StandardOutput.ReadToEnd() + build.StandardError.ReadToEnd();
        build.WaitForExit();
        Assert.True(build.ExitCode == 0, $"{string.Join(' ', arguments)} failed:{Environment.NewLine}{output}");
    }
}
