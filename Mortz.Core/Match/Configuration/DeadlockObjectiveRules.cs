using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public partial class DeadlockObjectiveRules : ObjectiveRules
{
    public const string CONTROL_TAG = "control";

    public override bool CanComplete => true;

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

    public int AbsenceDelayTicks => (int)(AbsenceDelay * SimConfig.TICK_RATE);
}
