using Mortz.Protocol.Net;

namespace Mortz.Runtime.Tests.Net;

/// <summary>Dispatches a server-to-client message into a client router.</summary>
public static class ServerWire
{
    extension<TMsg>(TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        public byte[] Payload() =>
            TMsg.Serialize(in message);

        public void Broadcast(NetRouter router) =>
            router.Dispatch(TMsg.MsgId, TMsg.Serialize(TMsg.InMatch(message, router.MatchGeneration)));

        public void SendTo(NetRouter router, int peerId) =>
            router.Dispatch(TMsg.MsgId, TMsg.Serialize(TMsg.InMatch(message, router.MatchGeneration)));
    }
}
