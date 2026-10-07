using Mortz.Server.Players;

namespace Mortz.Server.Hooks;

/// <summary>Brings a late joiner into a match already in progress.</summary>
public interface ISyncJip
{
    void Sync(Player jipPlayer);
}
