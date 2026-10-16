using Mortz.Client.Servers;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class InternetDiscoveryTests
{
    private static readonly ServerEndpoint _endpoint = new("192.0.2.1", 17000, 27000);
    private static ServerInfo Info() => new("Public", "deathmatch", "Arena", 3, 8, true, true,
        17000, NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH);
    private static InternetServer Steam(ServerEndpoint endpoint, ulong account) => new(endpoint, account, Info(), 20);
    private const string SERVER_ID = "00112233445566ff";
    private static readonly ServerEndpoint _fakeIp = new("169.254.10.1", 40000);
    private static InternetServer SteamWithId(ServerEndpoint endpoint, ulong account) =>
        new(endpoint, account, Info() with { ServerId = SERVER_ID }, 20);

    [Fact]
    public void SteamResultUsesSteamsAnswerAndJoinsOnlyThroughTheRelay()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { }, discovery, () => 0);
        browser.Open();
        discovery.Last.Found(Steam(_endpoint, 123));
        Assert.DoesNotContain(_endpoint, probe.Requests);
        browser.Select(_endpoint);
        Assert.Equal(ServerStatus.ONLINE, browser.Selected!.Status);
        Assert.Equal(3, browser.Selected.Info!.Players);
        Assert.Equal(20, browser.Selected.PingMs);
        Assert.False(browser.Selected.CanToggleFavorite);
        ServerJoinRequest? joined = null;
        browser.JoinRequested += request => joined = request;
        browser.JoinSelected();
        Assert.Equal(new ServerJoinRequest(_endpoint.Address, _endpoint.Port, 123, RelayOnly: true), joined);
        browser.JoinDirect("other.test", "7777");
        Assert.Equal(new ServerJoinRequest("other.test", 7777), joined);
    }

    [Fact]
    public void SteamFavoriteFindsTheServerAgainUnderANewFakeIpAndSteamId()
    {
        ServerEndpoint later = new("169.254.20.2", 41000);
        List<FavoriteServer> saved = [];
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => saved, favorites => saved = [.. favorites],
            discovery, () => 0);
        browser.Open();
        discovery.Last.Found(SteamWithId(_fakeIp, 123));
        browser.ToggleFavorite(_fakeIp);
        Assert.Equal(SERVER_ID, Assert.Single(saved).ServerId);
        Assert.Equal("Public", saved[0].Label);
        browser.Close();

        probe.Requests.Clear();
        browser.Open();
        Assert.DoesNotContain(_fakeIp, probe.Requests);
        discovery.Last.Found(SteamWithId(later, 456));
        browser.Select(later);
        Assert.Equal(ServerSource.FAVORITE, browser.Selected!.Source);
        Assert.Equal(ServerStatus.ONLINE, browser.Selected.Status);
        Assert.Single(browser.Entries, entry => entry.Source == ServerSource.FAVORITE);
        ServerJoinRequest? joined = null;
        browser.JoinRequested += request => joined = request;
        browser.JoinSelected();
        Assert.Equal(new ServerJoinRequest(later.Address, later.Port, 456, RelayOnly: true), joined);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SteamFavoriteSteamDoesNotListIsOfflineAndUnsavingDropsIt(bool steamAvailable)
    {
        List<FavoriteServer> saved = [new(_fakeIp.Address, _fakeIp.Port, "Friday", ServerId: SERVER_ID)];
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new() { Available = steamAvailable };
        using ServerBrowserController browser = new(probe, () => saved, favorites => saved = [.. favorites],
            discovery, () => 0);
        browser.Open();
        Assert.DoesNotContain(_fakeIp, probe.Requests);
        browser.Select(_fakeIp);
        if (steamAvailable)
        {
            Assert.Equal(ServerStatus.PROBING, browser.Selected!.Status);
            discovery.Last.Completed(InternetDiscoveryResult.COMPLETE);
        }
        Assert.Equal(ServerStatus.OFFLINE, browser.Selected!.Status);
        browser.ToggleFavorite(_fakeIp);
        Assert.Empty(saved);
        Assert.Null(browser.Selected);
        Assert.DoesNotContain(browser.Entries, entry => entry.Endpoint == _fakeIp);
    }

    [Fact]
    public void SteamResultForAKnownServerIsStillProbedAndKeepsItsFallback()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe,
            () => [new(_endpoint.Address, _endpoint.Port, "Saved", _endpoint.QueryPort)], _ => { }, discovery, () => 0);
        browser.Open();
        probe.Requests.Clear();
        discovery.Last.Found(Steam(_endpoint, 123));
        Assert.Contains(_endpoint, probe.Requests);
        probe.Reply(_endpoint, Info());
        browser.Select(_endpoint);
        ServerJoinRequest? joined = null;
        browser.JoinRequested += request => joined = request;
        browser.JoinSelected();
        Assert.Equal(new ServerJoinRequest(_endpoint.Address, _endpoint.Port, 123), joined);
    }

    [Fact]
    public void RefreshCloseAndCompletionReleaseOwnershipAndIgnoreLateResults()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { }, discovery, () => 0);
        browser.Open();
        Request old = discovery.Last;
        browser.Refresh();
        Assert.Equal(1, old.Disposals);
        old.Found(Steam(_endpoint, 123));
        Assert.DoesNotContain(browser.Entries, entry => entry.SeenOnSteam);
        discovery.Last.Found(Steam(_endpoint, 123));
        discovery.Last.Completed(InternetDiscoveryResult.COMPLETE);
        Assert.Equal(1, discovery.Last.Disposals);
        Assert.Equal(InternetDiscoveryState.COMPLETE, browser.InternetState);
        discovery.Last.Found(Steam(new("192.0.2.2", 7777), 124));
        Assert.Equal(2, browser.Entries.Count);
        browser.Refresh();
        Request closing = discovery.Last;
        browser.Close();
        closing.Found(Steam(_endpoint, 123));
        Assert.Equal(1, closing.Disposals);
        Assert.DoesNotContain(browser.Entries, entry => entry.SeenOnSteam);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadlineAndOutageKeepFavoritesLanAndBlindJoinAvailable(bool outage)
    {
        ulong now = 0;
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [new("saved.test", 7777)], _ => { }, discovery, () => now);
        browser.Open();
        if (outage) { discovery.Available = false; }
        else { now = ServerBrowserController.INTERNET_TIMEOUT_MS; }
        discovery.Last.Found(Steam(_endpoint, 123));
        Assert.Equal(outage ? InternetDiscoveryState.UNAVAILABLE : InternetDiscoveryState.TIMED_OUT, browser.InternetState);
        Assert.Equal(1, discovery.Last.Disposals);
        Assert.Contains(browser.Entries, entry => entry.Endpoint.Address == "saved.test");
        probe.Discover(_endpoint, Info());
        Assert.Contains(browser.Entries, entry => entry.SeenOnLan);
        bool joined = false;
        browser.JoinRequested += _ => joined = true;
        browser.JoinDirect("saved.test", "7777");
        Assert.True(joined);
        discovery.Available = true;
        browser.Advance();
        if (outage) { Assert.Equal(InternetDiscoveryState.READY, browser.InternetState); }
    }

    [Fact]
    public void DnsConfirmedAliasRetainsSavedHostnameQueryPortLabelAndSteamIdentity()
    {
        ServerEndpoint saved = new("Saved.Example.", 17000, 28000);
        ServerList list = new([new(saved.Address, saved.Port, "Friday", saved.QueryPort)]);
        list.ApplyInternet(Steam(_endpoint, 123));
        list.AddDiscovered(_endpoint);
        list.ConfirmResolved(saved, _endpoint.Address);
        list.ApplyReply(_endpoint, Info(), 20);
        ServerEntry entry = list.Find(_endpoint)!;
        Assert.Equal(saved, entry.Endpoint);
        Assert.Equal(123UL, entry.SteamAccountId);
        Assert.True(entry.SeenOnLan);
        Assert.True(entry.SeenOnSteam);
        Assert.Equal("Friday", Assert.Single(list.Favorites).Label);
        Assert.Equal(28000, Assert.Single(list.Favorites).QueryPort);
        Assert.Equal(2, list.Entries.Count);
        list.MarkProbing();
        Assert.False(entry.SeenOnSteam);
        Assert.False(entry.SeenOnLan);
        Assert.Equal(0UL, entry.SteamAccountId);
        Assert.Single(list.Favorites);
    }

    [Fact]
    public void DiscoveryBudgetDeduplicatesAndNeverDiscardsFavorites()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [new("saved.test", 7777)], _ => { }, discovery, () => 0);
        browser.Open();
        for (int i = 0; i < 1100; i++)
        {
            discovery.Last.Found(Steam(new($"192.0.{i / 256}.{i % 256}", 7777), (ulong)i));
        }
        Assert.Equal(1002, browser.Entries.Count);
        Assert.DoesNotContain(probe.Requests, endpoint => endpoint.Address.StartsWith("192.0.", StringComparison.Ordinal));
        Assert.Contains(browser.Entries, entry => entry.Endpoint.Address == "saved.test");
    }

    [Fact]
    public void SynchronousCompletionDisposesReturnedRequestExactlyOnce()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new() { CompleteSynchronously = true };
        using ServerBrowserController browser = new(probe, () => [], _ => { }, discovery, () => 0);
        browser.Open();
        Assert.Equal(InternetDiscoveryState.COMPLETE, browser.InternetState);
        Assert.Equal(1, discovery.Last.Disposals);
        browser.Close();
        Assert.Equal(1, discovery.Last.Disposals);
    }

    [Fact]
    public void RequestFailureDoesNotPreventLanFavoritesOrDirectJoining()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new() { Throw = true };
        using ServerBrowserController browser = new(probe, () => [new("saved.test", 7777)], _ => { }, discovery, () => 0);
        browser.Open();
        Assert.Equal(InternetDiscoveryState.FAILED, browser.InternetState);
        Assert.Equal(1, probe.LanStarts);
        Assert.Contains(browser.Entries, entry => entry.IsFavorite);
        ServerJoinRequest? joined = null;
        browser.JoinRequested += request => joined = request;
        browser.JoinDirect("saved.test", "7777");
        Assert.Equal(new ServerJoinRequest("saved.test", 7777), joined);
    }

    [Fact]
    public void CompletionAtDeadlineIsTimeoutAndCannotReviveARefresh()
    {
        ulong now = 0;
        Discovery discovery = new();
        using ServerBrowserController browser = new(new ServerBrowserControllerTests.FakeProbe(),
            () => [], _ => { }, discovery, () => now);
        browser.Open();
        now = ServerBrowserController.INTERNET_TIMEOUT_MS;
        discovery.Last.Completed(InternetDiscoveryResult.COMPLETE);
        Assert.Equal(InternetDiscoveryState.TIMED_OUT, browser.InternetState);
        Assert.Equal(1, discovery.Last.Disposals);
    }

    [Fact]
    public void LanAndSteamShareTransientBudgetButFavoritesCanStillBeEnriched()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [new(_endpoint.Address, _endpoint.Port, "Saved", _endpoint.QueryPort)],
            _ => { }, discovery, () => 0);
        browser.Open();
        for (int i = 0; i < 700; i++)
        {
            probe.Discover(new($"10.0.{i / 256}.{i % 256}", 7777), Info());
        }
        for (int i = 0; i < 700; i++)
        {
            discovery.Last.Found(Steam(new($"10.1.{i / 256}.{i % 256}", 7777), (ulong)i));
        }
        discovery.Last.Found(Steam(_endpoint, 123));
        Assert.Equal(1002, browser.Entries.Count);
        Assert.Equal(123UL, browser.Entries.Single(entry => entry.Endpoint == _endpoint).SteamAccountId);
        Assert.Contains(_endpoint, probe.Requests);
        Assert.DoesNotContain(probe.Requests, endpoint => endpoint.Address.StartsWith("10.1.", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvedFavoriteMergesLaterSteamAndLanWithoutLosingItsQueryEndpoint()
    {
        ServerEndpoint saved = new("saved.example", _endpoint.Port, 28000);
        ServerList list = new([new(saved.Address, saved.Port, "Friday", saved.QueryPort)]);
        list.ConfirmResolved(saved, _endpoint.Address);
        list.ApplyInternet(Steam(_endpoint, 123));
        list.AddDiscovered(_endpoint);
        list.ApplyReply(saved, Info(), 20);
        list.ApplyTimeout(_endpoint);
        ServerEntry entry = list.Find(_endpoint)!;
        Assert.Equal(2, list.Entries.Count);
        Assert.Equal(saved, entry.Endpoint);
        Assert.Equal(ServerStatus.ONLINE, entry.Status);
        Assert.True(entry.SeenOnSteam && entry.SeenOnLan);
        Assert.Equal(123UL, entry.SteamAccountId);
    }

    [Fact]
    public void ResultBatchesNotifyPresentationOncePerAdvance()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { }, discovery, () => 0);
        browser.Open();
        int changes = 0;
        browser.Changed += () => changes++;
        for (int i = 0; i < 100; i++)
        {
            ServerEndpoint endpoint = new($"192.0.2.{i}", 7777);
            discovery.Last.Found(Steam(endpoint, (ulong)i));
            probe.Reply(endpoint, Info());
        }
        Assert.Equal(0, changes);
        browser.Advance();
        Assert.Equal(1, changes);
        browser.Advance();
        Assert.Equal(1, changes);
    }

    private class Discovery : IInternetDiscovery
    {
        public bool Available { get; set; } = true;
        public Request Last { get; private set; } = null!;
        public bool CompleteSynchronously;
        public bool Throw;
        public IDisposable Request(Action<InternetServer> found, Action<InternetDiscoveryResult> completed)
        {
            if (Throw) { throw new InvalidOperationException("Unavailable"); }
            Last = new(found, completed);
            if (CompleteSynchronously) { completed(InternetDiscoveryResult.COMPLETE); }
            return Last;
        }
    }

    private class Request(Action<InternetServer> found, Action<InternetDiscoveryResult> completed) : IDisposable
    {
        public Action<InternetServer> Found => found;
        public Action<InternetDiscoveryResult> Completed => completed;
        public int Disposals;
        public void Dispose() => Disposals++;
    }
}
