namespace Mortz.Protocol.Net.Admission;

/// <summary>Server to client, first message after connecting: the client's peer id for this session.</summary>
public readonly record struct PeerAssigned(int PeerId);

/// <summary>Hand-written so Hello decodes across protocol versions, and a mismatch reports
/// INCOMPATIBLE instead of failing to decode.</summary>
public static class AdmissionWire
{
    private enum Type : byte
    {
        HELLO = 1,
        PROOF = 2,
        OFFER = 3,
        ACCEPTED = 4,
        REJECTED = 5,
        PEER_ASSIGNED = 6,
    }

    public static byte[] Encode(AdmissionHello hello) => Write(Type.HELLO, w =>
    {
        w.Write(hello.ProtocolVersion);
        w.Write(hello.SchemaHash);
        w.Write(hello.Name);
        w.Write(hello.Skin);
        w.Write(hello.Steam);
    });

    public static byte[] Encode(SteamProof proof) => Write(Type.PROOF, w =>
    {
        w.Write(proof.Attempt);
        w.Write(proof.AccountId);
        w.WriteArray(proof.Ticket);
    });

    public static byte[] Encode(AdmissionOffer offer) => Write(Type.OFFER, w =>
    {
        w.Write(offer.Attempt);
        w.Write((int)offer.Mode);
        w.Write(offer.ServerAccountId);
    });

    public static byte[] Encode(AdmissionAccepted accepted) => Write(Type.ACCEPTED, w =>
    {
        w.Write(accepted.Attempt);
        w.Write((int)accepted.Mode);
    });

    public static byte[] Encode(AdmissionRejected rejected) => Write(Type.REJECTED, w =>
    {
        w.Write(rejected.Attempt);
        w.Write((int)rejected.Reason);
    });

    public static byte[] Encode(PeerAssigned assigned) => Write(Type.PEER_ASSIGNED, w => w.Write(assigned.PeerId));

    /// <summary>Client-to-server frames: <see cref="AdmissionHello"/> or <see cref="SteamProof"/>.
    /// Null for anything else, including malformed input.</summary>
    public static object? DecodeFromClient(ReadOnlySpan<byte> frame) => Read(frame, (type, r) => type switch
    {
        Type.HELLO => ReadHello(r),
        Type.PROOF => ReadProof(r),
        _ => null,
    });

    private static AdmissionHello ReadHello(BinaryReader r)
    {
        int version = r.ReadInt32();
        if (version != NetConfig.PROTOCOL_VERSION)
        {
            // Another version's Hello may be shaped differently, so skip the rest.
            r.BaseStream.Seek(0, SeekOrigin.End);
            return new AdmissionHello(version, 0, "", 0, false);
        }
        return new AdmissionHello(version, r.ReadUInt64(), NetIo.ReadString(r), r.ReadInt32(), r.ReadBoolean());
    }

    /// <summary>Server-to-client frames: offer, accepted, rejected or <see cref="PeerAssigned"/>.
    /// Null for anything else, including malformed input.</summary>
    public static object? DecodeFromServer(ReadOnlySpan<byte> frame) => Read(frame, (type, r) => type switch
    {
        Type.OFFER => new AdmissionOffer(r.ReadInt32(), (AdmissionMode)r.ReadInt32(), r.ReadUInt64()),
        Type.ACCEPTED => new AdmissionAccepted(r.ReadInt32(), (AdmissionMode)r.ReadInt32()),
        Type.REJECTED => new AdmissionRejected(r.ReadInt32(), (AdmissionRejection)r.ReadInt32()),
        Type.PEER_ASSIGNED => new PeerAssigned(r.ReadInt32()),
        _ => null,
    });

    private static SteamProof ReadProof(BinaryReader r)
    {
        int attempt = r.ReadInt32();
        ulong accountId = r.ReadUInt64();
        byte[] ticket = r.ReadByteArray();
        if (ticket.Length > AdmissionLimits.TICKET_BYTES)
            throw new InvalidDataException("ticket too large");
        return new SteamProof(attempt, accountId, ticket);
    }

    private static byte[] Write(Type type, Action<BinaryWriter> body)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        w.Write((byte)WireKind.ADMISSION);
        w.Write((byte)type);
        body(w);
        w.Flush();
        return ms.ToArray();
    }

    private static object? Read(ReadOnlySpan<byte> frame, Func<Type, BinaryReader, object?> body)
    {
        if (frame.Length < 2 || WireFrame.KindOf(frame) != WireKind.ADMISSION)
            return null;
        using MemoryStream ms = new(frame.ToArray(), writable: false);
        using BinaryReader r = new(ms);
        r.ReadByte();
        Type type = (Type)r.ReadByte();
        try
        {
            object? value = body(type, r);
            // Trailing bytes mean a different shape than we think: treat as malformed.
            return ms.Position == ms.Length ? value : null;
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }
}
