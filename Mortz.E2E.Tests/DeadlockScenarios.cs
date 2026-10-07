using System.Diagnostics;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

public class DeadlockScenarios
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly Vec2 _leftControl = new(500, 1344);
    private static readonly Vec2 _rightControl = new(1500, 1344);
    private static readonly Vec2 _outside = new(1200, 320);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ControlBaySharesCaptureAcrossZonesAndBothClientsObserveVictory(bool allowJoinInProgress)
    {
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = $"{nameof(ControlBaySharesCaptureAcrossZonesAndBothClientsObserveVictory)}_{allowJoinInProgress}",
            MapId = "control-bay",
            Mode = "deadlock",
            Players = ["one", "two"],
            AllowJoinInProgress = allowJoinInProgress,
        }, Cancellation);
        await scenario.ReadyAllAsync(Cancellation);
        foreach (ClientDriver client in scenario.Clients)
        {
            await client.Events.WaitAsync<SnapshotObservedEvent>(_ => true, scenario.Budget.MatchEvent,
                cancellationToken: Cancellation);
        }
        ClientStateResponse first = await scenario.Client("one").StateAsync(Cancellation);
        ClientDriver blue = scenario.Client(first.Team == Team.BLUE ? "one" : "two");
        ClientDriver red = scenario.Client(first.Team == Team.RED ? "one" : "two");
        Assert.Equal(Team.BLUE, (await blue.StateAsync(Cancellation)).Team);
        Assert.Equal(Team.RED, (await red.StateAsync(Cancellation)).Team);

        await scenario.Server.PlacePlayerAsync(blue.PeerId, _leftControl, Cancellation);
        await blue.RunPlanAsync(BotInputPlan.Sequence(
            new BotInputFrame(InputButtons.RIGHT, 0, ticks: 30)), Cancellation);
        await WaitServer(scenario, state => state.Players.Single(player => player.PeerId == blue.PeerId).Position.X > 530);
        await WaitServer(scenario, state => state.Objective is { BlueProgress: >= 600, RedProgress: 0 });
        await scenario.Server.PlacePlayerAsync(red.PeerId, _rightControl, Cancellation);
        ServerStateResponse locked = await WaitServer(scenario, state => state.Objective is { IsDeadlocked: true });
        Assert.True(locked.Objective!.BlueProgress > locked.Objective.RedProgress);
        foreach (ClientDriver client in scenario.Clients)
        {
            ClientStateResponse observed = await WaitClient(scenario, client, state => state.Objective is { IsDeadlocked: true });
            Assert.Equal(locked.Objective, observed.Objective);
        }

        await scenario.AddClientAsync("late", Cancellation);
        ClientDriver late = scenario.Client("late");
        await late.Events.WaitAsync<SnapshotObservedEvent>(_ => true, scenario.Budget.MatchEvent,
            cancellationToken: Cancellation);
        ClientStateResponse joined = await WaitClient(scenario, late, state => state.Objective is { IsDeadlocked: true });
        Assert.Equal(locked.Objective, joined.Objective);
        Assert.Equal(allowJoinInProgress ? MatchSeat.PLAYER : MatchSeat.SPECTATOR, joined.Participation!.Value.Seat);

        await scenario.Server.PlacePlayerAsync(red.PeerId, _outside, Cancellation);
        ServerStateResponse grace = await WaitServer(scenario,
            state => state.Objective is { RedOccupied: false, RedGraceTicks: > 0 });
        Assert.Equal(locked.Objective.BlueProgress, grace.Objective!.BlueProgress);
        await WaitClient(scenario, blue, state => state.Objective is { RedGraceTicks: > 0 });
        ServerStateResponse decaying = await WaitServer(scenario,
            state => state.Objective is { RedDecaying: true } objective && objective.RedProgress < locked.Objective.RedProgress - 100);
        Assert.True(decaying.Objective!.BlueProgress > locked.Objective.BlueProgress);
        await WaitClient(scenario, red, state => state.Objective is { RedDecaying: true });

        await scenario.Server.PlacePlayerAsync(red.PeerId, _rightControl, Cancellation);
        ServerStateResponse relocked = await WaitServer(scenario, state => state.Objective is { IsDeadlocked: true });
        Assert.True(relocked.Objective!.BlueProgress > locked.Objective.BlueProgress);

        Assert.True((await scenario.Server.DamagePlayerAsync(blue.PeerId, 255, Cancellation)).Died);
        Assert.True((await scenario.Server.DamagePlayerAsync(red.PeerId, 255, Cancellation)).Died);
        await WaitServer(scenario, state => state.Objective is
        { BlueOccupied: false, RedOccupied: false, BlueDecaying: true, RedDecaying: true });
        await WaitServer(scenario, state => state.Players.All(player => player.Health > 0));
        foreach (ClientDriver client in new[] { blue, red })
        {
            await WaitClient(scenario, client, state => state.Participation is { Activity: MatchActivity.ACTIVE });
        }

        E2EEventCursor ending = scenario.Server.Events.Cursor;
        await scenario.Server.PlacePlayerAsync(blue.PeerId, _leftControl, Cancellation);
        MatchEndedEvent winner = await scenario.Server.Events.WaitAsync<MatchEndedEvent>(
            _ => true, scenario.Budget.MatchEvent, ending, Cancellation);
        Assert.Equal(E2EVictorKind.TEAM, winner.Kind);
        Assert.Equal((int)Team.BLUE, winner.VictorId);
        ServerStateResponse final = await scenario.Server.StateAsync(Cancellation);
        foreach (ClientDriver client in scenario.Clients)
        {
            ClientStateResponse observed = await WaitClient(scenario, client,
                state => state.Objective is { Capture: Team.BLUE });
            Assert.Equal(final.Objective, observed.Objective);
            Assert.Equal(winner.Tick, observed.ObjectiveTick);
            Assert.False(client.Process.LogContains("no handler for message id"));
        }
        await scenario.AddClientAsync("after", Cancellation);
        ClientDriver after = scenario.Client("after");
        await scenario.Server.Events.WaitAsync<PhaseChangedEvent>(
            value => value.Phase == E2EPhase.LOBBY, scenario.Budget.MatchEvent, ending, Cancellation);
        await after.Events.WaitAsync<LobbyRosterObservedEvent>(value => value.PlayerCount == 4,
            scenario.Budget.MatchEvent, cancellationToken: Cancellation);
        Assert.False(after.Process.LogContains("\"$event\":\"match_entered\""));
        Assert.Equal(4, (await scenario.Server.StateAsync(Cancellation)).Players.Length);
    }

    [Fact]
    public async Task DeadlockRespawnsEscalateAndStopAtTheConfiguredCeiling()
    {
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(DeadlockRespawnsEscalateAndStopAtTheConfiguredCeiling),
            MapId = "control-bay",
            Mode = null,
            Players = ["one", "two"],
            RulesetPath = Path.Combine(RepoRoot.Path, "Mortz.E2E.Tests", "Rulesets", "deadlock_respawns.toml"),
        }, Cancellation);
        await scenario.ReadyAllAsync(Cancellation);
        ClientDriver target = scenario.Client("two");
        await target.Events.WaitAsync<SnapshotObservedEvent>(_ => true, scenario.Budget.MatchEvent,
            cancellationToken: Cancellation);
        foreach (int expectedTicks in new[] { 30, 60, 90, 90 })
        {
            PlayerDamagedResponse death = await scenario.Server.DamagePlayerAsync(target.PeerId, 255, Cancellation);
            Assert.True(death.Died);
            ClientStateResponse waiting = await WaitClient(scenario, target,
                state => state.Participation is { Reason: SpectateReason.RESPAWN });
            Assert.Equal(expectedTicks, waiting.Participation!.Value.ReturnTick - death.AppliedTick);
            ServerStateResponse dead = await scenario.Server.StateAsync(Cancellation);
            Assert.InRange(dead.Players.Single(player => player.PeerId == target.PeerId).RespawnTicks, 1, expectedTicks);
            await WaitServer(scenario, state => state.Players.Single(player => player.PeerId == target.PeerId).Health > 0);
            await WaitClient(scenario, target, state => state.Participation is { Activity: MatchActivity.ACTIVE });
        }
    }

    private static Task<ServerStateResponse> WaitServer(MortzScenario scenario, Func<ServerStateResponse, bool> predicate) =>
        Wait(() => scenario.Server.StateAsync(Cancellation), predicate, scenario.Budget.MatchEvent);

    private static Task<ClientStateResponse> WaitClient(MortzScenario scenario, ClientDriver client,
        Func<ClientStateResponse, bool> predicate) =>
        Wait(() => client.StateAsync(Cancellation), predicate, scenario.Budget.MatchEvent);

    private static async Task<T> Wait<T>(Func<Task<T>> read, Func<T, bool> predicate, TimeSpan timeout)
    {
        Stopwatch clock = Stopwatch.StartNew();
        T latest;
        do
        {
            latest = await read();
            if (predicate(latest))
            {
                return latest;
            }
            await Task.Delay(100, Cancellation);
        } while (clock.Elapsed < timeout);
        Assert.Fail($"Live state did not reach the expected condition: {latest}");
        return latest;
    }
}
