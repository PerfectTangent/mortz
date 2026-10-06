using Mortz.Protocol.Net;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class ProtocolContractTests
{
    [Fact]
    public void ObjectiveReplicationProtocolIsVersion52()
    {
        Assert.Equal(52, NetConfig.PROTOCOL_VERSION);
    }

    [Fact]
    public void GeneratedMessageSchemaMatchesVersion52()
    {
        Assert.Equal(0x9012E2A3C930B4EEUL, NetRegistry.SCHEMA_HASH);
    }
}
