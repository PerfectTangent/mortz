using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;

namespace Mortz.Core.Match.Respawning;

public class EscalatingRespawnStrategy(EscalatingRespawnRulesSnapshot rules) : RespawnStrategy
{
    private readonly Dictionary<int, (ulong Deaths, int? ReturnTick)> _players = [];

    public int DelayTicks(ulong previousDeaths)
    {
        double seconds = Math.Min(rules.MaximumDelay,
            rules.BaseDelay + (double)rules.Increase * previousDeaths);
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
