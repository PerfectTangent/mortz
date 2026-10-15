using System.Buffers.Binary;

namespace Mortz.Protocol.Net;

/// <summary>The first byte of every transport message. 0 is never valid.</summary>
public enum WireKind : byte
{
    /// <summary><c>[u16 msgId][payload]</c>: a generated INetMessage.</summary>
    ENVELOPE = 1,

    /// <summary><c>[i32 ack][data]</c>, server to client.</summary>
    SNAPSHOT = 2,

    /// <summary><c>[packet]</c>, client to server.</summary>
    INPUTS = 3,

    /// <summary><c>[u8 type][body]</c>: admission handshake, see AdmissionWire. The only kind a
    /// peer may send before admission.</summary>
    ADMISSION = 4,
}

public static class WireFrame
{
    private const int ENVELOPE_HEADER = 1 + sizeof(ushort);
    private const int SNAPSHOT_HEADER = 1 + sizeof(int);

    /// <summary>GNS silently sends unreliable messages above this size reliably (15 x 1100 bytes).</summary>
    public const int MAX_UNRELIABLE_BYTES = 15 * 1100;

    /// <summary>Largest frame either side accepts: an envelope at its limit.</summary>
    public const int MAX_FRAME_BYTES = ENVELOPE_HEADER + NetConfig.MAX_ENVELOPE_BYTES;

    public static WireKind KindOf(ReadOnlySpan<byte> frame) => frame.IsEmpty ? 0 : (WireKind)frame[0];

    public static byte[] Envelope(ushort msgId, ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[ENVELOPE_HEADER + payload.Length];
        frame[0] = (byte)WireKind.ENVELOPE;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), msgId);
        payload.CopyTo(frame.AsSpan(ENVELOPE_HEADER));
        return frame;
    }

    public static bool TryReadEnvelope(ReadOnlySpan<byte> frame, out ushort msgId, out byte[] payload)
    {
        msgId = 0;
        payload = [];
        if (frame.Length < ENVELOPE_HEADER || KindOf(frame) != WireKind.ENVELOPE ||
            frame.Length - ENVELOPE_HEADER > NetConfig.MAX_ENVELOPE_BYTES)
            return false;
        msgId = BinaryPrimitives.ReadUInt16LittleEndian(frame[1..]);
        payload = frame[ENVELOPE_HEADER..].ToArray();
        return true;
    }

    public static byte[] Snapshot(int ack, ReadOnlySpan<byte> data)
    {
        byte[] frame = new byte[SNAPSHOT_HEADER + data.Length];
        frame[0] = (byte)WireKind.SNAPSHOT;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(1), ack);
        data.CopyTo(frame.AsSpan(SNAPSHOT_HEADER));
        return frame;
    }

    public static bool TryReadSnapshot(ReadOnlySpan<byte> frame, out int ack, out byte[] data)
    {
        ack = 0;
        data = [];
        if (frame.Length < SNAPSHOT_HEADER || KindOf(frame) != WireKind.SNAPSHOT)
            return false;
        ack = BinaryPrimitives.ReadInt32LittleEndian(frame[1..]);
        data = frame[SNAPSHOT_HEADER..].ToArray();
        return true;
    }

    public static byte[] Inputs(ReadOnlySpan<byte> packet)
    {
        byte[] frame = new byte[1 + packet.Length];
        frame[0] = (byte)WireKind.INPUTS;
        packet.CopyTo(frame.AsSpan(1));
        return frame;
    }

    public static bool TryReadInputs(ReadOnlySpan<byte> frame, int maxPacketBytes, out byte[] packet)
    {
        packet = [];
        if (frame.Length < 1 || KindOf(frame) != WireKind.INPUTS || frame.Length - 1 > maxPacketBytes)
            return false;
        packet = [.. frame[1..]];
        return true;
    }
}
