using Mortz.Core.Match.Objectives;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public partial class DeadlockObjectiveRules : ObjectiveRules
{
    public const string CONTROL_TAG = "control";
    public const string MAP_ERROR =
        "Deadlock requires at least one map zone tagged 'control'. Choose a map with control zones.";

    [UiCategory("Capture")]
    [UiProperty("Capture Speed (%/second)", min: 1, max: 100)]
    [MatchRule(min: 1, max: 100)]
    public int CaptureSpeed { get; set; } = 5;

    [UiProperty("Decay Speed (%/second)", min: 1, max: 100)]
    [MatchRule(min: 1, max: 100)]
    public int DecaySpeed { get; set; } = 5;

    [UiProperty("Absence Delay", min: 0, max: 60, step: 0.05f)]
    [MatchRule(min: 0, max: 60)]
    public float AbsenceDelay { get; set; } = 2;

    public override IEnumerable<string> Errors(ModeRules rules) => rules.Teams
        ? []
        : ["rules.objective 'deadlock' requires rules.teams = true."];

    public override string? MapError(MapZones zones) =>
        zones.All.Any(zone => zone.HasTag(CONTROL_TAG)) ? null : MAP_ERROR;
}

public sealed partial record DeadlockObjectiveRulesSnapshot
{
    public int AbsenceDelayTicks => (int)(AbsenceDelay * SimConfig.TICK_RATE);

    public override MatchObjective Create(MapZones zones) => new DeadlockObjective(this, zones);
}
