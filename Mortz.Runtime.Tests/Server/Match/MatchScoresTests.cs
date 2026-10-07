using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class MatchScoresTests
{
    private readonly MatchSeats _seats = new();

    private static ModeRules Cfg(bool teams = false,
        SuicidePenalty suicidePenalty = SuicidePenalty.NONE) => new()
        {
            Teams = teams,
            SuicidePenalty = suicidePenalty,
        };

    private MatchScores Scores(ModeRules config) =>
        new(config.ToSnapshot(), _seats.Scope, _seats.Seated);

    private Player Seat(MatchScores scores, int peerId, Team? team = null)
    {
        Player player = _seats.GetOrJoin(peerId);
        scores.Seat(player, team);
        return player;
    }

    [Fact]
    public void ATeamWithTheTeamsRuleOff_IsUnconstructible()
    {
        MatchScores s = Scores(Cfg());
        Player player = _seats.GetOrJoin(1);

        Assert.Throws<ArgumentException>(() => s.Seat(player, Team.BLUE));
    }

    [Fact]
    public void SameTeamKillsAreTeamkills_ButUnassignedKillersAreNot()
    {
        MatchScores s = Scores(Cfg(teams: true));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);
        Player p3 = Seat(s, 3); // never took a side
        Player p4 = Seat(s, 4);

        Assert.Equal(DeathKind.TEAM_KILL,
            s.ScoreDeath(p1.PeerId, p2, p1, null).Kind);
        Assert.Equal(DeathKind.KILL,
            s.ScoreDeath(p3.PeerId, p4, p3, null).Kind);
        Assert.Equal(DeathKind.KILL,
            s.ScoreDeath(p3.PeerId, p2, p3, null).Kind);
    }

    [Fact]
    public void Kill_CreditsTheKiller_AndCountsTheVictimsDeath()
    {
        MatchScores s = Scores(Cfg());
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);

        s.ScoreDeath(p1.PeerId, p2, p1, null);

        Assert.Equal(1, s.Of(p1).Kills);
        Assert.Equal(0, s.Of(p1).Deaths);
        Assert.Equal(0, s.Of(p2).Kills);
        Assert.Equal(1, s.Of(p2).Deaths);
    }

    [Fact]
    public void Suicide_OwnShellOrDeathPit_CountsADeathAndNoKill()
    {
        MatchScores s = Scores(Cfg());
        Player p1 = Seat(s, 1);

        s.ScoreDeath(p1.PeerId, p1, p1, null); // own shell
        s.ScoreDeath(0, p1, null, null); // death pit

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(2, s.Of(p1).Deaths);
    }

    [Fact]
    public void SuicidePenalty_SubtractsAKill_ScoresGoNegative()
    {
        MatchScores s = Scores(Cfg(suicidePenalty: SuicidePenalty.KILL));
        Player p1 = Seat(s, 1);

        s.ScoreDeath(p1.PeerId, p1, p1, null);
        s.ScoreDeath(0, p1, null, null);

        Assert.Equal(-2, s.Of(p1).Kills);
    }

    [Fact]
    public void SuicidePenaltyNoNegative_StopsAtZero()
    {
        MatchScores s = Scores(
            Cfg(teams: true, suicidePenalty: SuicidePenalty.KILL_NO_NEGATIVE));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.RED);

        s.ScoreDeath(p1.PeerId, p2, p1, null); // 1-0
        s.ScoreDeath(p1.PeerId, p1, p1, null); // the point goes back
        s.ScoreDeath(0, p1, null, null); // at zero: nothing to take

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(0, s.TeamKills[Team.BLUE]);
    }

    [Fact]
    public void RewardClosestEnemy_GrantsTheKill_AndReportsIt()
    {
        MatchScores s = Scores(
            Cfg(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY));
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);

        DeathScore result = s.ScoreDeath(p1.PeerId, p1, p1, p2);

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(1, s.Of(p2).Kills);
        Assert.Equal(new KillReward(2, 1), result.Reward);
    }

    [Fact]
    public void RewardClosestEnemy_NobodyEligible_NothingChanges()
    {
        MatchScores s = Scores(
            Cfg(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY));
        Player p1 = Seat(s, 1);

        DeathScore result = s.ScoreDeath(p1.PeerId, p1, p1, null);

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Null(result.Reward);
    }

    [Fact]
    public void Teamkill_AwardsNothing_VictimsDeathStillCounts()
    {
        MatchScores s = Scores(Cfg(teams: true));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);

        s.ScoreDeath(p1.PeerId, p2, p1, null);

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(1, s.Of(p2).Deaths);
        Assert.Equal(0, s.TeamKills[Team.BLUE]);
    }

    [Fact]
    public void TeamTotals_AccumulateAtKillTime_AndSurviveLeavers()
    {
        MatchScores s = Scores(Cfg(teams: true));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.BLUE);
        Player p3 = Seat(s, 3, Team.RED);

        s.ScoreDeath(p1.PeerId, p3, p1, null);
        s.ScoreDeath(p2.PeerId, p3, p2, null);
        Assert.Equal(2, s.TeamKills[Team.BLUE]);

        _seats.Leave(p1); // rage quit keeps the team's points on the board
        Assert.Equal(2, s.TeamKills[Team.BLUE]);
        Assert.DoesNotContain(s.Rows(), row => row.Player.PeerId == 1);
    }

    [Fact]
    public void SuicidePenalty_SubtractsFromTheTeamTotalToo()
    {
        MatchScores s = Scores(
            Cfg(teams: true, suicidePenalty: SuicidePenalty.KILL));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.RED);

        s.ScoreDeath(p1.PeerId, p2, p1, null); // 1-0
        s.ScoreDeath(p1.PeerId, p1, p1, null); // the point goes back

        Assert.Equal(0, s.Of(p1).Kills);
        Assert.Equal(0, s.TeamKills[Team.BLUE]);
    }

    [Fact]
    public void KillerWhoAlreadyLeft_CreditsNobody()
    {
        MatchScores s = Scores(Cfg());
        Player p1 = Seat(s, 1);
        Player p2 = Seat(s, 2);
        _seats.Leave(p1);

        // shell outlived its shooter
        DeathScore result = s.ScoreDeath(p1.PeerId, p2, null, null);

        Assert.Equal(DeathKind.UNCREDITED, result.Kind);
        Assert.Equal(1, s.Of(p2).Deaths);
        Assert.DoesNotContain(s.Rows(), row => row.Player.PeerId == 1);
    }

    [Theory]
    [InlineData(0, DeathKind.FALL)]
    [InlineData(2, DeathKind.SUICIDE)]
    [InlineData(99, DeathKind.UNCREDITED)]
    public void ScoreDeath_ClassifiesUncreditedDeaths(int killerId, DeathKind expected)
    {
        MatchScores s = Scores(Cfg());
        Seat(s, 1);
        Player p2 = Seat(s, 2);

        Player? killer = killerId == p2.PeerId ? p2 : null;
        DeathScore result = s.ScoreDeath(killerId, p2, killer, null);

        Assert.Equal(expected, result.Kind);
        Assert.False(result.CreditedKill);
    }

    [Fact]
    public void ScoreDeath_ReturnsFinalRowsAndTallies()
    {
        MatchScores s = Scores(Cfg(teams: true));
        Player p1 = Seat(s, 1, Team.BLUE);
        Player p2 = Seat(s, 2, Team.RED);

        DeathScore result = s.ScoreDeath(p1.PeerId, p2, p1, null);

        Assert.Equal(DeathKind.KILL, result.Kind);
        Assert.True(result.CreditedKill);
        Assert.Equal(1, result.Killer!.Value.Kills);
        Assert.Equal(1, result.Victim.Deaths);
        Assert.Equal(1, result.TeamKills[Team.BLUE]);
    }
}
