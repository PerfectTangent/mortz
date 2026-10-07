using Mortz.Core.Match.Teams;

namespace Mortz.Server.Match.Scoring;

/// <summary>One player's scoreboard row.</summary>
public sealed class ScoreState
{
    public Team? Team { get; set; }

    public int Kills { get; set; }

    public int Deaths { get; set; }

    public PlayerScore Snapshot => new(Team, Kills, Deaths);
}
