using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Server.Match.Scoring.SuicidePenalties;
using Mortz.Server.Players;

namespace Mortz.Server.Match.Scoring;

public sealed class MatchScores(
    ModeRulesSnapshot config,
    FeatureScope scope,
    IReadOnlyDictionary<int, Player> seated)
{
    private readonly PlayerTable<Player, ScoreState> _scores = scope.Table<Player, ScoreState>();
    private readonly SuicidePenaltyStrategy _suicidePenalty = SuicidePenaltyStrategy.Create(config.SuicidePenalty);
    private TeamKills _teamKills;

    public TeamKills TeamKills => _teamKills;
    public TeamDeaths TeamDeaths { get; private set; }

    public ScoreState Of(Player player) => _scores[player];

    public void Seat(Player player, Team? team)
    {
        if (team != null && !config.Teams)
            throw new ArgumentException("Team assignment with the Teams rule off.", nameof(team));
        _scores[player].Team = team;
    }

    /// <summary>Applies one death to the individual and team tallies.
    /// A death pit or the victim's own shell is a suicide: a death, never a
    /// kill, then whatever SuicidePenalty says. A killer who already left
    /// credits nobody; a teamkill awards nothing.</summary>
    public DeathScore ScoreDeath(int killerId, Player victim, Player? killer,
        Player? nearestEnemy)
    {
        ScoreState victimRow = Of(victim);
        victimRow.Deaths++;
        if (victimRow.Team is Team victimTeam)
            TeamDeaths = TeamDeaths.Add(victimTeam);

        DeathKind kind;
        KillReward? reward = null;
        if (killerId == 0)
        {
            kind = DeathKind.FALL;
            reward = _suicidePenalty.Apply(victim, nearestEnemy, this);
        }
        else if (killerId == victim.PeerId)
        {
            kind = DeathKind.SUICIDE;
            reward = _suicidePenalty.Apply(victim, nearestEnemy, this);
        }
        else if (killer == null)
        {
            kind = DeathKind.UNCREDITED;
        }
        else if (Teams.SameSide(Of(killer).Team, victimRow.Team))
        {
            kind = DeathKind.TEAM_KILL;
        }
        else
        {
            kind = DeathKind.KILL;
            AddKills(killer, +1);
        }

        return new DeathScore(
            killerId,
            victim.PeerId,
            kind,
            killer == null ? null : Of(killer).Snapshot,
            victimRow.Snapshot,
            reward,
            _teamKills);
    }

    public int AddKills(Player player, int delta)
    {
        ScoreState row = Of(player);
        row.Kills += delta;
        if (row.Team is Team team)
            _teamKills = _teamKills.Add(team, delta);
        return row.Kills;
    }

    /// <summary>Snapshot of every seated player's row, in ascending peer id.</summary>
    public IReadOnlyList<SeatedScore> Rows()
    {
        List<SeatedScore> rows = new(seated.Count);
        foreach (Player player in seated.Values)
        {
            rows.Add(new SeatedScore(player, Of(player).Snapshot));
        }
        return rows;
    }

}
