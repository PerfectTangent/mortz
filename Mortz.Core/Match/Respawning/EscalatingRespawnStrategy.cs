using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;

namespace Mortz.Core.Match.Respawning;

public class EscalatingRespawnStrategy : RespawnStrategy
{
    private readonly EscalatingRespawnRulesSnapshot _rules;
    private readonly Dictionary<int, (ulong Deaths, int? ReturnTick)> _players = [];

    public EscalatingRespawnStrategy(EscalatingRespawnRules rules)
    {
        if (rules.ValidationError is { } error)
        {
            throw new ArgumentException(error, nameof(rules));
        }
        _rules = rules.ToSnapshot();
    }

    public int DelayTicks(ulong previousDeaths)
    {
        double seconds = Math.Min(_rules.MaximumDelay,
            _rules.BaseDelay + (double)_rules.Increase * previousDeaths);
        // Match fixed respawns' seconds-to-ticks rounding after the wide, capped calculation.
        return Math.Max(1, (int)((float)seconds * SimConfig.TICK_RATE));
    }

    public override void Died(Death death, int tick)
    {
        (ulong deaths, _) = _players.GetValueOrDefault(death.PeerId);
        int at = checked(tick + DelayTicks(deaths));
        _players[death.PeerId] = (deaths == ulong.MaxValue ? deaths : deaths + 1, at);
    }

    public override int? ReturnTick(int peerId, int tick) =>
        _players.GetValueOrDefault(peerId).ReturnTick;

    public override void Spawned(int peerId, bool initial)
    {
        (ulong deaths, _) = _players.GetValueOrDefault(peerId);
        _players[peerId] = (deaths, null);
    }

    public override void Removed(int peerId) => _players.Remove(peerId);
}
