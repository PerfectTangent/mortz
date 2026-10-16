using System.Net;
using Mortz.Net.Gns;
using Mortz.Protocol.Input;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Abuse;
using Mortz.Protocol.Net.Admission;
using Mortz.Protocol.Net.Stats;
using Mortz.Server.Admission;
using Serilog;

namespace Mortz.Server;

/// <summary>Single-threaded: <see cref="Pump"/> and every send run on the tick thread.</summary>
public sealed class GnsServerTransport : IServerTransport, IServerAdmissionTransport, IDisposable
{
    private const GnsSend RELIABLE = GnsSend.RELIABLE | GnsSend.NO_NAGLE;
    private const GnsSend UNRELIABLE = GnsSend.UNRELIABLE | GnsSend.NO_NAGLE;

    private readonly GnsServer _server;
    private readonly List<Listener> _listeners = [];
    private readonly Func<ulong> _clock;
    private readonly ILogger _log;
    private readonly PeerGate _gate;

    // Our own peer ids: never 0 (no session) or SERVER_PEER_ID, never reused while the server runs.
    // Handles are only unique per listener's library, so they key together with it.
    private readonly Dictionary<(GnsServer, uint), int> _peerByConnection = [];
    private readonly Dictionary<int, (GnsServer Server, uint Connection)> _connectionByPeer = [];
    private int _nextPeerId = NetConfig.SERVER_PEER_ID + 1;
    private ulong _lastWireStatsMs;
    private bool _warnedLargeSnapshot;

    private ServerAdmission? _admission;
    private GameServer? _game;

    public int BoundPort => _server.BoundPort;

    private GnsServerTransport(GnsServer server, Func<ulong> clock, ILogger log, double rateScale)
    {
        _server = server;
        _listeners.Add(new Listener(this, server));
        _clock = clock;
        _log = log;
        _gate = new PeerGate(rateScale);
        _lastWireStatsMs = clock();
    }

    /// <summary>Port 0 picks a free port. <paramref name="rateScale"/> scales the per-peer rate
    /// limits in <see cref="PeerGate"/>.</summary>
    public static GnsServerTransport Listen(GnsLibrary lib, IPAddress bind, int port, Func<ulong> clock,
        ILogger log, double rateScale = 1) =>
        new(GnsServer.Listen(lib, bind, port, NetConfig.MAX_PLAYERS, Options), clock, log, rateScale);

    private static GnsServerOptions Options => new() { RecvMaxMessageBytes = WireFrame.MAX_FRAME_BYTES };

    /// <summary>Also accepts relay connections through Steam's game-server sockets.</summary>
    public void ListenRelay(GnsLibrary steam)
    {
        _listeners.Add(new Listener(this, GnsServer.ListenP2P(steam, 0, NetConfig.MAX_PLAYERS, Options)));
        _log.Information("accepting Steam relay connections");
    }

    /// <summary>Both are built on this transport, so they attach afterwards. Nothing is delivered
    /// until then.</summary>
    public void Attach(ServerAdmission admission, GameServer game)
    {
        _admission = admission;
        _game = game;
    }

    public void Detach()
    {
        _admission = null;
        _game = null;
    }

    public void Pump()
    {
        foreach (Listener listener in _listeners)
        {
            listener.Server.Pump(listener);
        }
    }

    public void Dispose()
    {
        foreach (Listener listener in _listeners)
        {
            listener.Server.Dispose();
        }
    }

    /// <summary>Tells the transport which listener a GNS event came from.</summary>
    private sealed class Listener(GnsServerTransport transport, GnsServer server) : IGnsHandler
    {
        public GnsServer Server { get; } = server;

        public void OnConnected(uint connection) => transport.OnConnected(Server, connection);

        public void OnDisconnected(uint connection, bool wasConnected, int reason, string debug) =>
            transport.OnDisconnected(Server, connection, reason, debug);

        public void OnMessage(uint connection, ReadOnlySpan<byte> data) => transport.OnMessage(Server, connection, data);
    }

    private void OnConnected(GnsServer server, uint connection)
    {
        int peerId = _nextPeerId++;
        _peerByConnection[(server, connection)] = peerId;
        _connectionByPeer[peerId] = (server, connection);
        server.Send(connection, AdmissionWire.Encode(new PeerAssigned(peerId)), RELIABLE);
        _gate.Connected(peerId);
        _log.Information("peer {PeerId} connected{Relay}", peerId, server == _server ? "" : " through Steam relay");
        _admission?.Connected(peerId, _clock());
    }

    private void OnDisconnected(GnsServer server, uint connection, int reason, string debug)
    {
        if (!Forget(server, connection, out int peerId))
            return;
        _log.Information("peer {PeerId} disconnected ({Reason} {Debug})", peerId, reason, debug);
        _admission?.Disconnected(peerId);
    }

    private void OnMessage(GnsServer server, uint connection, ReadOnlySpan<byte> frame)
    {
        if (!_peerByConnection.TryGetValue((server, connection), out int sender))
            return;
        ulong now = _clock();
        switch (WireFrame.KindOf(frame))
        {
            // The only kind allowed before admission, still rate limited.
            case WireKind.ADMISSION when _gate.AllowMessage(sender, now, NetAbusePolicy.EnvelopeCost(frame.Length)):
                switch (AdmissionWire.DecodeFromClient(frame))
                {
                    case AdmissionHello hello:
                        _admission?.Hello(sender, hello, now);
                        break;
                    case SteamProof proof:
                        _admission?.Proof(sender, proof, now);
                        break;
                }
                break;
            case WireKind.ENVELOPE when _gate.IsValidated(sender):
                if (WireFrame.TryReadEnvelope(frame, out ushort msgId, out byte[] payload) &&
                    _gate.AllowMessage(sender, now, NetAbusePolicy.EnvelopeCost(payload.Length)))
                {
                    _game?.Receive(sender, msgId, payload);
                }

                break;
            case WireKind.INPUTS when _gate.IsValidated(sender) && _gate.AllowInput(sender, now):
                if (WireFrame.TryReadInputs(frame, InputPacket.MAX_PACKET_BYTES, out byte[] packet))
                    _game?.Inputs(sender, packet);
                break;
        }
    }

