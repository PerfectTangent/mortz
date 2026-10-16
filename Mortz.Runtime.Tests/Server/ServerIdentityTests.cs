using Mortz.Protocol.Net.Query;
using Mortz.Server.Hosting;
using Serilog.Core;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public sealed class ServerIdentityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mortz-identity-" + Guid.NewGuid().ToString("N"));
    private string IdPath => Path.Combine(_directory, "data", "server-id");

    [Fact]
    public void TheIdIsCreatedOnceAndKeptAcrossRestarts()
    {
        string first = ServerIdentity.Load(IdPath, Logger.None);
        string second = ServerIdentity.Load(IdPath, Logger.None);

        Assert.True(ServerQueryMetadata.IsServerId(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public void AnUnreadableIdIsReplacedAndThenKept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IdPath)!);
        File.WriteAllText(IdPath, "garbage");

        string replaced = ServerIdentity.Load(IdPath, Logger.None);

        Assert.True(ServerQueryMetadata.IsServerId(replaced));
        Assert.Equal(replaced, ServerIdentity.Load(IdPath, Logger.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
