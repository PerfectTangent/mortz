using Mortz.Protocol.Net;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class ProtocolContractTests
{
    [Fact]
    public void LobbyWaitingProtocolIsVersion49()
    {
        Assert.Equal(49, NetConfig.PROTOCOL_VERSION);
    }

    [Fact]
    public void GeneratedMessageSchemaMatchesVersion49()
    {
        Assert.Equal(0x8A62377FE1AF70D7UL, NetRegistry.SCHEMA_HASH);
    }
}
