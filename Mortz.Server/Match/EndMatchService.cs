using Mortz.Core.Chat;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Match;
using Mortz.Server.Admin;
using Mortz.Server.Chat;
using Mortz.Server.Hooks;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Match;

/// <summary>The admin END_MATCH command. Server lifetime because it owes a reply
/// when there is no match to end.</summary>
public sealed class EndMatchService(
    AdminService admin,
    ChatService chat,
    PhaseHost phases)
    : IHandle<Player, EndMatchRequestMsg>, IObservePhase
{
    private string? _pendingAnnounce;

    public void Handle(Player sender, in EndMatchRequestMsg message)
    {
        if (!admin.Authorize(sender, message))
            return;
        if (phases.Kind != ServerPhaseKind.MATCH)
        {
            chat.SendTo(sender.PeerId, new ChatLine.System("No match is running."));
            return;
        }

        _pendingAnnounce = sender.Name;
        phases.RequestReturnToLobby();
    }

    /// <summary>Deferred to the fresh lobby so the line lands in the lobby chats.</summary>
    public void PhaseChanged(ServerPhaseKind phaseKind)
    {
        if (phaseKind != ServerPhaseKind.LOBBY || _pendingAnnounce is not string name)
            return;
        _pendingAnnounce = null;
        chat.Broadcast(new ChatLine.System($"{name} ended the match."));
    }
}
