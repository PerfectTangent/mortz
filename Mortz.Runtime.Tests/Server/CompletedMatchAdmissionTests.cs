using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Lobby;
using Mortz.Protocol.Net.Match;
using Mortz.Protocol.Net.Roster;
using Mortz.Protocol.Net.Stats;
using Mortz.Protocol.Replication;
using Mortz.Server.Diagnostics;
using Mortz.Server.Phases;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class CompletedMatchAdmissionTests
{
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(true, 0)]
    public void WaitingJoinersReceiveWinsWhenEnteringTheLobby(bool loadingAtEnd, int victoryLapTicks)
    {
        using TestServer server = Start(new E2EMatchControl(), true, victoryLapTicks);
        if (loadingAtEnd)
        {
            server.Server.Connect(new(9, "waiting", 0, null));
        }
        server.Link.Messages.Clear();

        Finish(server);
        if (!loadingAtEnd)
        {
            server.Server.Connect(new(9, "waiting", 0, null));
        }
        Assert.Contains(server.Link.Messages, sent => sent.Target == 9 && sent.Message is LobbyWaitMsg);
        Assert.DoesNotContain(server.Link.Messages, sent => sent.Message is SessionWinsMsg);

        ReturnToLobby(server);
        server.Ready(7);
        server.Ready(9);

        foreach (int peerId in new[] { 7, 9 })
        {
            SessionWinsMsg wins = Assert.Single(server.Link.Messages
                .Where(sent => sent.Target == peerId)
                .Select(sent => sent.Message).OfType<SessionWinsMsg>());
            Assert.Equal(1, wins.Rows.Single(row => row.PeerId == 7).Wins);
            Assert.Equal(0, wins.Rows.Single(row => row.PeerId == 9).Wins);
        }
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    public void LoadingJoinersNeverEnterACompletedMatch(bool allowJoin, int victoryLapTicks)
    {
        E2EMatchControl control = new();
        using TestServer server = Start(control, allowJoin, victoryLapTicks);
        int generation = server.Server.Generation;
        server.Server.Connect(new(9, "loading", 0, null));
        MatchLoadMsg load = server.Link.Last<MatchLoadMsg>();
        Assert.DoesNotContain(MatchSnapshot.Deserialize(load.InitialSnapshot).Players,
            player => player.Simulation.PeerId == 9);
        server.Link.Messages.Clear();

        Finish(server);

        Assert.Contains(server.Link.Messages, sent => sent.Target == 9 && sent.Message is LobbyWaitMsg);
        Assert.DoesNotContain(server.Link.Messages, sent => sent.Target == 9 && sent.Message is MatchEndMsg);
        Assert.Contains(server.Link.Messages, sent => sent.Target == 7 && sent.Message is MatchEndMsg);
        server.Link.Messages.Clear();
        server.Receive(9, new PhaseReadyMsg(generation));
        server.Receive(9, new PhaseReadyMsg(generation));
        Assert.DoesNotContain(server.Link.Messages, sent => sent.Target == 9 &&
            sent.Message is MatchStartMsg or MatchEndMsg);

        if (victoryLapTicks > 0)
        {
            WorldStateOutcome state = default;
            control.ReadState(value => state = value);
            server.AdvanceWithoutReady();
            Assert.DoesNotContain(state.Players, player => player.PeerId == 9);
            ReturnToLobby(server);
        }
        Assert.Equal(ServerPhaseKind.LOBBY, server.Server.Phase);
        server.Receive(7, new PhaseReadyMsg(server.Server.Generation));
        server.Receive(9, new PhaseReadyMsg(server.Server.Generation));
        Assert.Contains(server.Link.Last<LobbyStateMsg>().Members, member => member.PeerId == 9);
        Assert.Empty(server.Link.Disconnected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewArrivalsWaitWithoutLoadingOrHoldingUpTheLobby(bool allowJoin)
    {
        using TestServer server = Start(new E2EMatchControl(), allowJoin, 3);
        Finish(server);
        server.Link.Messages.Clear();
        server.Server.Connect(new(9, "waiting", 0, null));
        Assert.Contains(server.Link.Messages, sent => sent.Target == 9 && sent.Message is LobbyWaitMsg);
        Assert.DoesNotContain(server.Link.Messages, sent => sent.Target == 9 && sent.Message is MatchLoadMsg);
        server.Receive(9, new PhaseReadyMsg(server.Server.Generation));
        ReturnToLobby(server);
        Assert.Contains(server.Link.Messages, sent => sent.Target == 9 && sent.Message is LobbyLoadMsg);
        server.Ready(9);
        Assert.Contains(server.Link.Last<LobbyStateMsg>().Members, member => member.PeerId == 9);
        Assert.Empty(server.Link.Disconnected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JoinersAdmittedBeforeCompletionReceiveTheResult(bool allowJoin)
    {
        E2EMatchControl control = new();
        using TestServer server = Start(control, allowJoin, 3);
        server.Connect(9, "admitted");
        control.DamagePlayer(7, byte.MaxValue, _ => { });
        Assert.Equal(allowJoin, server.Link.Last<RosterMsg>().Entries.Any(player => player.PeerId == 9));
        server.Link.Messages.Clear();
        Finish(server);
        Assert.Contains(server.Link.Messages, sent => sent.Target is 0 or 9 && sent.Message is MatchEndMsg);
        Assert.DoesNotContain(server.Link.Messages, sent => sent.Message is LobbyWaitMsg);
    }

    [Fact]
    public void ArrivalAfterAZeroLapCompletionLoadsTheNewLobby()
    {
        using TestServer server = Start(new E2EMatchControl(), true, 0);
        Finish(server);
        Assert.Equal(ServerPhaseKind.LOBBY, server.Server.Phase);
        server.Link.Messages.Clear();
        server.Server.Connect(new(9, "arrival", 0, null));
        Assert.Contains(server.Link.Messages, sent => sent.Target == 9 && sent.Message is LobbyLoadMsg);
        Assert.DoesNotContain(server.Link.Messages, sent => sent.Target == 9 && sent.Message is MatchLoadMsg);
    }

    private static TestServer Start(E2EMatchControl control, bool allowJoin, int victoryLapTicks)
    {
        TestServer server = new(control: control, allowJoinInProgress: allowJoin,
            victoryLapTicks: victoryLapTicks, rules: new MatchConfig
            {
                Rules = new ModeRules
                {
                    EndCondition = new TimeLimitRules { Seconds = 1 },
                    Score = ScoreMetric.DEATHS,
                    SpawnImmunity = 0,
                    Respawn = new FixedRespawnRules { Delay = 0 },
                },
            });
        server.Connect(7, "player");
        server.Receive(7, new SetReadyMsg(true));
        server.Tick();
        return server;
    }

    private static void Finish(TestServer server)
    {
        for (int tick = 0; tick < SimConfig.TICK_RATE; tick++)
        {
            server.AdvanceWithoutReady();
        }
    }

    private static void ReturnToLobby(TestServer server)
    {
        for (int tick = 0; tick < 3 && server.Server.Phase == ServerPhaseKind.MATCH; tick++)
        {
            server.AdvanceWithoutReady();
        }
        Assert.Equal(ServerPhaseKind.LOBBY, server.Server.Phase);
    }
}
