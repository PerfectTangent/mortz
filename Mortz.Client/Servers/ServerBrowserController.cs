using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Servers;

public enum BrowserNotice { NONE, FOUND, INCOMPATIBLE, NO_RESPONSE, PINNED }
public enum DirectConnectState { CLOSED, EDITING, PROBING, NOT_FOUND }

public readonly record struct ServerJoinRequest(string Address, int Port, ulong KnownSteamAccountId = 0,
    bool RelayOnly = false);

public class ServerBrowserController : IDisposable
{
    public const ulong INTERNET_TIMEOUT_MS = 15_000;
    private readonly IServerProbe _probe;
    private readonly IInternetDiscovery _internet;
    private readonly Func<ulong> _clock;
    private IDisposable? _request;
    private long _generation;
    private ulong _internetDeadline;
    private readonly HashSet<ServerEndpoint> _internetEndpoints = [];
    private readonly Func<IEnumerable<FavoriteServer>> _readFavorites;
    private readonly Action<IEnumerable<FavoriteServer>> _saveFavorites;
    private ServerList _list = new();
    private bool _changedPending;
    private bool _open;
    private bool _disposed;

    public ServerBrowserController(IServerProbe probe, Func<IEnumerable<FavoriteServer>> readFavorites,
        Action<IEnumerable<FavoriteServer>> saveFavorites, IInternetDiscovery? internet = null,
        Func<ulong>? clock = null)
    {
        _probe = probe;
        _internet = internet ?? new UnavailableInternetDiscovery();
        _clock = clock ?? (() => (ulong)Environment.TickCount64);
        _readFavorites = readFavorites;
        _saveFavorites = saveFavorites;
        _probe.Replied += OnReplied;
        _probe.Discovered += OnLanDiscovered;
        _probe.TimedOut += OnTimedOut;
    }

