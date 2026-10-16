using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

// The trimmer misses reflection it can't see, so boot the real trimmed publish.
public sealed class TrimmedServerTests
{
    private static readonly TimeSpan _bootTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ATrimmedReleaseServerBootsWithAllContentAndARuleset()
    {
        string root = Directory.CreateTempSubdirectory("mortz-trimmed-").FullName;
        try
        {
            string publish = Path.Combine(root, "publish");
            Publish(publish);
            string work = Path.Combine(root, "work");
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "server.toml"), "name = \"Trimmed\"\n");

            int queryPort = FreeUdpPort();
            string executable = Path.Combine(publish, OperatingSystem.IsWindows() ? "MortzServer.exe" : "MortzServer");
            ProcessStartInfo info = new(executable)
            {
                WorkingDirectory = work,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in new[]
                     {
                         "--content-root", Path.Combine(RepoRoot.Path, "content"),
                         "--port", "0",
                         "--query-port", queryPort.ToString(),
                         "--ruleset", Path.Combine(RepoRoot.Path, "Mortz.E2E.Tests", "Rulesets", "endless_deathmatch.toml"),
                     })
            {
                info.ArgumentList.Add(argument);
            }

            StringBuilder output = new();
            using Process server = Process.Start(info)
                                   ?? throw new E2EProcessException("Could not start the trimmed server.");
            server.OutputDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
            server.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
            server.BeginOutputReadLine();
            server.BeginErrorReadLine();
            try
            {
                bool answered = await AnswersInfoQueryAsync(server, queryPort);
                string log;
                lock (output) log = output.ToString();
                Assert.True(answered, $"the trimmed server never answered A2S_INFO:{Environment.NewLine}{log}");
            }
            finally
            {
                if (!server.HasExited)
                    server.Kill(entireProcessTree: true);
                await server.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Publish(string output)
    {
        string runtime = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        ProcessStartInfo info = new("dotnet")
        {
            WorkingDirectory = RepoRoot.Path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[]
                 {
                     "publish", "Mortz.Server/Mortz.Server.csproj", "-c", "Release", "-r", runtime,
                     "--self-contained", $"-p:GnsRuntime={runtime}", "-o", output, "--nologo", "-v:quiet",
                 })
        {
            info.ArgumentList.Add(argument);
        }

        using Process publish = Process.Start(info)
                                ?? throw new E2EProcessException("Could not start dotnet publish.");
        string log = publish.StandardOutput.ReadToEnd() + publish.StandardError.ReadToEnd();
        publish.WaitForExit();
        Assert.True(publish.ExitCode == 0, $"dotnet publish failed:{Environment.NewLine}{log}");
    }

    // The query port only opens once the whole boot succeeded, so an info reply means every
    // load before it worked.
    private static async Task<bool> AnswersInfoQueryAsync(Process server, int queryPort)
    {
        byte[] request = [0xFF, 0xFF, 0xFF, 0xFF, (byte)'T', .. "Source Engine Query\0"u8];
        IPEndPoint endpoint = new(IPAddress.Loopback, queryPort);
        using UdpClient client = new(AddressFamily.InterNetwork);
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < _bootTimeout && !server.HasExited)
        {
            byte[]? reply = await QueryAsync(client, endpoint, request);
            // A challenge (0x41) means the responder is up, so repeat with the challenge appended.
            if (reply is { Length: >= 9 } && reply[4] == 0x41)
                reply = await QueryAsync(client, endpoint, [.. request, .. reply.AsSpan(5, 4)]);
            if (reply is { Length: > 4 } && reply[4] == 0x49)
                return true;
            await Task.Delay(250);
        }
        return false;
    }

    private static async Task<byte[]?> QueryAsync(UdpClient client, IPEndPoint endpoint, byte[] request)
    {
        try
        {
            await client.SendAsync(request, endpoint);
            using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(500));
            UdpReceiveResult result = await client.ReceiveAsync(timeout.Token);
            return result.Buffer;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException)
        {
            return null;
        }
    }

    private static int FreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }
}
