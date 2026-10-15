using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Mortz.Net.Gns;

/// <summary>ESteamNetworkingConfigValue keys, scopes and data types we use.</summary>
public static class GnsConfig
{
    public const int FAKE_PACKET_LOSS_SEND = 2;
    public const int FAKE_PACKET_LOSS_RECV = 3;
    public const int FAKE_PACKET_LAG_SEND = 4;
    public const int FAKE_PACKET_LAG_RECV = 5;
    public const int SEND_BUFFER_SIZE = 9;
    public const int SEND_RATE_MIN = 10;
    public const int SEND_RATE_MAX = 11;
    public const int RECV_BUFFER_SIZE = 47;
    public const int RECV_BUFFER_MESSAGES = 48;
    public const int RECV_MAX_MESSAGE_SIZE = 49;

    public const int SCOPE_GLOBAL = 1;

    public const int TYPE_INT32 = 1;
    public const int TYPE_FLOAT = 3;
}

/// <summary>SteamNetworkingConfigValue_t: key, data type, then an 8-byte union (only int32 used).</summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GnsConfigValue
{
    [FieldOffset(0)] public int Key;
    [FieldOffset(4)] public int DataType;
    [FieldOffset(8)] public int Int32;

    public static GnsConfigValue Int(int key, int value) =>
        new() { Key = key, DataType = GnsConfig.TYPE_INT32, Int32 = value };
}

/// <summary>k_nSteamNetworkingSend_* flags.</summary>
[Flags]
public enum GnsSend
{
    UNRELIABLE = 0,
    NO_NAGLE = 1,
    NO_DELAY = 4,
    RELIABLE = 8,
}

public enum GnsState
{
    NONE = 0,
    CONNECTING = 1,
    FINDING_ROUTE = 2,
    CONNECTED = 3,
    CLOSED_BY_PEER = 4,
    PROBLEM_DETECTED_LOCALLY = 5,
}

/// <summary>EResult values GNS returns from the calls we make.</summary>
public enum GnsResult
{
    OK = 1,
    NO_CONNECTION = 3,
}

/// <summary>Close reasons. GNS reserves 1000-1999 for the application.</summary>
public static class GnsEndReason
{
    public const int APP_GENERIC = 1000;
    public const int APP_KICKED = 1001;
    public const int APP_SHUTDOWN = 1002;
    public const int APP_SERVER_FULL = 1003;
}

/// <summary>Copied out of the native struct, which only lives during the callback.</summary>
public readonly record struct GnsStatusChange(
    uint Connection,
    uint ListenSocket,
    GnsState State,
    int EndReason,
    string EndDebug)
{
    // SteamNetConnectionStatusChangedCallback_t: m_hConn at 0, then m_info. Linux and macOS pack
    // the struct to 4, Windows to 8, which moves m_info.
    private static readonly int _infoOffset = OperatingSystem.IsWindows() ? 8 : 4;

    // SteamNetConnectionInfo_t, same on both packings.
    private const int INFO_LISTEN_SOCKET = 144;
    private const int INFO_STATE = 176;
    private const int INFO_END_REASON = 180;
    private const int INFO_END_DEBUG = 184;

    internal static unsafe GnsStatusChange Read(byte* callback)
    {
        byte* info = callback + _infoOffset;
        return new GnsStatusChange(
            *(uint*)callback,
            *(uint*)(info + INFO_LISTEN_SOCKET),
            (GnsState)(*(int*)(info + INFO_STATE)),
            *(int*)(info + INFO_END_REASON),
            GnsLibrary.Utf8(info + INFO_END_DEBUG));
    }
}

/// <summary>The useful part of SteamNetConnectionRealTimeStatus_t. Byte and packet figures are
/// rates per second, not counters.</summary>
public readonly record struct GnsRealTimeStatus(
    GnsState State,
    int PingMs,
    float OutPacketsPerSec,
    float OutBytesPerSec,
    float InPacketsPerSec,
    float InBytesPerSec)
{
    internal const int SIZE = 120;

    internal static unsafe GnsRealTimeStatus Read(byte* s) => new(
        (GnsState)(*(int*)s),
        *(int*)(s + 4),
        *(float*)(s + 16),
        *(float*)(s + 20),
        *(float*)(s + 24),
        *(float*)(s + 28));
}

/// <summary>SteamNetworkingIPAddr: 16-byte IPv6 address (IPv4 is v4-mapped), port in host order
/// at 16, packed to 1 byte.</summary>
internal static class GnsAddress
{
    public const int SIZE = 18;

    public static unsafe void Write(byte* dst, IPAddress address, ushort port)
    {
        IPAddress v6 = address.AddressFamily == AddressFamily.InterNetwork ? address.MapToIPv6() : address;
        v6.TryWriteBytes(new Span<byte>(dst, 16), out _);
        *(ushort*)(dst + 16) = port;
    }

    public static unsafe ushort Port(byte* src) => *(ushort*)(src + 16);
}

/// <summary>SteamNetworkingMessage_t field offsets (same on every platform).</summary>
internal static class GnsMessage
{
    public const int DATA = 0;
    public const int SIZE = 8;
    public const int CONNECTION = 12;
}
