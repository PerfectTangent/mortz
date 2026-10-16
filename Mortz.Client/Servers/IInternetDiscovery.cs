using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Servers;

/// <summary>A server from Steam's list, with the info Steam already queried.</summary>
public readonly record struct InternetServer(ServerEndpoint Endpoint, ulong AccountId, ServerInfo Info, int PingMs);
public enum InternetDiscoveryResult { COMPLETE, FAILED }
public enum InternetDiscoveryState { UNAVAILABLE, READY, SEARCHING, COMPLETE, FAILED, TIMED_OUT }

public interface IInternetDiscovery
{
    bool Available { get; }
    IDisposable Request(Action<InternetServer> found, Action<InternetDiscoveryResult> completed);
}

public class UnavailableInternetDiscovery : IInternetDiscovery
{
    public bool Available => false;
    public IDisposable Request(Action<InternetServer> found, Action<InternetDiscoveryResult> completed) =>
        throw new InvalidOperationException("Steam discovery is unavailable.");
}
