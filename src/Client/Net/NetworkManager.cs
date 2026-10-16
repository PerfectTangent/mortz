using System.Net.Sockets;
using Godot;
using Mortz.Client.Logging;
using Mortz.Client.Session;
using Mortz.Net.Gns;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admission;
using Serilog;

namespace Mortz.Client.Net;

/// <summary>The client's connection to a server. GnsServerTransport is the server side.</summary>
[GlobalClass]
public partial class NetworkManager : Node, INetwork, IClientSender, IClientAdmissionTransport, IGnsHandler
{
    private static readonly ILogger _log = MortzLog.For("net");

    private const GnsSend RELIABLE = GnsSend.RELIABLE | GnsSend.NO_NAGLE;
    private const GnsSend UNRELIABLE = GnsSend.UNRELIABLE | GnsSend.NO_NAGLE;
    // Inputs are re-sent redundantly, so a stale one is worth less than a dropped one.
    private const GnsSend INPUTS = GnsSend.UNRELIABLE | GnsSend.NO_NAGLE | GnsSend.NO_DELAY;

    public event Action<AdmissionOffer>? OfferReceived;
    public event Action<AdmissionAccepted>? AdmissionAcceptedReceived;
    public event Action<AdmissionRejected>? AdmissionRejectedReceived;
    public bool ClientAdmitted { get; private set; }

    /// <summary>Fires once the server assigns <see cref="LocalPeerId"/>.</summary>
    public event Action? Connected;
    public event Action? ConnectionFailed;
    public event Action? Disconnected;
    public event Action? TransportReset;
    /// <summary>ack = newest input sequence the server applied for THIS client.</summary>
    public event Action<byte[], int>? SnapshotReceived;

    /// <summary>Every server-to-client message lands here, the mirror of the server's
    /// NetRouter&lt;Player&gt;.</summary>
    public NetRouter Router { get; } = new();

    private readonly HashSet<int> _undispatched = [];
    private GnsClient? _client;

    /// <summary>0 means no session.</summary>
    public int LocalPeerId { get; private set; }

    /// <summary>Ping and per-second traffic, null without a connection.</summary>
    public GnsRealTimeStatus? Status => _client?.Status(_client.Connection);

    public override void _ExitTree() => Close();

    public override void _Process(double delta) => _client?.Pump(this);

    public Error StartClient(string address, int port)
    {
        if (GnsRuntime.Load() is not { } lib)
            return Error.CantCreate;
        try
        {
            _client = GnsClient.Connect(lib, address, port);
            return Error.Ok;
        }
        catch (Exception e) when (e is GnsException or SocketException)
        {
            _log.Warning("could not connect to {Address}:{Port}: {Reason}", address, port, e.Message);
            return Error.CantConnect;
        }
    }

    public void ResetPeer()
    {
        Close();
        TransportReset?.Invoke();
    }

    private void Close()
    {
        ClientAdmitted = false;
        Router.MatchGeneration = -1;
        _undispatched.Clear();
        _client?.Dispose();
        _client = null;
        LocalPeerId = 0;
    }

    public void MarkClientAdmitted() => ClientAdmitted = true;

    void IGnsHandler.OnConnected(uint connection) { }

    void IGnsHandler.OnDisconnected(uint connection, bool wasConnected, int reason, string debug)
    {
        bool hadSession = LocalPeerId != 0;
        _log.Information("connection closed ({Reason} {Debug})", reason, debug);
        _client?.Dispose();
        _client = null;
        if (hadSession)
            Disconnected?.Invoke();
        else
            ConnectionFailed?.Invoke();
    }

    void IGnsHandler.OnMessage(uint connection, ReadOnlySpan<byte> frame)
    {
        switch (WireFrame.KindOf(frame))
        {
            case WireKind.ADMISSION:
                Admission(AdmissionWire.DecodeFromServer(frame));
                break;
            case WireKind.ENVELOPE when ClientAdmitted:
                if (WireFrame.TryReadEnvelope(frame, out ushort msgId, out byte[] payload))
                    Dispatch(msgId, payload);
                break;
            case WireKind.SNAPSHOT when ClientAdmitted:
                if (WireFrame.TryReadSnapshot(frame, out int ack, out byte[] data))
                    SnapshotReceived?.Invoke(data, ack);
                break;
        }
    }

    private void Admission(object? message)
    {
        switch (message)
        {
            case PeerAssigned assigned when LocalPeerId == 0 && assigned.PeerId > NetConfig.SERVER_PEER_ID:
                LocalPeerId = assigned.PeerId;
                Connected?.Invoke();
                break;
            case AdmissionOffer offer:
                OfferReceived?.Invoke(offer);
                break;
            case AdmissionAccepted accepted:
                AdmissionAcceptedReceived?.Invoke(accepted);
                break;
            case AdmissionRejected rejected:
                ClientAdmitted = false;
                AdmissionRejectedReceived?.Invoke(rejected);
                break;
        }
    }

    private void Dispatch(ushort msgId, byte[] payload)
    {
        if (Router.Dispatch(msgId, payload))
            return;
        if (_undispatched.Add(msgId))
            _log.Information("no handler for {MessageName}", NetRegistry.NameOf(msgId));
    }

    void IClientAdmissionTransport.Hello(AdmissionHello hello) =>
        _client?.Send(AdmissionWire.Encode(hello), RELIABLE);

    void IClientAdmissionTransport.Proof(SteamProof proof) =>
        _client?.Send(AdmissionWire.Encode(proof), RELIABLE);

    public void Send<TMsg>(in TMsg message) where TMsg : struct, INetMessage<TMsg>
    {
        // A client only sends once admitted, so teardown after ResetPeer stays quiet.
        if (!ClientAdmitted)
            return;
        byte[] payload = TMsg.Serialize(TMsg.InMatch(message, Router.MatchGeneration));
        if (payload.Length > NetConfig.MAX_ENVELOPE_BYTES)
        {
            _log.Error("refused oversized outgoing {MessageName} ({Bytes} bytes)",
                NetRegistry.NameOf(TMsg.MsgId), payload.Length);
            return;
        }
        _client?.Send(WireFrame.Envelope(TMsg.MsgId, payload),
            TMsg.MsgChannel == NetChannel.RELIABLE ? RELIABLE : UNRELIABLE);
    }

    public void SendInputs(byte[] packet)
    {
        if (ClientAdmitted)
            _client?.Send(WireFrame.Inputs(packet), INPUTS);
    }
}
