using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

[ConfigVariant("score_target", "Score Target", typeof(ScoreTargetRules))]
[ConfigVariant("score_lead", "Score Lead", typeof(ScoreLeadRules))]
[ConfigVariant("time_limit", "Time Limit", typeof(TimeLimitRules))]
[ConfigVariant("objective_completion", "Objective Completion", typeof(ObjectiveCompletionRules))]
public abstract class EndConditionRules
{
    public virtual bool SuppliesQualifier => false;

    public abstract EndConditionRulesSnapshot ToSnapshot();
}

public sealed partial class ScoreTargetRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Score Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int Target { get; set; } = 5;
}

public sealed partial class ScoreLeadRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Score Lead Target", min: 1, max: 999)]
    [MatchRule(min: 1, max: 999)]
    public int Target { get; set; } = 3;
}

public partial class TimeLimitRules : EndConditionRules
{
    [UiCategory("Victory")]
    [UiProperty("Time Limit (seconds)", min: 1, max: 86400)]
    [MatchRule(min: 1, max: 86400)]
    public float Seconds { get; set; } = 180;
}

public partial class ObjectiveCompletionRules : EndConditionRules
{
    public override bool SuppliesQualifier => true;
}
