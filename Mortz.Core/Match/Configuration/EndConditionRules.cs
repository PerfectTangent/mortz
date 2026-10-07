using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

[ConfigVariant("score_target", "Score Target", typeof(ScoreTargetRules))]
[ConfigVariant("score_lead", "Score Lead", typeof(ScoreLeadRules))]
[ConfigVariant("time_limit", "Time Limit", typeof(TimeLimitRules))]
[ConfigVariant("none", "None", typeof(NoEndConditionRules))]
public abstract class EndConditionRules
{
    public abstract EndConditionRulesSnapshot ToSnapshot();

    public virtual IEnumerable<string> Errors(ModeRules rules) => [];
}

public sealed partial class ScoreTargetRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Score Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int Target { get; set; } = 5;
}

public sealed partial record ScoreTargetRulesSnapshot
{
    public override Victor? Winner(int tick, Scores scores) =>
        scores.Values.Any(score => score.Value >= Target) ? scores.Best() : null;

    public override MatchPoint? MatchPoint(Scores scores)
    {
        if (!scores.HigherIsBetter)
            return null;
        int best = 0;
        Victor? leader = null;
        foreach (ContenderScore score in scores.Values)
        {
            if (score.Value <= best)
                continue;
            best = score.Value;
            leader = score.Contender;
        }
        return Target - best == 1 ? new MatchPoint(1, leader) : null;
    }
}

public sealed partial class ScoreLeadRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Score Lead Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int Target { get; set; } = 3;
}

public sealed partial record ScoreLeadRulesSnapshot
{
    public override Victor? Winner(int tick, Scores scores)
    {
        LeadRace race = Race(scores);
        return race.Remaining == 0 ? race.Leader : null;
    }

    public override MatchPoint? MatchPoint(Scores scores)
    {
        if (!scores.HigherIsBetter)
            return null;
        LeadRace race = Race(scores);
        return race.Remaining == 1 ? new MatchPoint(1, race.Leader) : null;
    }

    private LeadRace Race(Scores scores)
    {
        LeadRace race = new(Target);
        foreach (ContenderScore score in scores.Values)
        {
            race.Offer(score.Contender, scores.HigherIsBetter ? score.Value : -score.Value);
        }
        return race;
    }
}

public partial class TimeLimitRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Time Limit (seconds)", min: 1, max: 86400)]
    [MatchRule(min: 1, max: 86400)]
    public float Seconds { get; set; } = 180;
}

public sealed partial record TimeLimitRulesSnapshot
{
    public int Ticks => Math.Max(1, (int)Math.Ceiling(Seconds * SimConfig.TICK_RATE));

    /// <summary>A tie keeps the match going until someone pulls ahead.</summary>
    public override Victor? Winner(int tick, Scores scores) => tick >= Ticks ? scores.Best() : null;
}

/// <summary>Only an objective can end the match.</summary>
public partial class NoEndConditionRules : EndConditionRules;

public sealed partial record NoEndConditionRulesSnapshot
{
    public override Victor? Winner(int tick, Scores scores) => null;
}
