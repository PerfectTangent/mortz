using Mortz.Core.Match.Respawning;
using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

[ConfigVariant("fixed", "Fixed", typeof(FixedRespawnRules))]
[ConfigVariant("none", "None", typeof(NoRespawnRules))]
[ConfigVariant("escalating", "Escalating", typeof(EscalatingRespawnRules))]
public abstract class RespawnRules
{
    public abstract RespawnRulesSnapshot ToSnapshot();

    public virtual IEnumerable<string> Errors(ModeRules rules) => [];
}

public partial class FixedRespawnRules : RespawnRules
{
    [UiCategory("Respawn")]
    [UiProperty("Respawn Delay", min: 0, max: 60, step: 0.05f)]
    [MatchRule(min: 0, max: 60)]
    public float Delay { get; set; } = SimConfig.RESPAWN_DELAY;
}

public sealed partial record FixedRespawnRulesSnapshot
{
    public int DelayTicks => (int)(Delay * SimConfig.TICK_RATE);

    public override RespawnStrategy Create() => new FixedRespawnStrategy(DelayTicks);
}

public partial class NoRespawnRules : RespawnRules;

public sealed partial record NoRespawnRulesSnapshot
{
    public override RespawnStrategy Create() => new NoRespawnStrategy();
}

public partial class EscalatingRespawnRules : RespawnRules
{
    public const float MAXIMUM_DELAY_SECONDS = ushort.MaxValue / (float)SimConfig.TICK_RATE;

    [UiCategory("Respawn")]
    [UiProperty("Base Delay", min: 0, max: MAXIMUM_DELAY_SECONDS, step: 0.05f)]
    [MatchRule(min: 0, max: MAXIMUM_DELAY_SECONDS)]
    public float BaseDelay { get; set; } = 2;

    [UiProperty("Increase Per Death", min: 0, max: MAXIMUM_DELAY_SECONDS, step: 0.05f)]
    [MatchRule(min: 0, max: MAXIMUM_DELAY_SECONDS)]
    public float Increase { get; set; } = 1;

    [UiProperty("Maximum Delay", min: 1f / SimConfig.TICK_RATE, max: MAXIMUM_DELAY_SECONDS, step: 0.05f)]
    [MatchRule(min: 0, max: MAXIMUM_DELAY_SECONDS)]
    public float MaximumDelay { get; set; } = 10;

    public override IEnumerable<string> Errors(ModeRules rules) =>
        !float.IsFinite(BaseDelay) || BaseDelay < 0 ||
        !float.IsFinite(Increase) || Increase < 0 ||
        !float.IsFinite(MaximumDelay) || MaximumDelay * (double)SimConfig.TICK_RATE < 1 ||
        MaximumDelay * (double)SimConfig.TICK_RATE > ushort.MaxValue || BaseDelay > MaximumDelay
            ? ["rules.respawn 'escalating' requires finite nonnegative base delay/increase, base delay <= maximum delay, and a maximum delay of 1..65535 ticks."]
            : [];
}

public sealed partial record EscalatingRespawnRulesSnapshot
{
    public override RespawnStrategy Create() => new EscalatingRespawnStrategy(this);
}

public abstract record RespawnRulesSnapshot
{
    public abstract RespawnRules ToMutable();

    public abstract RespawnStrategy Create();
}
