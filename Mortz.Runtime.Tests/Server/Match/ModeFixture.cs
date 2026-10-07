using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Modes;
using Mortz.Server.Players;

namespace Mortz.Runtime.Tests.Server.Match;

/// <summary>A GameMode over a bare world, fed hand-built deaths.</summary>
public sealed class ModeFixture
{
    private readonly MatchCells _cells = new();

    public ModeFixture(ModeRules rules, MatchObjective? objective = null)
    {
        rules.SpawnImmunity = 0;
        TerrainMask terrain = new(128, 128, (_, _) => false, (_, _) => false);
        World = new SimWorld(terrain, new MatchConfig { Rules = rules }, Array.Empty<SpawnPoint>());
        Context = new MatchContext(World, _cells.Seated);
        Mode = new GameMode(World.Config.Rules, objective ?? NoObjective.Instance);
        Scoring = new ScoringStep(World.Config.Rules, _cells.Keys, _cells.Seated);
    }

    public SimWorld World { get; }

    public MatchContext Context { get; }

    public ScoringStep Scoring { get; }

    public GameMode Mode { get; }

    public Player Player(int peerId) => _cells.GetOrJoin(peerId);

    public void Seat(int peerId, Team? team = null)
    {
        Player player = Player(peerId);
        World.AddPlayer(peerId, team);
        Scoring.Seat(player, team);
    }

    public Death Death(int victimId, int killerId) =>
        new(victimId, World.Players[victimId].Position, killerId, false, ShellId: -1);

    public GameModeUpdate Advance(params Death[] deaths) => Mode.Advance(Context, Scoring, deaths);
}
