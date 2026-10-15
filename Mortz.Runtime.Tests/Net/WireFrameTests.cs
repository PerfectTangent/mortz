using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admission;
using Xunit;

namespace Mortz.Runtime.Tests.Net;

public class WireFrameTests
{
    [Fact]
    public void EnvelopeRoundTrips()
    {
        byte[] frame = WireFrame.Envelope(513, [1, 2, 3]);

        Assert.True(WireFrame.TryReadEnvelope(frame, out ushort msgId, out byte[] payload));
        Assert.Equal(513, msgId);
        Assert.Equal([1, 2, 3], payload);
    }

    [Fact]
    public void OversizedEnvelopeIsRejected()
    {
        byte[] frame = WireFrame.Envelope(1, new byte[NetConfig.MAX_ENVELOPE_BYTES + 1]);

        Assert.False(WireFrame.TryReadEnvelope(frame, out _, out _));
    }

    [Fact]
    public void SnapshotRoundTrips()
    {
        byte[] frame = WireFrame.Snapshot(-7, [9, 8]);

        Assert.True(WireFrame.TryReadSnapshot(frame, out int ack, out byte[] data));
        Assert.Equal(-7, ack);
        Assert.Equal([9, 8], data);
    }

    [Fact]
    public void InputsOverTheLimitAreRejected()
    {
        byte[] frame = WireFrame.Inputs(new byte[10]);

        Assert.True(WireFrame.TryReadInputs(frame, 10, out byte[] packet));
        Assert.Equal(10, packet.Length);
        Assert.False(WireFrame.TryReadInputs(frame, 9, out _));
    }

    [Fact]
    public void KindsDoNotCrossDecode()
    {
        byte[] snapshot = WireFrame.Snapshot(1, [1]);

        Assert.False(WireFrame.TryReadEnvelope(snapshot, out _, out _));
        Assert.False(WireFrame.TryReadInputs(snapshot, 100, out _));
        Assert.Null(AdmissionWire.DecodeFromClient(snapshot));
        Assert.Equal((WireKind)0, WireFrame.KindOf([]));
    }

    [Fact]
    public void ClientAdmissionFramesRoundTrip()
    {
        var hello = new AdmissionHello(NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH, "Gil", 3, true);

        Assert.Equal(hello, AdmissionWire.DecodeFromClient(AdmissionWire.Encode(hello)));
        var proof = (SteamProof)AdmissionWire.DecodeFromClient(AdmissionWire.Encode(new SteamProof(2, 77UL, [5, 6])))!;
        Assert.Equal((2, 77UL), (proof.Attempt, proof.AccountId));
        Assert.Equal([5, 6], proof.Ticket);
    }

    [Fact]
    public void ServerAdmissionFramesRoundTrip()
    {
        object[] frames =
        [
            new AdmissionOffer(1, AdmissionMode.STEAM, 99UL),
            new AdmissionAccepted(1, AdmissionMode.GUEST),
            new AdmissionRejected(4, AdmissionRejection.SERVER_FULL),
            new PeerAssigned(12),
        ];

        foreach (object frame in frames)
        {
            byte[] bytes = frame switch
            {
                AdmissionOffer m => AdmissionWire.Encode(m),
                AdmissionAccepted m => AdmissionWire.Encode(m),
                AdmissionRejected m => AdmissionWire.Encode(m),
                PeerAssigned m => AdmissionWire.Encode(m),
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(frame, AdmissionWire.DecodeFromServer(bytes));
        }
    }

    [Fact]
    public void FramesOnlyDecodeInTheirDirection()
    {
        byte[] hello = AdmissionWire.Encode(new AdmissionHello(NetConfig.PROTOCOL_VERSION, 0, "a", 0, false));
        byte[] offer = AdmissionWire.Encode(new AdmissionOffer(1, AdmissionMode.GUEST, 0));

        Assert.Null(AdmissionWire.DecodeFromServer(hello));
        Assert.Null(AdmissionWire.DecodeFromClient(offer));
    }

    [Fact]
    public void HelloFromAnotherVersionStillYieldsItsVersion()
    {
        // A future Hello with an extra field must still be read far enough to reject as INCOMPATIBLE.
        byte[] future = [.. AdmissionWire.Encode(new AdmissionHello(NetConfig.PROTOCOL_VERSION + 1, 1, "x", 0, false)), 0xFF];

        AdmissionHello hello = Assert.IsType<AdmissionHello>(AdmissionWire.DecodeFromClient(future));
        Assert.Equal(NetConfig.PROTOCOL_VERSION + 1, hello.ProtocolVersion);
    }

    [Fact]
    public void MalformedAdmissionIsIgnored()
    {
        byte[] hello = AdmissionWire.Encode(new AdmissionHello(NetConfig.PROTOCOL_VERSION, 0, "name", 0, false));

        Assert.Null(AdmissionWire.DecodeFromClient(hello[..^2]));
        Assert.Null(AdmissionWire.DecodeFromClient([.. hello, 0]));
        Assert.Null(AdmissionWire.DecodeFromClient([(byte)WireKind.ADMISSION, 1, 0xFF]));
    }

    [Fact]
    public void OversizedTicketIsIgnored()
    {
        byte[] proof = AdmissionWire.Encode(new SteamProof(1, 1, new byte[AdmissionLimits.TICKET_BYTES + 1]));

        Assert.Null(AdmissionWire.DecodeFromClient(proof));
    }
}
