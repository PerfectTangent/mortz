using Mortz.Core.Match.Scoring;
using Mortz.Protocol.Net.Match;
using Mortz.Server.Hooks;
using Mortz.Server.Players;
using Serilog;

namespace Mortz.Server.Match.Services;

/// <summary>Publishes the match-point presentation state and keeps it available for late joiners.</summary>
public class MatchPointService(IServerLink link, ILogger log) : IObserveMatchUpdate, ILoadPhase, ISyncJip
{
    public MatchPoint? Active { get; private set; }

    public void MatchUpdated(in MatchUpdate update)
    {
        if (update.MatchPoint == Active)
            return;

        Active = update.MatchPoint;
        log.Information("match point {MatchPointState}", Active != null ? "on" : "off");
        link.Broadcast(MatchProtocol.Encode(Active));
    }

    public void Load(Player player, PhaseLoad load)
    {
        if (load.Initial)
            SendCurrent(player);
    }

    public void Sync(Player jipPlayer) => SendCurrent(jipPlayer);

    private void SendCurrent(Player player)
    {
        if (Active != null)
            link.Send(player.PeerId, MatchProtocol.Encode(Active));
    }
}
