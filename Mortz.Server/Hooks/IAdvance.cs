namespace Mortz.Server.Hooks;

/// <summary>A server frame passed.</summary>
public interface IAdvance
{
    void Advance(ServerTime time);
}
