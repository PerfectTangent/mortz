using Mortz.Protocol.Net;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class ProtocolContractTests
{
    [Fact]
    public void SingleMatchResultProtocolIsVersion53()
    {
        Assert.Equal(53, NetConfig.PROTOCOL_VERSION);
    }

    [Fact]
    public void GeneratedMessageSchemaMatchesVersion53()
    {
        Assert.Equal(0xC10BF8FC829C2426UL, NetRegistry.SCHEMA_HASH);
    }
}
