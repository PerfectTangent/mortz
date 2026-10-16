using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Replication;
using Mortz.Server.Match.Events;
using Mortz.Server.Match.Modes;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

/// <summary>Owns the ordered match systems and their match-lifetime state.</summary>
public sealed class MatchRuntime
{
    private readonly SortedDictionary<int, Player> _seated = [];
    private readonly ScoringStep _scoring;
    private readonly GameMode _mode;
    private readonly ParticipationStep _participation;
    private readonly GameEventJudge _gameEvents;
    private readonly EndingStep _ending;
    private readonly MatchContext _context;

    public int SeatedPlayerCount => _seated.Count;

    public MatchRuntime(TerrainMask terrain, MatchConfig config, int victoryLapTicks,
        FeatureScope scope, int generation, IReadOnlyList<SpawnPoint>? spawnPoints = null,
        MapZones? zones = null)
    {
        Generation = generation;
        World = new SimWorld(terrain, config, spawnPoints, zones);
        _context = new MatchContext(World, _seated);

        _participation = new ParticipationStep(scope);
        _scoring = new ScoringStep(World.Config.Rules, scope, _seated);
        _mode = new GameMode(World.Config.Rules, World.Config.Rules.Objective.Create(World.Zones));
        _gameEvents = new GameEventJudge(scope, _seated, _scoring.TeamOf);
        _ending = new EndingStep(victoryLapTicks);
    }

    public int Generation { get; }

    public SimWorld World { get; }

    public MatchStage Stage => _context.Stage;

    public ObjectiveState Objective => _mode.Objective;

    public Victor? Winner => _ending.Winner;

    public FinalKillEvent? FinalKill => _ending.FinalKill;

    public MatchConfigSnapshot Config => World.Config;

    public TeamKills TeamKills => _scoring.TeamKills;

    public PlayerScore ScoreOf(Player player) => _scoring.ScoreOf(player);

    public IReadOnlyList<SeatedScore> ScoreRows() => _scoring.Rows();

    /// <summary>Lobby-assigned teams carry into the match (frozen like every
    /// other rule); anyone without one (late joiners) lands on the smallest.</summary>
    public Team? Seat(Player player, Team? lobbyTeam = null)
    {
        EnsurePlaying();
        Team? team = null;
        if (Config.Rules.Teams)
        {
            team = lobbyTeam ?? Teams.Smallest(
                World.Players.Values.Select(member => member.Team));
        }

        World.AddPlayer(player.PeerId, team);
        _seated[player.PeerId] = player;
        _scoring.Seat(player, team);
        _participation.Seat(player);
        return team;
    }

    public void AddJipSpectator(Player player)
    {
        EnsurePlaying();
        _participation.AddJipSpectator(player);
    }

    public MatchParticipation ParticipationOf(Player player) =>
        _participation.Of(player);

    public PlayerPresentationState PresentationOf(in PlayerState simulation)
    {
        int peerId = simulation.PeerId;
        Player member = _seated[peerId];
        PlayerStats stats = World.Stats[peerId];

        byte spreeMagnitude = _gameEvents.KillingSpreeMagnitude(member);
        bool isBleeding = simulation.IsAtCriticalHealth(stats.MaxHealth);

        return new PlayerPresentationState(spreeMagnitude, isBleeding);
    }

    public void Remove(Player player)
    {
        World.RemovePlayer(player.PeerId);
        _seated.Remove(player.PeerId);
        _gameEvents.PlayerLeft(player);
    }

    public void EnqueueInput(Player player, int seq, PlayerInput input)
    {
        if (Stage == MatchStage.PLAYING)
        {
            World.EnqueueInput(player.PeerId, seq, input);
        }
    }

    public MatchUpdate Advance(ServerTime time)
    {
        if (Stage == MatchStage.VICTORY_LAP)
        {
            return new MatchUpdate
            {
                Tick = World.Tick,
                Time = time,
                ReturnToLobby = _ending.AdvanceVictoryLap(_context),
            };
        }

        World.Step();
        GameModeUpdate mode = _mode.Advance(_context, _scoring, World.Deaths);
        IReadOnlyList<MatchParticipationChange> participation =
            _participation.Apply(_context, World.Deaths);
        IReadOnlyList<Judgment> judgments = _gameEvents.JudgeFrame(mode.Eliminations, World.Tick);
        _ending.Apply(_context, mode.Outcome);
        return new MatchUpdate
        {
            Tick = World.Tick,
            Time = time,
            MortarEvents = [.. World.MortarEvents],
            Explosions = [.. World.Explosions],
            ShellRetirements = [.. World.ShellRetirements],
            Deaths = [.. World.Deaths],
            Eliminations = [.. mode.Eliminations],
            MatchPoint = mode.MatchPoint,
            GameEvents = [.. judgments],
            ParticipationChanges = [.. participation],
            MatchEnded = mode.Outcome?.Winner,
            Winners = mode.Outcome?.Winner is Victor victor ? [.. Winners(victor)] : [],
            FinalKill = mode.Outcome?.FinalKill,
            ReturnToLobby = _ending.ReturnToLobby,
            ModifierChanges = [.. World.DrainModifierChanges()],
        };
    }

    /// <summary>Players credited with the win: the winner itself, or everyone
    /// still in the match on the winning team.</summary>
    public Player[] Winners(Victor victor) => victor switch
    {
        Victor.Team team =>
        [
            .. _seated.Values
                .Where(member => _scoring.TeamOf(member) == team.Value)
        ],
        Victor.Player player => _seated.GetValueOrDefault(player.PeerId) is Player winner
            ? [winner]
            : [],
        _ => throw new ArgumentOutOfRangeException(nameof(victor)),
    };

    private void EnsurePlaying()
    {
        if (Stage != MatchStage.PLAYING)
        {
            throw new InvalidOperationException("The completed match is closed to new participants.");
        }
    }
}
