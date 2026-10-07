using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Server.Match.Scoring;

namespace Mortz.Server.Match.Modes;

/// <summary>Turns one playing tick into scored deaths and, once decided, the match outcome.</summary>
public class GameMode(ModeRulesSnapshot rules, MatchObjective objective)
{
    private readonly bool _teams = rules.Teams;
    private readonly ScoreMetric _metric = rules.Score;
    private readonly EndConditionRulesSnapshot _end = rules.EndCondition;
    private readonly bool _finalReplay = rules.FinalReplay;

    public ObjectiveState Objective => objective.State;

    /// <summary>A completed objective wins outright. Otherwise the end condition
    /// is checked against the scores from before this tick's deaths (timer and
    /// roster changes), then after each death. Every death is scored either way.</summary>
    public GameModeUpdate Advance(MatchContext match, ScoringStep scoring, IReadOnlyList<Death> deaths)
    {
        SimWorld world = match.World;
        objective.Advance(world);
        MatchOutcome? outcome = null;
        if (objective.Winner is { } captured)
            outcome = new MatchOutcome(captured);
        else if (_end.Winner(world.Tick, Scores(scoring)) is { } early)
            outcome = new MatchOutcome(early);

        List<ScoredKill> eliminations = [];
        foreach (Death death in deaths)
        {
            if (scoring.ScoreDeath(match, death) is not ScoredKill kill)
                continue;
            eliminations.Add(kill);
            if (outcome != null || _end.Winner(world.Tick, Scores(scoring)) is not { } winner)
                continue;
            FinalKillEvent? replay = _finalReplay
                ? FinalKillEvent.Capture(world.Tick, kill, death, world.Explosions)
                : null;
            outcome = new MatchOutcome(winner, replay);
        }

        MatchPoint? matchPoint = outcome == null ? _end.MatchPoint(Scores(scoring)) : null;
        return new GameModeUpdate(eliminations, matchPoint, outcome);
    }

    private Scores Scores(ScoringStep scoring)
    {
        IReadOnlyList<SeatedScore> rows = scoring.Rows();
        bool kills = _metric == ScoreMetric.KILLS;
        ContenderScore[] values = _teams
            ? Teams.All
                .Where(team => rows.Any(row => row.Score.Team == team))
                .Select(team => new ContenderScore(new Victor.Team(team),
                    kills ? scoring.TeamKills[team] : scoring.TeamDeaths[team]))
                .ToArray()
            : rows
                .Select(row => new ContenderScore(new Victor.Player(row.Player.PeerId),
                    kills ? row.Score.Kills : row.Score.Deaths))
                .ToArray();
        return new Scores(values, _metric.HigherIsBetter());
    }
}

/// <summary>No final-kill replay is implied by a winner or a death on the ending tick.</summary>
public record MatchOutcome(Victor Winner, FinalKillEvent? FinalKill = null);

public readonly record struct GameModeUpdate(
    IReadOnlyList<ScoredKill> Eliminations, MatchPoint? MatchPoint, MatchOutcome? Outcome);
