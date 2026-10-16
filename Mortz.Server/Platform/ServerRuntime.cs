using Mortz.Protocol.Net.Query;

namespace Mortz.Server.Platform;

/// <summary>Starts the platform and the query port, and reports what works.</summary>
public class ServerRuntime(IServerPlatform? platform, Func<int, bool> startQuery, Action stopQuery) : IDisposable
{
    private const string STANDALONE = "Standalone";
    private const string QUERY_UNAVAILABLE = "Query port unavailable, direct joining still works.";
    /// <summary>Set only once the platform initialized.</summary>
    private IServerPlatform? _platform;
    private string _status = "";
    private bool _query;
    private bool _started;
    private bool _disposed;
    public ServerCapabilities Capabilities { get; private set; }

    public bool LoginPending => _platform?.LoginPending == true;

    public void Start(ServerInfo info, int queryPort, bool isPublic)
    {
        if (_started || _disposed)
            throw new InvalidOperationException("Server runtime already started or disposed.");
        _started = true;
        if (platform?.Initialize(info, isPublic) == true)
            _platform = platform;
        else
            platform?.Dispose();
        _query = startQuery(queryPort);
        // A platform that failed to start explains why in its last status.
        _status = platform?.Status ?? STANDALONE;
        RefreshCapabilities();
    }

    public void Advance(ServerInfo info)
    {
        if (_platform == null)
            return;
        _platform.Advance(info);
        RefreshCapabilities();
    }

    private void RefreshCapabilities() => Capabilities = new ServerCapabilities(_query,
        _platform?.AuthenticationAvailable == true, _platform?.PublicationActive == true,
        _query ? _platform?.Status ?? _status : QUERY_UNAVAILABLE);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _platform?.Dispose();
        _platform = null;
        stopQuery();
        Capabilities = default;
    }
}
