using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

[ConfigVariant("fixed", "Fixed", typeof(FixedRespawnRules))]
[ConfigVariant("none", "None", typeof(NoRespawnRules))]
[ConfigVariant("escalating", "Escalating", typeof(EscalatingRespawnRules))]
public abstract class RespawnRules
{
    public abstract RespawnRulesSnapshot ToSnapshot();
}

public partial class FixedRespawnRules : RespawnRules
{
    [UiCategory("Respawn")]
    [UiProperty("Respawn Delay", min: 0, max: 60, step: 0.05f)]
    [MatchRule(min: 0, max: 60)]
    public float Delay { get; set; } = SimConfig.RESPAWN_DELAY;

    public int DelayTicks => (int)(Delay * SimConfig.TICK_RATE);
}

public partial class NoRespawnRules : RespawnRules;

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

    public string? ValidationError =>
        !float.IsFinite(BaseDelay) || BaseDelay < 0 ||
        !float.IsFinite(Increase) || Increase < 0 ||
        !float.IsFinite(MaximumDelay) || MaximumDelay * (double)SimConfig.TICK_RATE < 1 ||
        MaximumDelay * (double)SimConfig.TICK_RATE > ushort.MaxValue || BaseDelay > MaximumDelay
            ? "rules.respawn 'escalating' requires finite nonnegative base delay/increase, base delay <= maximum delay, and a maximum delay of 1..65535 ticks."
            : null;
}

public abstract record RespawnRulesSnapshot
{
    public abstract RespawnRules ToMutable();
}

public static class RespawnRulesProjection
{
    public static RespawnRulesSnapshot ToSnapshot(RespawnRules rules) => rules.ToSnapshot();
    public static RespawnRules ToMutable(RespawnRulesSnapshot snapshot) => snapshot.ToMutable();
    public static void Clamp(RespawnRules rules) => RespawnRulesMetadata.Clamp(rules);
}
