using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Protocol.Net.Stats;
using Mortz.Server;
using Mortz.Server.Hooks;
using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Mortz.Server.Wins;
using Serilog.Core;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class WinsServiceTests
{
    private readonly RecordingTransport _transport = new();
    private readonly FeatureScope _scope = new("server");
    private readonly Roster _roster = new();
    private readonly WinsService _wins;

    public WinsServiceTests()
    {
        _wins = new WinsService(_scope, _roster, new ReadyLink(_transport), Logger.None);
    }

    [Fact]
    public void WinsAccumulatePerPeer()
    {
        Player seven = _roster.Join(7, "seven");
        _roster.Join(8, "eight");
        Player nine = _roster.Join(9, "nine");

        _wins.Record([seven]);
        _wins.Record([seven]);
        _wins.Record([nine]);

        Assert.Empty(_transport.Messages);
        Dictionary<int, int> table = LoadLobby(seven);
        Assert.Equal(2, table[7]);
        Assert.Equal(1, table[9]);
        Assert.Equal(0, table[8]);
    }

    [Fact]
    public void LeavingDeletesTheTally()
    {
        Player seven = _roster.Join(7, "seven");
        _wins.Record([seven]);
        Assert.Equal(1, LoadLobby(seven)[7]);

        _roster.Leave(7);
        _scope.Forget(seven);

        Assert.Equal(0, LoadLobby(_roster.Join(7, "seven"))[7]);
    }

    [Fact]
    public void OnlyALobbyLoadSendsTheTable()
    {
        Player seven = _roster.Join(7, "seven");

        _wins.Load(seven, new PhaseLoad(ServerPhaseKind.MATCH, 2, Initial: true));

        Assert.Empty(_transport.Messages);
    }

    [Fact]
    public void RecordsTheWinnersOfTheCompletedMatch()
    {
        Player winner = _roster.Join(1, "winner");
        Player victim = _roster.Join(2, "victim");
        using MatchRuntime runtime = new(
            new TerrainMask(128, 128, (_, _) => false, (_, _) => false),
            new MatchConfig
            {
                Rules = new ModeRules
                {
                    EndCondition = new ScoreTargetRules { Target = 1 },
                    SuicidePenalty = SuicidePenalty.REWARD_CLOSEST_ENEMY,
                    SpawnImmunity = 0,
                },
            },
            victoryLapTicks: 3,
            new FeatureScope("match"),
            generation: 2,
            [new SpawnPoint(new Vec2(10, 32)), new SpawnPoint(new Vec2(40, 32))]);
        runtime.Seat(winner);
        runtime.Seat(victim);
        runtime.World.QueueDamage(victim.PeerId, byte.MaxValue);
        MatchUpdate update = runtime.Advance(default);

        _wins.MatchUpdated(update);

        Dictionary<int, int> table = LoadLobby(winner);
        Assert.Equal(1, table[winner.PeerId]);
        Assert.Equal(0, table[victim.PeerId]);
    }

    private Dictionary<int, int> LoadLobby(Player player)
    {
        _wins.Load(player, new PhaseLoad(ServerPhaseKind.LOBBY, 2, Initial: false));
        SessionWinsMsg message = _transport.Messages
            .Select(sent => sent.Message)
            .OfType<SessionWinsMsg>()
            .Last();
        Dictionary<int, int> table = new();
        foreach (PeerWins row in message.Rows)
        {
            table[row.PeerId] = row.Wins;
        }
        return table;
    }
}
