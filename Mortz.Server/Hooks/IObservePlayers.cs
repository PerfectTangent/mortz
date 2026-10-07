using Mortz.Server.Players;

namespace Mortz.Server.Hooks;

/// <summary>A player was admitted, or left.</summary>
public interface IObservePlayers
{
    void PlayerJoined(Player player);

    void PlayerLeft(Player player);
}
