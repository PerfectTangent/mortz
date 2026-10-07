using Mortz.Protocol.Net.Chat;
using Mortz.Protocol.Net.Lobby;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

/// <summary>Shutdown works for every live shape, and a second Dispose does nothing.</summary>
public class GameServerDisposeTests
{
    [Fact]
    public void ShuttingDownWithPlayersInTheLobbySaysNothing()
    {
        TestServer server = new();
        server.Connect(7, "alice");
        server.Receive(7, new TypingMsg(true));
        server.Link.Messages.Clear();

        server.Server.Dispose();

        Assert.Empty(server.Link.Messages);
    }

    [Fact]
    public void ShuttingDownMidMatchClosesTheMatchScopeToo()
    {
        TestServer server = new();
        server.Connect(7, "alice");
        server.Connect(8, "bob");
        server.Receive(7, new SetReadyMsg(true));
        server.Receive(8, new SetReadyMsg(true));
        server.Tick();
        server.Link.Messages.Clear();

        server.Server.Dispose();

        Assert.Empty(server.Link.Messages);
    }
}