    public IReadOnlyList<ServerEntry> Entries => _list.Entries;
    public ServerEntry? Selected { get; private set; }
    public InternetDiscoveryState InternetState { get; private set; }
    public int InternetResults { get; private set; }
    public BrowserNotice Notice { get; private set; }
    public ServerEndpoint? NoticeEndpoint { get; private set; }
    public DirectConnectState DirectState { get; private set; }
    public ServerAddressError AddressError { get; private set; }
    public ServerEndpoint? DirectTarget { get; private set; }
    public event Action? Changed;
    public event Action<ServerJoinRequest>? JoinRequested;

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _open = true;
        _list = new ServerList(_readFavorites());
        Selected = null;
        DirectState = DirectConnectState.CLOSED;
        Refresh();
    }

    public void Close()
    {
        _open = false;
        DirectTarget = null;
        DirectState = DirectConnectState.CLOSED;
        _probe.Cancel();
        CancelInternet();
    }

    public void Refresh()
    {
        if (!_open) { return; }
        _probe.Cancel();
        CancelInternet();
        DirectTarget = null;
        AddressError = ServerAddressError.NONE;
        if (DirectState != DirectConnectState.CLOSED)
        {
            DirectState = DirectConnectState.EDITING;
        }
        Notice = BrowserNotice.NONE;
        _list.MarkProbing();
        Reselect();
        _probe.Probe(Entries.Where(entry => !entry.RelayOnly).Select(entry => entry.Endpoint));
        _probe.DiscoverLan();
        StartInternet();
        NotifyChanged();
    }

    public void Select(ServerEndpoint endpoint)
    {
        if (!_open) { return; }
        Selected = _list.Find(endpoint);
        Notice = BrowserNotice.NONE;
        NotifyChanged();
    }

    public void JoinSelected()
    {
        if (!_open || Selected is not ServerEntry entry) { return; }
        if (entry.Status == ServerStatus.ONLINE)
        {
            JoinRequested?.Invoke(new(entry.Endpoint.Address, entry.Endpoint.Port, entry.SteamAccountId, entry.RelayOnly));
            return;
        }
        Notice = entry.Status == ServerStatus.INCOMPATIBLE
            ? BrowserNotice.INCOMPATIBLE : BrowserNotice.NO_RESPONSE;
        NotifyChanged();
    }

    public void ToggleFavorite(ServerEndpoint endpoint)
    {
        if (!_open || _list.Find(endpoint) is not ServerEntry entry) { return; }
        if (_list.ToggleFavorite(entry))
        {
            _saveFavorites(_list.Favorites);
            Reselect();
        }
        else
        {
            Notice = BrowserNotice.PINNED;
        }
        NotifyChanged();
    }

    public void OpenDirect()
    {
        if (!_open) { return; }
        DirectTarget = null;
        AddressError = ServerAddressError.NONE;
        DirectState = DirectConnectState.EDITING;
        NotifyChanged();
    }

    public void CancelDirect()
    {
        DirectTarget = null;
        DirectState = DirectConnectState.CLOSED;
        NotifyChanged();
    }

    public void FindDirect(string address, string port, string queryPort)
    {
        if (!_open) { return; }
        DirectTarget = null;
        if (!ServerAddressInput.TryReadQuery(address, port, queryPort,
                out ServerEndpoint endpoint, out ServerAddressError error))
        {
            AddressError = error;
            DirectState = DirectConnectState.EDITING;
            NotifyChanged();
            return;
        }
        AddressError = ServerAddressError.NONE;
        DirectTarget = endpoint;
        DirectState = DirectConnectState.PROBING;
        _list.AddDirect(endpoint).Status = ServerStatus.PROBING;
        NotifyChanged();
        _probe.Probe(endpoint);
    }

    public void JoinDirect(string addressText, string portText)
    {
        if (!_open) { return; }
        if (ServerAddressInput.TryReadGame(addressText, portText,
                out string address, out int port, out ServerAddressError error))
        {
            JoinRequested?.Invoke(new(address, port));
            return;
        }
        AddressError = error;
        NotifyChanged();
    }

    private void StartInternet()
    {
        InternetResults = 0;
        _internetEndpoints.Clear();
        if (!_internet.Available)
        {
            InternetState = InternetDiscoveryState.UNAVAILABLE;
            _list.MarkUnlisted();
            return;
        }
        InternetState = InternetDiscoveryState.SEARCHING;
        _internetDeadline = _clock() + INTERNET_TIMEOUT_MS;
        long generation = _generation;
        try
        {
            IDisposable request = _internet.Request(
                server => InternetFound(generation, server), result => InternetCompleted(generation, result));
            if (generation == _generation) { _request = request; }
            else { request.Dispose(); }
        }
        catch
        {
            EndInternet(InternetDiscoveryState.FAILED);
        }
    }

    public void Advance()
    {
        CheckInternetState();
        if (_open && _changedPending)
        {
            _changedPending = false;
            NotifyChanged();
        }
    }

    private void CheckInternetState()
    {
        if (!_open) { return; }
        if (!_internet.Available && InternetState != InternetDiscoveryState.UNAVAILABLE)
            EndInternet(InternetDiscoveryState.UNAVAILABLE);
        else if (_internet.Available && InternetState == InternetDiscoveryState.UNAVAILABLE)
            InternetState = InternetDiscoveryState.READY;
        else if (InternetState == InternetDiscoveryState.SEARCHING && _clock() >= _internetDeadline)
            EndInternet(InternetDiscoveryState.TIMED_OUT);
        else
            return;
        _changedPending = true;
    }

    private void InternetFound(long generation, InternetServer server)
    {
        CheckInternetState();
        if (generation != _generation ||
            _internetEndpoints.Count >= ServerProbeCoordinator.MAX_TRANSIENT_RESULTS ||
            !_internetEndpoints.Add(server.Endpoint) ||
            _list.ApplyInternet(server) is not ServerEntry entry) { return; }
        InternetResults++;
        if (!entry.RelayOnly) { _probe.Probe(entry.Endpoint); }
        _changedPending = true;
    }

    private void InternetCompleted(long generation, InternetDiscoveryResult result)
    {
        CheckInternetState();
        if (generation != _generation) { return; }
        EndInternet(result == InternetDiscoveryResult.COMPLETE
            ? InternetDiscoveryState.COMPLETE : InternetDiscoveryState.FAILED);
        _changedPending = true;
    }

    private void EndInternet(InternetDiscoveryState state)
    {
        CancelInternet();
        InternetState = state;
        _list.MarkUnlisted();
    }

    private void CancelInternet()
    {
        _generation++;
        IDisposable? request = _request;
        _request = null;
        request?.Dispose();
    }

    /// <summary>Entries get merged or dropped, so the selection follows its endpoint.</summary>
    private void Reselect()
    {
        if (Selected != null) { Selected = _list.Find(Selected.Endpoint); }
    }

    private void OnLanDiscovered(ServerProbeReply reply)
    {
        if (!_open) { return; }
        _list.AddDiscovered(reply.Endpoint);
        OnReplied(reply);
    }

    private void OnReplied(ServerProbeReply reply)
    {
        if (!_open) { return; }
        _list.ConfirmResolved(reply.Endpoint, reply.ResolvedAddress);
        _list.ApplyReply(reply.Endpoint, reply.Info, reply.PingMs);
        Reselect();
        _changedPending = true;
        if (reply.Endpoint == DirectTarget)
        {
            DirectTarget = null;
            DirectState = DirectConnectState.CLOSED;
            Selected = _list.Find(reply.Endpoint);
            Notice = BrowserNotice.FOUND;
            NoticeEndpoint = reply.Endpoint;
            NotifyChanged();
        }
    }

    private void OnTimedOut(ServerEndpoint endpoint)
    {
        if (!_open) { return; }
        _list.ApplyTimeout(endpoint);
        _changedPending = true;
        if (endpoint == DirectTarget)
        {
            DirectTarget = null;
            DirectState = DirectConnectState.NOT_FOUND;
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        _changedPending = false;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        Close();
        _probe.Replied -= OnReplied;
        _probe.Discovered -= OnLanDiscovered;
        _probe.TimedOut -= OnTimedOut;
    }
}
