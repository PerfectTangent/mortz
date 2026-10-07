using Mortz.Client.Players;

namespace Mortz.Client.Hooks;

/// <summary>A player was first referenced, or retired by a full table.</summary>
public interface IObservePlayers
{
    void PlayerJoined(ClientPlayer player);

    void PlayerLeft(ClientPlayer player);
}
