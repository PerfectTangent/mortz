using Mortz.Protocol.Net.Query;

namespace Mortz.Server.Platform;

public interface IServerPlatform : IDisposable
{
    bool Initialize(ServerInfo info, bool isPublic);
    bool AuthenticationAvailable { get; }
    bool PublicationActive { get; }
    /// <summary>True until the first login attempt succeeds or fails.</summary>
    bool LoginPending { get; }
    string Status { get; }
    void Advance(ServerInfo info);
}
