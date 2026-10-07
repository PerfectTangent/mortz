using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public sealed partial class ModeRules
{
    [UiCategory("Mode")]
    [UiProperty("Teams")]
    [MatchRule]
    public bool Teams { get; set; }

    public EndConditionRules EndCondition { get; set; } = new ScoreTargetRules();

    public ObjectiveRules Objective { get; set; } = new NoObjectiveRules();

    public RespawnRules Respawn { get; set; } = new FixedRespawnRules();

    [UiProperty("Score")]
    [MatchRule]
    public ScoreMetric Score { get; set; } = ScoreMetric.KILLS;

    [UiProperty("Final Replay")]
    [MatchRule]
    public bool FinalReplay { get; set; } = true;

    // Self-damage always applies.
    [UiProperty("Friendly Fire")]
    [UiVisibleWhen(nameof(FriendlyFireIsRelevant))]
    [MatchRule]
    public bool FriendlyFire { get; set; } = true;

    [UiProperty("Suicide Penalty")]
    [MatchRule]
    public SuicidePenalty SuicidePenalty { get; set; } = SuicidePenalty.NONE;

    [UiProperty("Spectate During Respawn")]
    [MatchRule]
    public bool SpectateDuringRespawn { get; set; } = true;

    [UiCategory("Spawn")]
    [UiProperty("Spawn Immunity", min: 0, max: 4, step: 0.05f)]
    [MatchRule(min: 0, max: 4)]
    public float SpawnImmunity { get; set; } = SimConfig.SPAWN_IMMUNITY;

    public bool FriendlyFireIsRelevant => Teams;

    public IReadOnlyList<string> Errors()
    {
        List<string> errors = [.. EndCondition.Errors(this), .. Objective.Errors(this), .. Respawn.Errors(this)];
        if (EndCondition is NoEndConditionRules && Objective is NoObjectiveRules)
        {
            errors.Add("rules.end_condition 'none' requires an objective, or the match can never end.");
        }
        return errors;
    }
}

public sealed partial record ModeRulesSnapshot
{
    public int SpawnImmunityTicks => (int)(SpawnImmunity * SimConfig.TICK_RATE);
}
