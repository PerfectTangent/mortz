using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Runtime.Tests.Core;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class DeadlockMatchTests
{
    public static MatchConfig Config() => new()
    {
        Rules = new ModeRules
        {
            Teams = true,
            Objective = new DeadlockObjectiveRules { CaptureSpeed = 100, DecaySpeed = 100, AbsenceDelay = 0 },
            EndCondition = new ObjectiveCompletionRules(),
            Winner = WinnerRule.QUALIFYING,
            Evaluation = EvaluationTiming.TICK,
            Replay = ReplayRule.NONE,
            Respawn = new FixedRespawnRules { Delay = 0 },
            SpawnImmunity = 0,
            SuicidePenalty = SuicidePenalty.REWARD_CLOSEST_ENEMY,
        },
    };

    public static MapZones Zones() => new([
        new MapZone("left", ["control"], ZoneShape.Rect(20, 200, 80, 50), null),
        new MapZone("left overlap", ["control"], ZoneShape.Rect(30, 210, 50, 40), null),
        new MapZone("right", ["control"], ZoneShape.Rect(280, 200, 80, 50), null),
        new MapZone("unrelated", [], ZoneShape.Rect(150, 200, 80, 50), null),
    ]);

    [Fact]
    public void SeparateZonesShareOneBarAndOverlapsOrExtraPlayersDoNotAccelerateIt()
    {
        using Fixture f = new();
        f.Runtime.World.Teleport(1, new Vec2(50, 250));
        f.Runtime.World.Teleport(2, new Vec2(310, 250));
        f.Runtime.Seat(f.Open(3), Team.BLUE);
        f.Runtime.World.Teleport(3, new Vec2(60, 250));
        f.Runtime.AddJipSpectator(f.Open(4));
        f.Step();
        Assert.Equal(100, f.State.BlueProgress);
        Assert.Equal(100, f.State.RedProgress);
        Assert.True(f.State.BlueOccupied);
        Assert.True(f.State.RedOccupied);
        f.Step(29);
        Assert.True(f.State.IsDeadlocked);
        Assert.Equal(3000, f.State.BlueProgress);
        Assert.Equal(3000, f.State.RedProgress);
        Assert.Null(f.Runtime.Winner);
    }

    [Fact]
    public void BodyCenterControlsOccupancyAndSpawnProtectionStillCounts()
    {
        MatchConfig config = Config();
        config.Rules.SpawnImmunity = 1;
        using Fixture f = new(config);
        f.Runtime.World.Teleport(1, new Vec2(50, 250));
        f.Runtime.World.Teleport(2, new Vec2(200, 250));
        f.Step();
        Assert.True(f.Runtime.World.Players[1].SpawnImmunityTicks > 0);
        Assert.True(f.State.BlueOccupied);
        Assert.False(f.State.RedOccupied);
        // The feet are inside the region, but the body center is above it.
        f.Runtime.World.Teleport(1, new Vec2(50, 200));
        f.Step();
        Assert.False(f.State.BlueOccupied);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("disconnect")]
    [InlineData("death")]
    public void LosingTheLastOccupantStartsTheSameAbsenceGrace(string cause)
    {
        MatchConfig config = Config();
        ((DeadlockObjectiveRules)config.Rules.Objective).AbsenceDelay = 2f / SimConfig.TICK_RATE;
        config.Rules.Respawn = new NoRespawnRules();
        using Fixture f = new(config);
        f.Runtime.World.Teleport(1, new Vec2(50, 250));
        f.Runtime.World.Teleport(2, new Vec2(200, 250));
        f.Step(3);
        if (cause == "leave")
        {
            f.Runtime.World.Teleport(1, new Vec2(200, 250));
        }
        else if (cause == "disconnect")
        {
            f.Runtime.Remove(f.Blue);
        }
        else
        {
            f.Runtime.World.QueueDamage(1, byte.MaxValue);
        }
        f.Step();
        Assert.False(f.State.BlueOccupied);
        Assert.Equal(300, f.State.BlueProgress);
        Assert.Equal(2, f.State.BlueGraceTicks);
        f.Step();
        Assert.Equal(300, f.State.BlueProgress);
        f.Step();
        Assert.Equal(200, f.State.BlueProgress);
        Assert.True(f.State.BlueDecaying);
    }

    [Fact]
    public void ADefenderKilledOnTheCaptureTickCannotContestAndKillsDoNotDecideTheWinner()
    {
        using Fixture f = new();
        f.Runtime.World.Teleport(2, new Vec2(200, 250));
        for (int death = 0; death < 3; death++)
        {
            f.Runtime.World.QueueDamage(1, byte.MaxValue);
            f.Step();
            f.Step();
        }
        Assert.Equal(3, f.Runtime.TeamKills.Red);
        f.Runtime.World.Teleport(1, new Vec2(50, 250));
        f.Step(59);
        Assert.Null(f.Runtime.Winner);
        f.Runtime.World.Teleport(2, new Vec2(310, 250));
        f.Runtime.World.QueueDamage(2, byte.MaxValue);
        MatchUpdate ended = f.Runtime.Advance(default);
        Assert.False(f.State.RedOccupied);
        Assert.Equal(Team.BLUE, f.State.CapturedBy);
        Assert.Equal(new Victor.Team(Team.BLUE), ended.MatchEnded);
        Assert.True(f.Runtime.TeamKills.Blue < f.Runtime.TeamKills.Red);
        Assert.Null(ended.FinalKill);
        Assert.Equal(-1, ended.Standing.Remaining);
        DeadlockObjectiveState final = f.State;
        f.Step(2);
        Assert.Same(final, f.State);
    }

    [Theory]
    [InlineData("teams")]
    [InlineData("end_condition")]
    [InlineData("winner")]
    [InlineData("evaluation")]
    [InlineData("replay")]
    public void InvalidCompositionFailsAtEveryEntryPoint(string field)
    {
        MatchConfig config = Config();
        switch (field)
        {
            case "teams": config.Rules.Teams = false; break;
            case "end_condition": config.Rules.EndCondition = new ScoreTargetRules(); break;
            case "winner": config.Rules.Winner = WinnerRule.HIGHEST_SCORE; break;
            case "evaluation": config.Rules.Evaluation = EvaluationTiming.ELIMINATION; break;
            case "replay": config.Rules.Replay = ReplayRule.DECISIVE_ELIMINATION; break;
        }
        Assert.Contains(ModeComposition.Errors(config.Rules), error => error.Contains(field));
        Assert.Throws<InvalidDataException>(() => MatchConfigCodec.FromBytes(config.ToBytes()));
        Assert.Null(TomlModel.Read<RulesetManifest>(TomlModel.Write(new RulesetManifest { Rules = config.Rules })).Value);
        Assert.Throws<ArgumentException>(() => new MatchRuntime(TestWorlds.Flat(), config, 0, new MatchStateKeys(1), zones: Zones()));
    }

    [Fact]
    public void MissingControlZonesFailAtConstructionWithoutInvalidatingOtherModes()
    {
        Assert.Contains("control", Assert.Throws<ArgumentException>(() =>
            new MatchRuntime(TestWorlds.Flat(), Config(), 0, new MatchStateKeys(1))).Message);
        MapZones untagged = new([new MapZone("control", ["deadlock"], ZoneShape.Rect(0, 0, 400, 300), null)]);
        Assert.Contains("control", Assert.Throws<ArgumentException>(() =>
            new MatchRuntime(TestWorlds.Flat(), Config(), 0, new MatchStateKeys(1), zones: untagged)).Message);
        using MatchRuntime deathmatch = new(TestWorlds.Flat(), new MatchConfig(), 0, new MatchStateKeys(1));
        Assert.IsType<NoObjectiveState>(deathmatch.Objective);
    }

    private class Fixture : IDisposable
    {
        private readonly MatchStateKeys _keys = new(1);
        public MatchRuntime Runtime { get; }
        public Player Blue { get; }
        public DeadlockObjectiveState State => Assert.IsType<DeadlockObjectiveState>(Runtime.Objective);

        public Fixture(MatchConfig? config = null)
        {
            Runtime = new MatchRuntime(TestWorlds.Flat(), config ?? Config(), 2, _keys,
                [new SpawnPoint(new Vec2(200, 250), Team.BLUE), new SpawnPoint(new Vec2(210, 250), Team.RED)], Zones());
            _keys.Seal();
            Blue = Open(1);
            Runtime.Seat(Blue, Team.BLUE);
            Runtime.Seat(Open(2), Team.RED);
        }

        public Player Open(int id)
        {
            Player player = new(id, $"Player {id}", 0, 1);
            player.OpenMatch(_keys.Count, _keys.Generation);
            return player;
        }

        public void Step(int ticks = 1)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                Runtime.Advance(default);
            }
        }

        public void Dispose() => Runtime.Dispose();
    }
}
