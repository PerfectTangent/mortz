namespace Mortz.Server;

/// <summary>Transport traffic since the last read: transport framing
/// included, IP/UDP headers not.</summary>
public readonly record struct WireStats(
    double SentBytes,
    double RecvBytes,
    double SentPackets,
    double RecvPackets
);
