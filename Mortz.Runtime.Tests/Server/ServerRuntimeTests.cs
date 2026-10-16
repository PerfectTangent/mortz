using Mortz.Protocol.Net.Query;
using Mortz.Server.Platform;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class ServerRuntimeTests
{
    private static readonly ServerInfo _info = new("Test", "deathmatch", "duel", 0, 8, true, true, 7777, 45, 1);

    [Fact]
    public void InitializationFailureReleasesSdkAndStillAnswersQueries()
    {
        List<string> calls = [];
        FakePlatform platform = new(calls) { InitializeResult = false };
        using ServerRuntime runtime = new(platform, _ => { calls.Add("bind"); return true; }, () => calls.Add("close"));
        runtime.Start(_info, 7778, true);
        Assert.Equal(["initialize", "dispose", "bind"], calls);
        Assert.True(runtime.Capabilities.Query);
        Assert.False(runtime.Capabilities.Authentication);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WeAnswerQueriesWhetherOrNotSteamPublishes(bool isPublic)
    {
        List<string> calls = [];
        FakePlatform platform = new(calls);
        using ServerRuntime runtime = new(platform, _ => { calls.Add("bind"); return true; }, () => calls.Add("close"));
        runtime.Start(_info, 7778, isPublic);
        Assert.Equal(["initialize", "bind"], calls);
        Assert.True(runtime.Capabilities.Query);
        platform.AuthenticationAvailable = true;
        runtime.Advance(_info);
        Assert.True(runtime.Capabilities.Authentication);
        platform.AuthenticationAvailable = false;
        runtime.Advance(_info);
        Assert.False(runtime.Capabilities.Authentication);
    }

    [Fact]
    public void QueryBindFailureKeepsSteamAndTheGameRunning()
    {
        List<string> calls = [];
        FakePlatform platform = new(calls) { AuthenticationAvailable = true };
        using ServerRuntime runtime = new(platform, _ => false, () => calls.Add("close"));
        runtime.Start(_info, 7778, false);
        Assert.True(runtime.Capabilities.Authentication);
        Assert.False(runtime.Capabilities.Query);
        Assert.Equal(["initialize"], calls);
    }

    [Fact]
    public void ShutdownStopsSteamBeforeQueryAndIsIdempotent()
    {
        List<string> calls = [];
        FakePlatform platform = new(calls);
        ServerRuntime runtime = new(platform, _ => true, () => calls.Add("close"));
        runtime.Start(_info, 7778, false);
        runtime.Dispose();
        runtime.Dispose();
        Assert.Equal(["initialize", "dispose", "close"], calls);
        Assert.Equal(default, runtime.Capabilities);
    }

    [Fact]
    public void StandaloneDoesNotNeedPlatformAndReportsBindFailureSeparately()
    {
        using ServerRuntime runtime = new(null, _ => false, () => { });
        runtime.Start(_info, 7778, false);
        Assert.False(runtime.Capabilities.Query);
        Assert.False(runtime.Capabilities.Authentication);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void LoginIsPendingOnlyWhileAStartedPlatformLogsOn(bool initializes, bool platformPending, bool pending)
    {
        FakePlatform platform = new([]) { InitializeResult = initializes, LoginPending = platformPending };
        using ServerRuntime runtime = new(platform, _ => true, () => { });
        runtime.Start(_info, 7778, false);
        Assert.Equal(pending, runtime.LoginPending);
        runtime.Dispose();
        Assert.False(runtime.LoginPending);
    }

    private class FakePlatform(List<string> calls) : IServerPlatform
    {
        public bool InitializeResult { get; init; } = true;
        public bool AuthenticationAvailable { get; set; }
        public bool PublicationActive => false;
        public bool LoginPending { get; set; } = true;
        public string Status => "Login pending";
        public bool Initialize(ServerInfo info, bool isPublic)
        {
            calls.Add("initialize");
            return InitializeResult;
        }
        public void Advance(ServerInfo info) { }
        public void Dispose() => calls.Add("dispose");
    }
}
