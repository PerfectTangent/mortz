using System.Net;
using System.Net.Sockets;
using Mortz.Protocol.Net.Query;
using Serilog;

namespace Mortz.Server.Discovery;

/// <summary>Answers A2S queries on the query port.</summary>
public sealed class QueryResponder(ILogger log) : IDisposable
{
    private const int MAX_PACKETS_PER_POLL = 32;
    private const int MAX_PACKET_BYTES = 16384;

    private readonly A2SResponder _responder = new();
    private readonly byte[] _buffer = new byte[MAX_PACKET_BYTES + 1];
    private Socket? _socket;
    private Func<ServerInfo>? _describe;

    public bool Start(int queryPort, Func<ServerInfo> describe, bool loopbackOnly)
    {
        if (_socket != null)
            throw new InvalidOperationException("Query responder already started.");
        Socket socket = loopbackOnly
            ? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            : new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        try
        {
            if (OperatingSystem.IsWindows())
                IgnoreConnectionResets(socket);
            socket.Bind(new IPEndPoint(loopbackOnly ? IPAddress.Loopback : IPAddress.IPv6Any, queryPort));
            socket.Blocking = false;
        }
        catch (SocketException e)
        {
            log.Error("query port {Port} unavailable: {Error}, direct joining still works", queryPort, e.SocketErrorCode);
            socket.Dispose();
            return false;
        }
        _socket = socket;
        _describe = describe;
        log.Information("answering A2S queries on port {Port}", queryPort);
        return true;
    }

    public void Stop()
    {
        _socket?.Dispose();
        _socket = null;
        _describe = null;
    }

    public void Dispose() => Stop();

    public void Poll(ulong nowMs)
    {
        if (_socket is not Socket socket || _describe == null)
            return;
        EndPoint any = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetwork
            ? IPAddress.Any : IPAddress.IPv6Any, 0);
        for (int i = 0; i < MAX_PACKETS_PER_POLL && socket.Available > 0; i++)
        {
            int length;
            EndPoint from = any;
            try
            {
                length = socket.ReceiveFrom(_buffer, ref from);
            }
            catch (SocketException)
            {
                continue;
            }
            byte[] packet = _buffer[..length];
            if (length > MAX_PACKET_BYTES || !ServerQueryProtocol.TryDecodeRequest(packet, out _, out _))
                continue;
            IPEndPoint source = (IPEndPoint)from;
            foreach (byte[] response in _responder.Respond(packet, source.Address.ToString(), source.Port, nowMs, _describe()))
            {
                Send(socket, response, source);
            }
        }
    }

    private static void Send(Socket socket, byte[] data, IPEndPoint target)
    {
        try
        {
            socket.SendTo(data, target);
        }
        catch (SocketException)
        {
            // A full send buffer or an unreachable querier: the querier retries.
        }
    }

    /// <summary>Without this, an ICMP "port unreachable" from a querier that went away makes the
    /// next receive on Windows throw.</summary>
    private static void IgnoreConnectionResets(Socket socket)
    {
        const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
        socket.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
    }
}