    void IServerAdmissionTransport.Offer(int peerId, AdmissionOffer offer) =>
        SendTo(peerId, AdmissionWire.Encode(offer), RELIABLE);

    void IServerAdmissionTransport.Accept(int peerId, AdmissionAccepted accepted)
    {
        SendTo(peerId, AdmissionWire.Encode(accepted), RELIABLE);
        if (!_gate.TryValidate(peerId))
            throw new InvalidOperationException("Only a pending connected peer can be admitted.");
    }

    void IServerAdmissionTransport.Reject(int peerId, AdmissionRejected rejected)
    {
        _gate.Remove(peerId);
        SendTo(peerId, AdmissionWire.Encode(rejected), RELIABLE);
        // Close lingers, so the rejection still arrives.
        Close(peerId, "admission rejected");
    }

    public void Send<TMsg>(int peerId, in TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        if (_gate.IsValidated(peerId) && Envelope(in message) is { } frame)
            SendTo(peerId, frame, Flags<TMsg>());
    }

    public void Broadcast<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        if (Envelope(in message) is not { } frame)
            return;
        foreach (int peerId in _gate.ValidatedPeers)
        {
            SendTo(peerId, frame, Flags<TMsg>());
        }
    }

    /// <summary>Drops a peer without waiting for it to leave.</summary>
    public void Disconnect(int peerId, string reason)
    {
        _log.Information("disconnecting peer {PeerId}: {Reason}", peerId, reason);
        _gate.Remove(peerId);
        _admission?.Disconnected(peerId);
        Close(peerId, reason);
    }

    public void SendSnapshot(int peerId, byte[] data, int ack)
    {
        if (!_gate.IsValidated(peerId))
            return;
        byte[] frame = WireFrame.Snapshot(ack, data);
        if (frame.Length > WireFrame.MAX_UNRELIABLE_BYTES && !_warnedLargeSnapshot)
        {
            // GNS sends it reliably instead, which stalls everything behind it on loss.
            _warnedLargeSnapshot = true;
            _log.Warning("snapshot of {Bytes} bytes exceeds the unreliable limit of {Limit}",
                frame.Length, WireFrame.MAX_UNRELIABLE_BYTES);
        }
        SendTo(peerId, frame, UNRELIABLE);
    }

    /// <summary>GNS's smoothed round-trip time per validated peer.</summary>
    public PeerPing[] PeerPings() =>
        _gate.ValidatedPeers
            .Select(peerId => (peerId, status: Status(peerId)))
            .Where(p => p.status != null)
            .Select(p => new PeerPing(p.peerId, p.status!.Value.PingMs))
            .ToArray();

    /// <summary>GNS only reports rates, so this multiplies them by the time since the last call.</summary>
    public WireStats PopWireStats()
    {
        ulong now = _clock();
        double seconds = (now - _lastWireStatsMs) / 1000.0;
        _lastWireStatsMs = now;
        WireStats total = default;
        foreach (GnsRealTimeStatus status in _connectionByPeer.Keys.Select(Status).OfType<GnsRealTimeStatus>())
        {
            total = new WireStats(
                total.SentBytes + status.OutBytesPerSec * seconds,
                total.RecvBytes + status.InBytesPerSec * seconds,
                total.SentPackets + status.OutPacketsPerSec * seconds,
                total.RecvPackets + status.InPacketsPerSec * seconds);
        }
        return total;
    }

    private byte[]? Envelope<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        byte[] payload = TMsg.Serialize(in message);
        if (payload.Length <= NetConfig.MAX_ENVELOPE_BYTES)
            return WireFrame.Envelope(TMsg.MsgId, payload);
        _log.Error("refused oversized outgoing {MessageName} ({Bytes} bytes)",
            NetRegistry.NameOf(TMsg.MsgId), payload.Length);
        return null;
    }

    private static GnsSend Flags<TMsg>() where TMsg : struct, INetMessage<TMsg> =>
        TMsg.MsgChannel == NetChannel.RELIABLE ? RELIABLE : UNRELIABLE;

    private void SendTo(int peerId, byte[] frame, GnsSend flags)
    {
        if (_connectionByPeer.TryGetValue(peerId, out (GnsServer Server, uint Connection) peer))
            peer.Server.Send(peer.Connection, frame, flags);
    }

    private GnsRealTimeStatus? Status(int peerId) =>
        _connectionByPeer.TryGetValue(peerId, out (GnsServer Server, uint Connection) peer) ? peer.Server.Status(peer.Connection) : null;

    private void Close(int peerId, string reason)
    {
        if (!_connectionByPeer.TryGetValue(peerId, out (GnsServer Server, uint Connection) peer))
            return;
        Forget(peer.Server, peer.Connection, out _);
        peer.Server.Kick(peer.Connection, GnsEndReason.APP_KICKED, reason, linger: true);
    }

    private bool Forget(GnsServer server, uint connection, out int peerId)
    {
        if (!_peerByConnection.Remove((server, connection), out peerId))
            return false;
        _connectionByPeer.Remove(peerId);
        _gate.Remove(peerId);
        return true;
    }
}
