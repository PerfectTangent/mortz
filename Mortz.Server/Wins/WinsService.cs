using Mortz.Core.Features;
using Mortz.Protocol.Net.Stats;
using Mortz.Server.Hooks;
using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Serilog;

namespace Mortz.Server.Wins;

public sealed class WinsService(
    FeatureScope scope,
    Roster roster,
    IServerLink link,
    ILogger log) : IObserveMatchUpdate, ILoadPhase
{
    private readonly PlayerTable<Player, SessionWins> _wins = scope.Table<Player, SessionWins>();

    public void MatchUpdated(in MatchUpdate update) => Record(update.Winners);

    public void Load(Player player, PhaseLoad load)
    {
        if (load.Kind == ServerPhaseKind.LOBBY)
            link.Send(player.PeerId, SessionStatsProtocol.Encode(Table()));
    }

    public void Record(IReadOnlyList<Player> winners)
    {
        foreach (Player player in winners)
        {
            SessionWins wins = _wins[player];
            wins.Count++;
            log.Information("{PlayerName} now has {Wins} session win(s)", player.Name, wins.Count);
        }
    }

    private PeerWins[] Table()
    {
        PeerWins[] table = new PeerWins[roster.Count];
        int index = 0;
        foreach (Player player in roster)
        {
            table[index++] = new PeerWins(player.PeerId, _wins[player].Count);
        }
        return table;
    }
}
