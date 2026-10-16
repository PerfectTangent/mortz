using Mortz.Core.Features;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Chat;
using Mortz.Server.Hooks;
using Mortz.Server.Players;

namespace Mortz.Server.Chat;

public sealed class TypingService(FeatureScope scope, IServerLink link) : IHandle<Player, TypingMsg>, IObservePlayers
{
    private readonly PlayerTable<Player, TypingState> _typing = scope.Table<Player, TypingState>();

    public void Handle(Player sender, in TypingMsg message)
    {
        if (_typing[sender].Set(message.IsTyping))
            link.Broadcast(new TypingStateMsg(sender.PeerId, message.IsTyping));
    }

    public void PlayerLeft(Player player)
    {
        if (_typing[player].Set(false))
            link.Broadcast(new TypingStateMsg(player.PeerId, false));
    }
}
