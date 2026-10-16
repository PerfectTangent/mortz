#if MORTZ_STEAM
using System.Net;
using Godot;
using Mortz.Client.Servers;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Names;
using Mortz.Protocol.Net.Query;
using SteamApi = GDExtension.Wrappers.Steam;

namespace Mortz.Platform.Steam;

public class SteamInternetDiscovery(SteamApi api, Func<bool> available) : IInternetDiscovery, IDisposable
{
    private Request? _request;
    private bool _disposed;
    public bool Available => !_disposed && available();

    IDisposable IInternetDiscovery.Request(Action<InternetServer> found, Action<InternetDiscoveryResult> completed)
    {
        if (!Available) { throw new InvalidOperationException("Steam discovery is unavailable."); }
        _request?.Dispose();
        Request request = new(api, found, completed);
        _request = request;
        request.Start();
        return request;
    }

    // Consumer callbacks run after the SDK pump, so cancellation can safely release native callbacks.
    public void Advance() => _request?.Dispatch();

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _request?.Dispose();
        _request = null;
    }

    private sealed class Request(SteamApi api, Action<InternetServer> found,
        Action<InternetDiscoveryResult> completed) : IDisposable
    {
        private long _handle;
        private bool _disposed;
        private bool _subscribed;
        private int _received;
        private readonly Queue<InternetServer> _results = new();
        private InternetDiscoveryResult? _completion;

        public void Start()
        {
            try
            {
                api.RequestServerListServerRespondedSignal += Responded;
                api.RequestServerListRefreshCompleteSignal += Completed;
                _subscribed = true;
                using Godot.Collections.Array gameDirectory = ["gamedir", "mortz"];
                using Godot.Collections.Array filters = [gameDirectory];
                _handle = api.RequestInternetServerList(NetConfig.GAME_APP_ID, filters);
                if (_handle == 0) { throw new InvalidOperationException("Steam did not create a server-list request."); }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Responded(long handle, long index)
        {
            if (_disposed || handle != _handle || _received >= ServerProbeCoordinator.MAX_TRANSIENT_RESULTS) { return; }
            using Godot.Collections.Dictionary details = api.GetServerDetails(index, handle);
            if (TryRead(details, out InternetServer server))
            {
                _received++;
                _results.Enqueue(server);
            }
        }

        private void Completed(long handle, long response)
        {
            if (_disposed || handle != _handle) { return; }
            _completion = response is 0 or 2 ? InternetDiscoveryResult.COMPLETE : InternetDiscoveryResult.FAILED;
        }

        public void Dispatch()
        {
            while (!_disposed && _results.TryDequeue(out InternetServer server))
            {
                found(server);
            }
            if (!_disposed && _completion is InternetDiscoveryResult result)
            {
                _completion = null;
                completed(result);
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            // Removing a handler that was never added disconnects the Godot signal, which errors.
            if (_subscribed)
            {
                api.RequestServerListServerRespondedSignal -= Responded;
                api.RequestServerListRefreshCompleteSignal -= Completed;
            }
            long handle = _handle;
            _handle = 0;
            _results.Clear();
            if (handle != 0)
            {
                try { api.CancelQuery(handle); }
                finally { api.ReleaseRequest(handle); }
            }
        }
    }

    private static bool TryRead(Godot.Collections.Dictionary details, out InternetServer server)
    {
        server = default;
        if (!details.TryGetValue("app_id", out Variant app) || app.AsInt64() != NetConfig.GAME_APP_ID ||
            !details.TryGetValue("connection_address", out Variant connection) ||
            !details.TryGetValue("query_address", out Variant query) ||
            !IPEndPoint.TryParse(connection.AsString(), out IPEndPoint? game) ||
            !IPEndPoint.TryParse(query.AsString(), out IPEndPoint? probe) ||
            !game.Address.Equals(probe.Address)) { return false; }
        try
        {
            // FakeIP entries share one port and are never queried, so the default query port is fine.
            ServerEndpoint endpoint = probe.Port == game.Port
                ? new(game.Address.ToString(), game.Port)
                : new(game.Address.ToString(), game.Port, probe.Port);
            ulong account = details.TryGetValue("steam_id", out Variant id) ? unchecked((ulong)id.AsInt64()) : 0;
            ServerInfo info = new(Text(details, "name"), "", Text(details, "map"), 0, Number(details, "max_players"),
                false, false, game.Port, 0, 0, NetConfig.GAME_APP_ID, MetadataValid: false);
            server = new InternetServer(endpoint, account,
                ServerQueryMetadata.ApplyTags(info, Text(details, "game_tags", ServerQueryMetadata.MAX_TAG_BYTES)),
                Number(details, "ping"));
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static string Text(Godot.Collections.Dictionary details, string key,
        int maxLength = ServerQueryProtocol.MAX_TEXT_LENGTH) =>
        details.TryGetValue(key, out Variant value) ? SafeName.Sanitize(value.AsString(), maxLength) : "";

    private static int Number(Godot.Collections.Dictionary details, string key) =>
        details.TryGetValue(key, out Variant value) ? Math.Max(0, value.AsInt32()) : 0;
}
#endif
