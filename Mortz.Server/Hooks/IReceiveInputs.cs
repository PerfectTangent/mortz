using Mortz.Server.Players;

namespace Mortz.Server.Hooks;

/// <summary>A raw input packet arrived while inputs are accepted.</summary>
public interface IReceiveInputs
{
    void Inputs(Player player, byte[] packet);
}
